using System;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Provides fee availability checks in a UI-agnostic way.
    /// </summary>
    public sealed class WalletFeeService
    {
        private readonly Func<AccountManager> accountProvider;

        public WalletFeeService(Func<AccountManager> accountProvider)
        {
            this.accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public void EnsureKcal(decimal minAmount, Action<PromptResult> callback)
        {
            var accountManager = accountProvider();
            var state = accountManager?.CurrentState;

            if (accountManager == null || state == null)
            {
                callback?.Invoke(PromptResult.Failure);
                return;
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                callback?.Invoke(PromptResult.Success);
                return;
            }

            var feeBalance = state.GetAvailableAmount("KCAL");
            callback?.Invoke(feeBalance >= minAmount ? PromptResult.Success : PromptResult.Failure);
        }
    }
}
