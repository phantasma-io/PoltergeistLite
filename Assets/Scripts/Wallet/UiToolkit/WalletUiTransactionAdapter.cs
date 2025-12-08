using System;
using System.Threading.Tasks;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.RPC.Models;
using Poltergeist.Wallet;

namespace Poltergeist.UiToolkit
{
    /// <summary>
    /// Reusable bridge between UITK screens and transaction orchestration.
    /// Keeps password prompt, progress UI and action disabling consistent across views.
    /// </summary>
    internal sealed class WalletUiTransactionAdapter : IWalletTransactionUi
    {
        private readonly WalletAuthService authService;
        private readonly IWalletAuthUi authUi;
        private readonly Action<string> setStatus;
        private readonly Action<bool> setEnabled;
        private readonly Func<string, int, Task<PromptResult>> showSendProgressAsync;
        private readonly Func<Hash, bool, Task<(Hash hash, TransactionResult txResult, string error)>> showConfirmationAsync;
        private bool sending;

        internal WalletUiTransactionAdapter(
            WalletAuthService authService,
            IWalletAuthUi authUi,
            Action<string> setStatus,
            Action<bool> setEnabled,
            Func<string, int, Task<PromptResult>> showSendProgressAsync,
            Func<Hash, bool, Task<(Hash hash, TransactionResult txResult, string error)>> showConfirmationAsync)
        {
            this.authService = authService ?? throw new ArgumentNullException(nameof(authService));
            this.authUi = authUi ?? throw new ArgumentNullException(nameof(authUi));
            this.setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
            this.setEnabled = setEnabled ?? throw new ArgumentNullException(nameof(setEnabled));
            this.showSendProgressAsync = showSendProgressAsync ?? throw new ArgumentNullException(nameof(showSendProgressAsync));
            this.showConfirmationAsync = showConfirmationAsync ?? throw new ArgumentNullException(nameof(showConfirmationAsync));
        }

        public Task<PromptResult> RequestPasswordAsync(string description, PlatformKind platform)
        {
            return authService.RequestPasswordAsync(description, platform, true, false, authUi, ignoreStoredPassword: false);
        }

        public Task<PromptResult> ShowSendProgressAsync(string description, int txCount)
        {
            return showSendProgressAsync(description, txCount);
        }

        public void PushSendingState()
        {
            sending = true;
            setEnabled(false);
        }

        public void PopSendingState()
        {
            sending = false;
            setEnabled(true);
        }

        public async Task<(Hash hash, TransactionResult txResult, string error)> ShowConfirmationAsync(Hash hash, bool refreshBalanceAfterConfirmation)
        {
            try
            {
                return await showConfirmationAsync(hash, refreshBalanceAfterConfirmation);
            }
            finally
            {
                sending = false;
                setEnabled(true);
            }
        }

        public Task ShowErrorAsync(string message)
        {
            setStatus(message ?? "Error");
            if (sending)
            {
                setEnabled(true);
            }

            return Task.CompletedTask;
        }
    }
}
