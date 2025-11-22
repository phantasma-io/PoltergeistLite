using System;
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
    /// Builds staking/claiming transaction plans so UI layers stay thin.
    /// </summary>
    public sealed class WalletStakeService
    {
        private readonly Func<AccountManager> _accountProvider;

        public WalletStakeService(Func<AccountManager> accountProvider)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public WalletTransactionDraftResult BuildStakePlan(decimal requestedAmount)
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

            var balance = state.balances?.FirstOrDefault(x => string.Equals(x.Symbol, DomainSettings.StakingTokenSymbol, StringComparison.OrdinalIgnoreCase));
            if (balance == null)
            {
                return WalletTransactionDraftResult.Fail("SOUL balance is not available.");
            }

            var amount = requestedAmount;
            if (amount > balance.Available && !(accountManager.Settings.devMode && accountManager.Settings.devMode_NoValidation))
            {
                amount = balance.Available;
            }

            if (amount <= 0)
            {
                return WalletTransactionDraftResult.Fail("Not enough SOUL to stake.");
            }

            var address = Address.Parse(state.address);
            var decimals = Tokens.GetTokenDecimals(DomainSettings.StakingTokenSymbol, accountManager.CurrentPlatform);

            var sb = new ScriptBuilder();
            sb.AllowGas(address, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
            sb.CallContract("stake", "Stake", address, UnitConversion.ToBigInteger(amount, decimals));
            sb.SpendGas(address);
            var script = sb.EndScript();

            var plan = WalletTransactionDraft.ForSingleScript($"Stake {amount} SOUL", script, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None);
            return WalletTransactionDraftResult.CreateSuccess(plan, amount);
        }

        public WalletTransactionDraftResult BuildUnstakePlan(decimal amount)
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

            var decimals = Tokens.GetTokenDecimals(DomainSettings.StakingTokenSymbol, accountManager.CurrentPlatform);
            var address = Address.Parse(state.address);

            var sb = new ScriptBuilder();
            sb.AllowGas(address, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
            sb.CallContract("stake", "Unstake", address, UnitConversion.ToBigInteger(amount, decimals));
            sb.SpendGas(address);
            var script = sb.EndScript();

            var plan = WalletTransactionDraft.ForSingleScript($"Unstake {amount} SOUL", script, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None);
            return WalletTransactionDraftResult.CreateSuccess(plan, amount);
        }

        public WalletTransactionDraftResult BuildClaimKcalPlan(decimal claimableAmount)
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

            var address = Address.Parse(state.address);
            var sb = new ScriptBuilder();

            sb.AllowGas(address, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
            sb.CallContract("stake", "Claim", address, address);
            sb.SpendGas(address);

            var script = sb.EndScript();
            var plan = WalletTransactionDraft.ForSingleScript($"Claim {claimableAmount} KCAL", script, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None);
            return WalletTransactionDraftResult.CreateSuccess(plan, claimableAmount);
        }

        public WalletTransactionDraftResult BuildClaimSmRewardPlan()
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

            var address = Address.Parse(state.address);

            var sb = new ScriptBuilder();
            sb.AllowGas(address, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
            sb.CallContract("stake", "MasterClaim", address);
            sb.SpendGas(address);
            var script = sb.EndScript();

            var plan = WalletTransactionDraft.ForSingleScript("Claim SM reward", script, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None);
            return WalletTransactionDraftResult.CreateSuccess(plan);
        }
    }
}
