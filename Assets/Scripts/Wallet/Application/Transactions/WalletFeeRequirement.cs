using System;
using PhantasmaPhoenix.Protocol;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Ensures fee availability in a UI-agnostic way.
    /// </summary>
    public sealed class WalletFeeRequirement
    {
        private readonly Func<AccountManager> _accountProvider;

        public WalletFeeRequirement(Func<AccountManager> accountProvider)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public void EnsureKcal(decimal minAmount, Action<PromptResult, string> callback)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                callback?.Invoke(PromptResult.Failure, "Account manager is not available yet.");
                return;
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                callback?.Invoke(PromptResult.Success, null);
                return;
            }

            var state = accountManager.CurrentState;
            if (state == null)
            {
                callback?.Invoke(PromptResult.Failure, "Account state is unavailable.");
                return;
            }

            var feeBalance = state.GetAvailableAmount("KCAL");
            callback?.Invoke(feeBalance >= minAmount ? PromptResult.Success : PromptResult.Failure, feeBalance >= minAmount ? null : "KCAL is required to make transactions!");
        }
    }
}
