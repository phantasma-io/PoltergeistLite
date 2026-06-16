using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.VM;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Core.Extensions;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Unity.Core;
using PhantasmaPhoenix.NFT;
using PhantasmaPhoenix.NFT.Extensions;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Unity.Core.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Numerics;

namespace Poltergeist
{
    public partial class AccountManager
    {
        public void RefreshBalances(bool force, PlatformKind platforms = PlatformKind.None, Action callback = null)
        {
            async Task ExecuteAsync()
            {
                if (!HasSelection)
                {
                    Log.WriteWarning("RefreshBalances: skipped because no account is selected.");
                    return;
                }

                var currentAccount = CurrentAccount;
                if (currentAccount.passwordProtected && string.IsNullOrEmpty(CurrentPasswordHash))
                {
                    Log.WriteWarning("RefreshBalances: skipped because current account is locked.");
                    return;
                }

                // Avoid refreshing while token list is being rebuilt (e.g., after network change); schedule a retry once tokens are ready.
                // We need token metadata (decimals, flags) to parse balances safely; when tokens are empty or reinit is running, bail out.
                var tokensReady = Tokens.GetTokens().Length > 0;
                if (!tokensReady || tokensReinitInProgress)
                {
                    ScheduleBalanceRefreshAfterTokens(force, platforms, callback);
                    if (!tokensReinitInProgress)
                    {
                        TokensReinit();
                    }

                    Log.Write("[Balances] Refresh skipped: tokens not ready, will retry after token reload.");
                    return;
                }

                var session = CaptureAccountSession();
                List<PlatformKind> platformsList;
                if (platforms == PlatformKind.None)
                    platformsList = currentAccount.platforms.Split();
                else
                    platformsList = platforms.Split();

                lock (_refreshStatus)
                {
                    RefreshStatus refreshStatus;
                    var now = DateTime.UtcNow;
                    if (_refreshStatus.ContainsKey(PlatformKind.Phantasma))
                    {
                        refreshStatus = _refreshStatus[PlatformKind.Phantasma];

                        refreshStatus.BalanceRefreshing = true;
                        refreshStatus.LastBalanceRefresh = now;
                        refreshStatus.BalanceRefreshCallback = callback;
                        refreshStatus.BalanceError = null;

                        _refreshStatus[PlatformKind.Phantasma] = refreshStatus;
                    }
                    else
                    {
                        _refreshStatus.Add(PlatformKind.Phantasma,
                            new RefreshStatus
                            {
                                BalanceRefreshing = true,
                                LastBalanceRefresh = now,
                                BalanceRefreshCallback = callback,
                                BalanceError = null,
                                HistoryRefreshing = false,
                                LastHistoryRefresh = DateTime.MinValue
                            });
                    }
                }

                Log.Write($"[Balances] RefreshBalances start force={force} currentPlatform={CurrentPlatform} targets={string.Join(',', platformsList)}"); //TODO Check if still needed once refactoring is over
                foreach (var platform in platformsList)
                {
                    BalancesRefreshStarted?.Invoke(platform);
                }

                var wif = CurrentWif;
                var keys = PhantasmaKeys.FromWIF(wif);
                var ethKeys = PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.FromWIF(wif);
                UpdateOpenAccount();
                try
                {
                    var acc = await AsyncPhantasma.FromApi<PhantasmaPhoenix.RPC.Models.AccountResult>(
                        (onSuccess, onError) => phantasmaApi.GetAccount(
                            keys.Address.Text,
                            onSuccess,
                            onError,
                            timeout: WebClient.DefaultTimeout,
                            retries: NetworkRetryPolicy.Retries),
                        session.Token);

                    if (!IsSessionCurrent(session, PlatformKind.Phantasma, acc?.Address))
                    {
                        return;
                    }

                    var balanceMap = new Dictionary<string, Balance>();
                    HashSet<string> missingTokens = null;

                    foreach (var entry in acc.Balances)
                    {
                        var token = Tokens.GetToken(entry.Symbol, PlatformKind.Phantasma);
                        var decimals = token?.Decimals ?? 8;
                        var availableAmount = ParseTokenAmount(entry.Amount, decimals);

                        if (token == null)
                        {
                            missingTokens ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            missingTokens.Add(entry.Symbol);
                        }

                        balanceMap[entry.Symbol] = new Balance()
                        {
                            Symbol = entry.Symbol,
                            Available = availableAmount,
                            Staked = BigInteger.Zero,
                            Claimable = BigInteger.Zero,
                            Chain = entry.Chain,
                            Decimals = decimals,
                            Burnable = token?.IsBurnable() ?? true,
                            Fungible = token?.IsFungible() ?? true,
                            Ids = entry.Ids
                        };

                    }

                    var soulDecimals = Tokens.GetTokenDecimals("SOUL", PlatformKind.Phantasma);
                    var kcalDecimals = Tokens.GetTokenDecimals("KCAL", PlatformKind.Phantasma);

                    var stakedAmount = ParseTokenAmount(acc.Stakes.Amount, soulDecimals);
                    var claimableAmount = ParseTokenAmount(acc.Stakes.Unclaimed, kcalDecimals);

                    var stakeTimestamp = new Timestamp(acc.Stakes.Time);

                    if (stakedAmount > 0)
                    {
                        var symbol = "SOUL";
                        if (balanceMap.ContainsKey(symbol))
                        {
                            var entry = balanceMap[symbol];
                            entry.Staked = stakedAmount;
                        }
                        else
                        {
                            var token = Tokens.GetToken(symbol, PlatformKind.Phantasma);
                            var entry = new Balance()
                            {
                                Symbol = symbol,
                                Chain = "main",
                                Staked = stakedAmount,
                                Claimable = BigInteger.Zero,
                                Decimals = token.Decimals,
                                Burnable = token.IsBurnable(),
                                Fungible = token.IsFungible()
                            };
                            balanceMap[symbol] = entry;
                        }
                    }

                    if (claimableAmount > 0)
                    {
                        var symbol = "KCAL";
                        if (balanceMap.ContainsKey(symbol))
                        {
                            var entry = balanceMap[symbol];
                            entry.Claimable = claimableAmount;
                        }
                        else
                        {
                            var token = Tokens.GetToken(symbol, PlatformKind.Phantasma);
                            var entry = new Balance()
                            {
                                Symbol = symbol,
                                Chain = "main",
                                Staked = BigInteger.Zero,
                                Claimable = claimableAmount,
                                Decimals = token.Decimals,
                                Burnable = token.IsBurnable(),
                                Fungible = token.IsFungible()
                            };
                            balanceMap[symbol] = entry;
                        }
                    }

                    balanceMap = balanceMap
                        .OrderBy(b => b.Key != "SOUL")
                        .ThenBy(b => b.Key != "KCAL")
                        .ThenBy(b => b.Key)
                        .ToDictionary(kv => kv.Key, kv => kv.Value);

                    var state = new AccountState()
                    {
                        platform = PlatformKind.Phantasma,
                        address = acc.Address,
                        name = acc.Name,
                        balances = balanceMap.Values.ToArray(),
                        flags = AccountFlags.None
                    };

                    var soulMasterThreshold = WalletAmountParser.FromDecimal(SoulMasterStakeAmount, soulDecimals);
                    if (stakedAmount >= soulMasterThreshold)
                    {
                        state.flags |= AccountFlags.Master;
                    }

                    if (acc.Validator.Equals("Primary") || acc.Validator.Equals("Secondary"))
                    {
                        state.flags |= AccountFlags.Validator;
                    }

                    state.stakeTime = stakeTimestamp;

                    state.usedStorage = acc.Storage.Used;
                    state.availableStorage = acc.Storage.Available;
                    state.archives = acc.Storage.Archives;
                    state.avatarData = acc.Storage.Avatar;

                    ReportWalletBalance(PlatformKind.Phantasma, state, session);
                    SetBalanceError(PlatformKind.Phantasma, null, session);

                    if (missingTokens != null && missingTokens.Count > 0)
                    {
                        Log.WriteWarning($"RefreshBalances: detected unknown tokens ({string.Join(", ", missingTokens)}) - reloading token list.");
                        ScheduleBalanceRefreshAfterTokens();
                        TokensReinit();
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (PhantasmaRequestException ex)
                {
                    Log.WriteWarning($"RefreshBalances[PHA] {ex.ErrorType}: {ex.Message}");

                    RotateRpcOnWebError(ex);

                    SetBalanceError(PlatformKind.Phantasma, $"Phantasma request failed: {ex.Message}", session);
                    ReportWalletBalance(PlatformKind.Phantasma, null, session);
                }
                catch (FormatException ex)
                {
                    Log.WriteWarning($"RefreshBalances[PHA] parse error: {ex.Message}");
                    SetBalanceError(PlatformKind.Phantasma, $"Error while parsing balances: {ex.Message}", session);
                    ReportWalletBalance(PlatformKind.Phantasma, null, session);
                }
                catch (Exception ex)
                {
                    Log.WriteWarning($"RefreshBalances[PHA] unexpected error: {ex}");
                    SetBalanceError(PlatformKind.Phantasma, $"Error while fetching balances: {ex.Message}", session);
                    ReportWalletBalance(PlatformKind.Phantasma, null, session);
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

        public void RefreshHistory(bool force, PlatformKind platforms = PlatformKind.None)
        {
            async Task ExecuteAsync()
            {
                if (!HasSelection)
                {
                    Log.WriteWarning("RefreshHistory: skipped because no account is selected.");
                    return;
                }

                var currentAccount = CurrentAccount;
                if (currentAccount.passwordProtected && string.IsNullOrEmpty(CurrentPasswordHash))
                {
                    Log.WriteWarning("RefreshHistory: skipped because current account is locked.");
                    return;
                }

                var session = CaptureAccountSession();
                var accountName = string.IsNullOrEmpty(currentAccount.name) ? "(unknown)" : currentAccount.name;
                Log.Write($"[History] RefreshHistory start force={force} currentAccount={accountName} currentPlatform={CurrentPlatform} platformsArg={platforms}");
                List<PlatformKind> platformsList;
                if (platforms == PlatformKind.None)
                    platformsList = CurrentAccount.platforms.Split();
                else
                    platformsList = platforms.Split();

                lock (_refreshStatus)
                {
                    RefreshStatus refreshStatus;
                    var now = DateTime.UtcNow;
                    if (_refreshStatus.ContainsKey(PlatformKind.Phantasma))
                    {
                        refreshStatus = _refreshStatus[PlatformKind.Phantasma];

                        refreshStatus.HistoryRefreshing = true;
                        refreshStatus.LastHistoryRefresh = now;

                        _refreshStatus[PlatformKind.Phantasma] = refreshStatus;
                    }
                    else
                    {
                        _refreshStatus.Add(PlatformKind.Phantasma,
                            new RefreshStatus
                            {
                                BalanceRefreshing = false,
                                LastBalanceRefresh = DateTime.MinValue,
                                BalanceRefreshCallback = null,
                                HistoryRefreshing = true,
                                LastHistoryRefresh = now
                            });
                    }
                }

                foreach (var platform in platformsList)
                {
                    HistoryRefreshStarted?.Invoke(platform);
                }

                var wif = this.CurrentWif;
                accountHistoryNotLoaded = false; // First refresh attempt for this wallet has been triggered.

                var keys = PhantasmaKeys.FromWIF(wif);
                try
                {
                    var result = await AsyncPhantasma.FromApi<AccountTransactionsResult, uint, uint>(
                        (onSuccess, onError) => phantasmaApi.GetAddressTransactions(
                            keys.Address.Text,
                            1,
                            20,
                            onSuccess,
                            onError,
                            timeout: WebClient.DefaultTimeout,
                            retries: NetworkRetryPolicy.Retries),
                        session.Token);
                    var (transactions, _, _) = result;

                    if (!IsSessionCurrent(session))
                    {
                        return;
                    }

                    var history = new List<HistoryEntry>();

                    foreach (var tx in transactions.Txs)
                    {
                        history.Add(new HistoryEntry()
                        {
                            hash = tx.Hash,
                            date = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(tx.Timestamp).ToLocalTime(),
                            url = GetPhantasmaTransactionURL(tx.Hash)
                        });
                    }

                    var platformsLabel = string.Join(",", platformsList);
                    Log.Write($"[History] RefreshHistory success txCount={history.Count} platforms={platformsLabel}");
                    ReportWalletHistory(PlatformKind.Phantasma, history, session);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (PhantasmaRequestException ex)
                {
                    RotateRpcOnWebError(ex);
                    Log.WriteWarning($"[History] RefreshHistory failed (SDK) {ex.ErrorType}: {ex.Message}");
                    ReportWalletHistory(PlatformKind.Phantasma, null, session);
                }
                catch (Exception ex)
                {
                    // Ensure UI does not stay stuck in a perpetual refresh state on unexpected failures.
                    Log.WriteWarning($"RefreshHistory[PHA] unexpected error: {ex}");
                    ReportWalletHistory(PlatformKind.Phantasma, null, session);
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

    }
}
