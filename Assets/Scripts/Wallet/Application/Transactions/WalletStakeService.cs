using System;
using System.Linq;
using System.Numerics;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.VM;

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

        public ValidationResult<string> BuildUnstakeMessage(BigInteger requestedAmount)
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
            var stakedAmount = stakingBalance?.Staked ?? BigInteger.Zero;
            if (stakingBalance == null || stakedAmount <= 0)
            {
                return ValidationResult<string>.Fail("No SOUL is currently staked.");
            }

            if (requestedAmount <= 0 || requestedAmount > stakedAmount)
            {
                return ValidationResult<string>.Fail("Invalid unstake amount.");
            }

            var kcalBalance = state.balances?.FirstOrDefault(s => s.Symbol == DomainSettings.FuelTokenSymbol);
            var kcalClaimable = kcalBalance?.Claimable ?? BigInteger.Zero;
            var soulDecimals = Tokens.GetTokenDecimals(DomainSettings.StakingTokenSymbol, accountManager.CurrentPlatform);
            var kcalDecimals = Tokens.GetTokenDecimals(DomainSettings.FuelTokenSymbol, accountManager.CurrentPlatform);

            var message = $"Do you want to unstake {WalletAmountFormatter.Format(requestedAmount, soulDecimals)} SOUL?";

            if (kcalClaimable > 0)
            {
                message += $"\n\nAll unclaimed KCAL will be claimed: {WalletAmountFormatter.Format(kcalClaimable, kcalDecimals)} KCAL.";
            }

            var nameRegistered = !string.Equals(state.name, ValidationUtils.ANONYMOUS_NAME, StringComparison.OrdinalIgnoreCase);
            var twoSoul = WalletAmountParser.Parse("2", soulDecimals);
            if (requestedAmount > stakedAmount - twoSoul && nameRegistered)
            {
                message += "\n\nYour account will also lose the current registered name.\nKeep 2 SOUL staked if you want to keep your registered name.";
            }

            return ValidationResult<string>.Ok(message, message);
        }

        public ValidationResult<string> BuildClaimKcalMessage(BigInteger claimableAmount)
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

            var decimals = Tokens.GetTokenDecimals(DomainSettings.FuelTokenSymbol, accountManager.CurrentPlatform);
            var message = $"Do you want to claim KCAL?\nThere is {WalletAmountFormatter.Format(claimableAmount, decimals)} KCAL available.\n\nPlease note, after claiming KCAL you won't be able to unstake SOUL for next 24 hours.";
            return ValidationResult<string>.Ok(message, message);
        }

        public WalletTransactionDraftResult BuildStakeDraft(BigInteger requestedAmount)
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

            var available = balance.Available;
            var amount = requestedAmount;
            if (amount > available && !(accountManager.Settings.devMode && accountManager.Settings.devMode_NoValidation))
            {
                amount = available;
            }

            if (amount <= 0)
            {
                return WalletTransactionDraftResult.Fail("Not enough SOUL to stake.");
            }

            var address = Address.Parse(state.address);
            var decimals = Tokens.GetTokenDecimals(DomainSettings.StakingTokenSymbol, accountManager.CurrentPlatform);

            var sb = new ScriptBuilder();
            sb.AllowGas(address, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
            sb.CallContract("stake", "Stake", address, amount);
            sb.SpendGas(address);
            var script = sb.EndScript();

            var plan = WalletTransactionDraft.ForSingleScript($"Stake {WalletAmountFormatter.Format(amount, decimals, accountManager.Settings.balanceDisplayPrecision)} SOUL", script, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None);
            return WalletTransactionDraftResult.CreateSuccess(plan, amount);
        }

        public WalletTransactionDraftResult BuildUnstakeDraft(BigInteger amount)
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
            sb.CallContract("stake", "Unstake", address, amount);
            sb.SpendGas(address);
            var script = sb.EndScript();

            var plan = WalletTransactionDraft.ForSingleScript($"Unstake {WalletAmountFormatter.Format(amount, decimals, accountManager.Settings.balanceDisplayPrecision)} SOUL", script, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None);
            return WalletTransactionDraftResult.CreateSuccess(plan, amount);
        }

        public WalletTransactionDraftResult BuildClaimKcalDraft(BigInteger claimableAmount)
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
            var decimals = Tokens.GetTokenDecimals("KCAL", accountManager.CurrentPlatform);
            var plan = WalletTransactionDraft.ForSingleScript($"Claim {WalletAmountFormatter.Format(claimableAmount, decimals, accountManager.Settings.balanceDisplayPrecision)} {DomainSettings.FuelTokenSymbol}", script, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None);
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
