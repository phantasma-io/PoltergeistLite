using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.VM;
using Poltergeist;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Builds burn transaction plans for fungible tokens and NFTs.
    /// </summary>
    public sealed class WalletBurnService
    {
        private readonly Func<AccountManager> _accountProvider;
        private readonly WalletNftTransactionBuilder _nftBuilder;

        public WalletBurnService(Func<AccountManager> accountProvider, WalletNftTransactionBuilder nftBuilder)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
            _nftBuilder = nftBuilder ?? throw new ArgumentNullException(nameof(nftBuilder));
        }

        public WalletTransactionDraftResult BuildFungibleBurnPlan(string symbol, decimal amount)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return WalletTransactionDraftResult.Fail("Account manager is not available yet.");
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                return WalletTransactionDraftResult.Fail($"Current platform must be {PlatformKind.Phantasma}");
            }

            var state = accountManager.CurrentState;
            if (state == null)
            {
                return WalletTransactionDraftResult.Fail("Account state is unavailable.");
            }

            var balance = state.balances?.FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            if (balance == null)
            {
                return WalletTransactionDraftResult.Fail($"{symbol} balance is not available.");
            }

            var burnAmount = amount;
            if (burnAmount > balance.Available && !(accountManager.Settings.devMode && accountManager.Settings.devMode_NoValidation))
            {
                burnAmount = balance.Available;
            }

            if (burnAmount <= 0)
            {
                return WalletTransactionDraftResult.Fail($"Not enough {symbol} to burn.");
            }

            var decimals = Tokens.GetTokenDecimals(symbol, accountManager.CurrentPlatform);
            var target = Address.Parse(state.address);

            var sb = new ScriptBuilder();
            sb.AllowGas(target, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
            sb.CallInterop("Runtime.BurnTokens", target, symbol, UnitConversion.ToBigInteger(burnAmount, decimals));
            sb.SpendGas(target);
            var script = sb.EndScript();

            var plan = WalletTransactionDraft.ForSingleScript($"Burn {burnAmount} {symbol} tokens", script, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None);
            return WalletTransactionDraftResult.CreateSuccess(plan, burnAmount);
        }

        public WalletTransactionDraftResult BuildNftBurnPlan(string symbol, IEnumerable<string> nftIds)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return WalletTransactionDraftResult.Fail("Account manager is not available yet.");
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                return WalletTransactionDraftResult.Fail($"Current platform must be {PlatformKind.Phantasma}");
            }

            var state = accountManager.CurrentState;
            if (state == null)
            {
                return WalletTransactionDraftResult.Fail("Account state is unavailable.");
            }

            var ids = nftIds?.Where(x => !string.IsNullOrEmpty(x)).ToList() ?? new List<string>();
            if (ids.Count == 0)
            {
                return WalletTransactionDraftResult.Fail("No NFTs selected.");
            }

            var target = Address.Parse(state.address);
            byte[] script;
            try
            {
                script = _nftBuilder.BuildBurnScript(symbol, ids, target);
            }
            catch (Exception e)
            {
                return WalletTransactionDraftResult.Fail($"Failed to build NFT burn transaction.\n{e.Message}");
            }

            var plan = WalletTransactionDraft.ForSingleScript($"Burn {ids.Count} {symbol} NFTs", script, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None);
            return WalletTransactionDraftResult.CreateSuccess(plan, ids.Count);
        }
    }
}
