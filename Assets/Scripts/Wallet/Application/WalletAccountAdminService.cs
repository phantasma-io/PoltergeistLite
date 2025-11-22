using System;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.VM;
using Poltergeist;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Builds account administration transaction plans (migration, name registration).
    /// </summary>
    public sealed class WalletAccountAdminService
    {
        private readonly Func<AccountManager> _accountProvider;

        public WalletAccountAdminService(Func<AccountManager> accountProvider)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public WalletTransactionDraftResult BuildMigratePlan(Address targetAddress)
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

            var source = Address.Parse(state.address);

            var sb = new ScriptBuilder();
            sb.AllowGas(source, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
            sb.CallContract("account", "Migrate", source, targetAddress);
            sb.SpendGas(source);
            var script = sb.EndScript();

            var plan = WalletTransactionDraft.ForSingleScript("Migrate account", script, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None);
            return WalletTransactionDraftResult.CreateSuccess(plan);
        }

        public WalletTransactionDraftResult BuildRegisterNamePlan(string name, string addressText)
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

            var source = Address.Parse(addressText);
            var sb = new ScriptBuilder();
            sb.AllowGas(source, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
            sb.CallContract("account", "RegisterName", source, name);
            sb.SpendGas(source);
            var script = sb.EndScript();

            var plan = WalletTransactionDraft.ForSingleScript($"Register address name\nName: {name}\nAddress: {addressText}?", script, DomainSettings.RootChainName, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, ProofOfWork.None);
            return WalletTransactionDraftResult.CreateSuccess(plan);
        }
    }
}
