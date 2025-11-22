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
    /// Builds staking/claiming transaction drafts so UI layers stay thin.
    /// </summary>
    public sealed class WalletStakeService
    {
        private readonly Func<AccountManager> _accountProvider;
        private const uint UnstakeCooldownSeconds = 86400;

        public WalletStakeService(Func<AccountManager> accountProvider)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public ValidationResult GetUnstakeAvailability()
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return ValidationResult.Fail("Account manager is not available yet.");
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                return ValidationResult.Fail($"Current platform must be {PlatformKind.Phantasma}");
            }

            var state = accountManager.CurrentState;
            if (state == null)
            {
                return ValidationResult.Fail("Account state is unavailable.");
            }

            var canUnstake = (Timestamp.Now - state.stakeTime) >= UnstakeCooldownSeconds;
            if (!canUnstake)
            {
                return ValidationResult.Fail("You can unstake only after 24 hours from staking.");
            }

            return ValidationResult.Ok();
        }

        public ValidationResult<string> BuildUnstakeMessage(decimal requestedAmount)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return ValidationResult<string>.Fail("Account manager is not available yet.");
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                return ValidationResult<string>.Fail($"Current platform must be {PlatformKind.Phantasma}");
            }

            var state = accountManager.CurrentState;
            if (state == null)
            {
                return ValidationResult<string>.Fail("Account state is unavailable.");
            }

            var availability = GetUnstakeAvailability();
            if (!availability.Success)
            {
                return ValidationResult<string>.Fail(availability.Error);
            }

            var stakingBalance = state.balances?.FirstOrDefault(x => string.Equals(x.Symbol, DomainSettings.StakingTokenSymbol, StringComparison.OrdinalIgnoreCase));
            if (stakingBalance == null || stakingBalance.Staked <= 0)
            {
                return ValidationResult<string>.Fail("No SOUL is currently staked.");
            }

            if (requestedAmount <= 0 || requestedAmount > stakingBalance.Staked)
            {
                return ValidationResult<string>.Fail("Invalid unstake amount.");
            }

            var kcalBalance = state.balances?.FirstOrDefault(s => s.Symbol == "KCAL");
            var kcalClaimable = kcalBalance?.Claimable ?? 0;

            var message = $"Do you want to unstake {requestedAmount} SOUL?";

            if (kcalClaimable > 0)
            {
                message += $"\n\nAll unclaimed KCAL will be claimed: {WalletAmountFormatter.Format(kcalClaimable, kcalClaimable >= 1 ? MoneyFormatType.Standard : MoneyFormatType.Long)} KCAL.";
            }

            var nameRegistered = !string.Equals(state.name, ValidationUtils.ANONYMOUS_NAME, StringComparison.OrdinalIgnoreCase);
            if (requestedAmount > stakingBalance.Staked - 2 && nameRegistered)
            {
                message += "\n\nYour account will also lose the current registered name.\nKeep 2 SOUL staked if you want to keep your registered name.";
            }

            return ValidationResult<string>.Ok(message, message);
        }

        public ValidationResult<string> BuildClaimKcalMessage(decimal claimableAmount)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return ValidationResult<string>.Fail("Account manager is not available yet.");
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                return ValidationResult<string>.Fail($"Current platform must be {PlatformKind.Phantasma}");
            }

            if (claimableAmount <= 0)
            {
                return ValidationResult<string>.Fail("No KCAL available to claim.");
            }

            var message = $"Do you want to claim KCAL?\nThere is {claimableAmount} KCAL available.\n\nPlease note, after claiming KCAL you won't be able to unstake SOUL for next 24 hours.";
            return ValidationResult<string>.Ok(message, message);
        }

        public WalletTransactionDraftResult BuildStakeDraft(decimal requestedAmount)
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

        public WalletTransactionDraftResult BuildUnstakeDraft(decimal amount)
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

        public WalletTransactionDraftResult BuildClaimKcalDraft(decimal claimableAmount)
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

        public WalletTransactionDraftResult BuildClaimSmRewardDraft()
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
