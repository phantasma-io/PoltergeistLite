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
        private void ReportWalletBalance(PlatformKind platform, AccountState state, AccountSession session)
        {
            try
            {
                if (!IsSessionCurrent(session, platform, state?.address))
                {
                    return;
                }

                RefreshStatus refreshStatus;
                lock (_refreshStatus)
                {
                    refreshStatus = _refreshStatus.ContainsKey(platform)
                        ? _refreshStatus[platform]
                        : new RefreshStatus();
                    refreshStatus.BalanceRefreshing = false;
                    _refreshStatus[platform] = refreshStatus;
                }

                Log.Write($"[Balances] ReportWalletBalance platform={platform} stateNull={state == null}"); //TODO Check if still needed once refactoring is over

                if (state != null)
                {
                    Log.Write("Received new state for " + platform);

                    if (_states.TryGetValue(platform, out var previousState) && previousState?.dappTokens != null)
                    {
                        foreach (var entry in previousState.dappTokens)
                        {
                            if (!state.dappTokens.ContainsKey(entry.Key))
                            {
                                state.dappTokens[entry.Key] = entry.Value;
                            }
                        }
                    }

                    _states[platform] = state;
                }

                var temp = refreshStatus.BalanceRefreshCallback;
                lock (_refreshStatus)
                {
                    refreshStatus.BalanceRefreshCallback = null;
                    _refreshStatus[platform] = refreshStatus;
                }
                temp?.Invoke();

                Log.Write($"[Balances] Invoking BalancesUpdated for {platform} (subscribers: {BalancesUpdated?.GetInvocationList()?.Length ?? 0})"); //TODO Check if still needed once refactoring is over
                BalancesUpdated?.Invoke(platform);
            }
            catch (Exception) { } // This fixes crash when user leaves account fast without waiting for balances to load
        }

        // log=false is used for progressive updates to avoid flooding the log.
        private void ReportWalletNft(PlatformKind platform, string symbol, AccountSession session, bool log = true)
        {
            if (!IsSessionCurrent(session))
            {
                return;
            }

            var normalizedSymbol = NormalizeNftSymbol(symbol);
            var key = (platform, normalizedSymbol);
            int? nftCount = null;
            if (!string.IsNullOrEmpty(normalizedSymbol))
            {
                lock (_nftCacheLock)
                {
                    if (_nfts.TryGetValue(key, out var nfts) && nfts != null)
                    {
                        nftCount = nfts.Count;
                    }
                }
            }

            if (nftCount.HasValue)
            {
                if (log)
                {
                    Log.Write($"Received {nftCount.Value} new {symbol} NFTs for {platform}");
                }

                if (CurrentPlatform == PlatformKind.None)
                {
                    CurrentPlatform = platform;
                }

                if (log)
                {
                    Log.Write($"[NFT] Invoking NftsUpdated for {platform} {symbol} (subscribers: {NftsUpdated?.GetInvocationList()?.Length ?? 0})"); //TODO Check if still needed once refactoring is over
                }
                NftsUpdated?.Invoke(platform, symbol);
            }
        }

        private void ReportWalletHistory(PlatformKind platform, List<HistoryEntry> history, AccountSession session)
        {
            try
            {
                if (!IsSessionCurrent(session))
                {
                    return;
                }

                lock (_refreshStatus)
                {
                    if (!_refreshStatus.TryGetValue(platform, out var refreshStatus))
                    {
                        refreshStatus = new RefreshStatus();
                        Log.Write($"[History] ReportWalletHistory: created missing RefreshStatus for {platform}");
                    }
                    refreshStatus.HistoryRefreshing = false;
                    _refreshStatus[platform] = refreshStatus;
                }

                Log.Write($"[History] ReportWalletHistory platform={platform} historyNull={history == null}"); //TODO Check if still needed once refactoring is over

                if (history != null)
                {
                    Log.Write("Received new history for " + platform);
                    _history[platform] = history.ToArray();

                    if (CurrentPlatform == PlatformKind.None)
                    {
                        CurrentPlatform = platform;
                    }
                }

                Log.Write($"[History] Invoking HistoryUpdated for {platform} (subscribers: {HistoryUpdated?.GetInvocationList()?.Length ?? 0})"); //TODO Check if still needed once refactoring is over
                HistoryUpdated?.Invoke(platform);
            }
            catch (Exception) { } // This fixes crash when user leaves account fast without waiting for balances to load
        }


    }
}
