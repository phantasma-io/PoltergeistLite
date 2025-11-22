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
    /// Builds burn transaction drafts for fungible tokens and NFTs.
    /// </summary>
    public sealed class WalletBurnService
    {
        private readonly Func<AccountManager> _accountProvider;
        private readonly WalletNftTransactionBuilder _nftBuilder;
        private readonly WalletFeeRequirement _feeRequirement;

        public WalletBurnService(Func<AccountManager> accountProvider, WalletNftTransactionBuilder nftBuilder, WalletFeeRequirement feeRequirement)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
            _nftBuilder = nftBuilder ?? throw new ArgumentNullException(nameof(nftBuilder));
            _feeRequirement = feeRequirement ?? throw new ArgumentNullException(nameof(feeRequirement));
        }

        public ValidationResult<WalletTransactionDraft> PrepareFungibleBurn(string symbol, decimal availableAmount, decimal requestedAmount)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return ValidationResult<WalletTransactionDraft>.Fail("Account manager is not available yet.");
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                return ValidationResult<WalletTransactionDraft>.Fail($"Current platform must be {PlatformKind.Phantasma}");
            }

            var state = accountManager.CurrentState;
            if (state == null)
            {
                return ValidationResult<WalletTransactionDraft>.Fail("Account state is unavailable.");
            }

            if (requestedAmount <= 0 || requestedAmount > availableAmount)
            {
                return ValidationResult<WalletTransactionDraft>.Fail("Invalid burn amount.");
            }

            var feeCheck = EnsureKcal(accountManager, 0.1m);
            if (!feeCheck.Success)
            {
                return ValidationResult<WalletTransactionDraft>.Fail(feeCheck.Error);
            }

            var draftResult = BuildFungibleBurnDraft(symbol, requestedAmount, state, accountManager);
            if (!draftResult.Success)
            {
                return ValidationResult<WalletTransactionDraft>.Fail(draftResult.Error);
            }

            var message = $"Are you sure you want to burn {draftResult.Amount} {symbol} tokens?";
            return ValidationResult<WalletTransactionDraft>.Ok(draftResult.Draft, message);
        }

        public WalletTransactionDraftResult BuildFungibleBurnDraft(string symbol, decimal amount)
        {
            var accountManager = _accountProvider();
            var state = accountManager?.CurrentState;
            return BuildFungibleBurnDraft(symbol, amount, state, accountManager);
        }

        private WalletTransactionDraftResult BuildFungibleBurnDraft(string symbol, decimal amount, AccountState state, AccountManager accountManager)
        {
            if (accountManager == null)
            {
                return WalletTransactionDraftResult.Fail("Account manager is not available yet.");
            }

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

        public ValidationResult<WalletTransactionDraft> PrepareNftBurn(string symbol, IEnumerable<string> nftIds)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return ValidationResult<WalletTransactionDraft>.Fail("Account manager is not available yet.");
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                return ValidationResult<WalletTransactionDraft>.Fail($"Current platform must be {PlatformKind.Phantasma}");
            }

            var state = accountManager.CurrentState;
            if (state == null)
            {
                return ValidationResult<WalletTransactionDraft>.Fail("Account state is unavailable.");
            }

            var ids = nftIds?.Where(x => !string.IsNullOrEmpty(x)).ToList() ?? new List<string>();
            if (ids.Count == 0)
            {
                return ValidationResult<WalletTransactionDraft>.Fail("No NFTs selected.");
            }

            var feeCheck = EnsureKcal(accountManager, 0.1m);
            if (!feeCheck.Success)
            {
                return ValidationResult<WalletTransactionDraft>.Fail(feeCheck.Error);
            }

            var target = Address.Parse(state.address);
            byte[] script;
            try
            {
                script = _nftBuilder.BuildBurnScript(symbol, ids, target);
            }
            catch (Exception e)
            {
                return ValidationResult<WalletTransactionDraft>.Fail($"Failed to build NFT burn transaction.\n{e.Message}");
            }

            var plan = WalletTransactionDraft.ForSingleScript($"Burn {ids.Count} {symbol} NFTs", script, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None);
            var message = $"Are you sure you want to burn (destroy) {ids.Count} {symbol} NFTs?";
            return ValidationResult<WalletTransactionDraft>.Ok(plan, message);
        }

        public ValidationResult EnsureKcal(AccountManager accountManager, decimal minAmount)
        {
            var result = ValidationResult.Ok();
            _feeRequirement.EnsureKcal(minAmount, (feeResult, error) =>
            {
                if (feeResult != PromptResult.Success)
                {
                    result = ValidationResult.Fail(string.IsNullOrEmpty(error) ? "KCAL is required to make transactions!" : error);
                }
            });

            return result;
        }
    }
}
