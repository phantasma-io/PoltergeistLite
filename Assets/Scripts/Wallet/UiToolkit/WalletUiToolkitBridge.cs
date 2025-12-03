using System;
using System.Text;
using System.Threading.Tasks;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist.Wallet;

namespace Poltergeist.UiToolkit
{
    /// <summary>
    /// UITK implementation of IWalletUiBridge so WalletLink can request user interaction without legacy UI.
    /// </summary>
    internal sealed class WalletUiToolkitBridge : IWalletUiBridge, IDisposable
    {
        private readonly WalletUiModalHost modalHost;
        private readonly WalletTransactionOrchestrator transactionOrchestrator;
        private readonly WalletUiTransactionDialogs transactionDialogs;
        private readonly Func<AccountManager> accountProvider;

        internal WalletUiToolkitBridge(WalletApplicationContext context, WalletUiModalHost modalHost)
        {
            this.modalHost = modalHost ?? throw new ArgumentNullException(nameof(modalHost));
            var ctx = context ?? throw new ArgumentNullException(nameof(context));

            accountProvider = () => AccountManager.Instance;
            var authUi = new ModalAuthUi(this.modalHost);
            var authService = ctx.AuthService ?? throw new ArgumentNullException(nameof(ctx.AuthService));
            transactionDialogs = new WalletUiTransactionDialogs(this.modalHost, accountProvider, SetStatus);
            transactionOrchestrator = new WalletTransactionOrchestrator(accountProvider, new LinkTransactionUi(authService, authUi, transactionDialogs, this.modalHost, SetStatus));
        }

        public void Dispose()
        {
            transactionDialogs?.Dispose();
        }

        public void PostToMainThread(Action action)
        {
            UnityTaskRunner.PostToMainThread(action);
        }

        public async Task<bool> PromptAsync(string text)
        {
            AppFocus.Instance?.StartFocus();
            var result = await WalletUiModalHelper.ShowConfirmAsync(modalHost, "Phantasma Link", text ?? string.Empty, "Yes", "No");
            return result == PromptResult.Success;
        }

        public Task<(Hash hash, TransactionResult txResult, string error)> SendTransactionDraftAsync(WalletTransactionDraft draft, bool refreshBalanceAfterConfirmation = true)
        {
            return transactionOrchestrator.SendTransactionDraftAsync(draft, refreshBalanceAfterConfirmation);
        }

        public void TxResultMessage(Hash hash, TransactionResult txResult, string error, string successCustomMessage = null, string failureCustomMessage = null)
        {
            ShowTxResultAsync(hash, txResult, error, successCustomMessage, failureCustomMessage).Forget(ex => Log.WriteWarning(ex.ToString()));
        }

        public Task<(string[] result, string error)> InvokeScriptAsync(string chain, byte[] script)
        {
            var accountManager = accountProvider();
            if (accountManager == null)
            {
                return Task.FromResult(((string[])null, "Account manager is not ready."));
            }

            if (script == null || script.Length == 0)
            {
                return Task.FromResult(((string[])null, "Error invoking script. Script is null."));
            }

            var tcs = new TaskCompletionSource<(string[] result, string error)>(TaskCreationOptions.RunContinuationsAsynchronously);
            accountManager.InvokeScript(chain, script, (results, invokeError) =>
            {
                if (string.IsNullOrEmpty(invokeError))
                {
                    tcs.TrySetResult((results, null));
                    return;
                }

                var scriptText = Encoding.UTF8.GetString(script);
                tcs.TrySetResult((null, $"Error invoking script.\n{invokeError}\nScript: {scriptText}"));
            });

            return tcs.Task;
        }

        public Task<(bool success, string error)> WriteArchiveAsync(Hash hash, int blockIndex, byte[] data)
        {
            var accountManager = accountProvider();
            if (accountManager == null)
            {
                return Task.FromResult((false, "Account manager is not ready."));
            }

            if (data == null || data.Length == 0)
            {
                return Task.FromResult((false, "Error writing archive. No data available."));
            }

            var tcs = new TaskCompletionSource<(bool success, string error)>(TaskCreationOptions.RunContinuationsAsynchronously);
            accountManager.WriteArchive(hash, blockIndex, data, (result, writeError) => tcs.TrySetResult((result, writeError)));
            return tcs.Task;
        }

        private Task ShowTxResultAsync(Hash hash, TransactionResult txResult, string error, string successCustomMessage, string failureCustomMessage)
        {
            return WalletUiTransactionResultHelper.ShowAsync(modalHost, accountProvider, hash, txResult, error, successCustomMessage, failureCustomMessage);
        }

        private void SetStatus(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            // Transaction dialogs expect a status sink; for now we log to keep diagnostics visible until a shared status surface exists in UITK.
            Log.Write($"[UITK][Link] {message}");
        }

        private sealed class ModalAuthUi : IWalletAuthUi
        {
            private readonly WalletUiModalHost modalHost;

            internal ModalAuthUi(WalletUiModalHost modalHost)
            {
                this.modalHost = modalHost ?? throw new ArgumentNullException(nameof(modalHost));
            }

            public Task<(PromptResult result, string password)> PromptPasswordAsync(string title, string caption, int minLength, int maxLength)
            {
                return WalletUiModalHelper.ShowPromptAsync(
                    modalHost,
                    string.IsNullOrWhiteSpace(title) ? "Account Authorization" : title,
                    caption ?? string.Empty,
                    minLength,
                    maxLength,
                    allowEmpty: false,
                    hasInput: true,
                    isPassword: true,
                    multiline: false,
                    primaryLabel: "Confirm",
                    secondaryLabel: "Cancel",
                    successResult: PromptResult.Success,
                    cancelResult: PromptResult.Failure);
            }

            public Task ShowErrorAsync(string message)
            {
                return WalletUiModalHelper.ShowErrorAsync(modalHost, "Error", message ?? "Authorization failed.");
            }
        }

        // Dedicated transaction UI for WalletLink flows that reuses shared UITK dialogs to stay consistent with on-screen flows.
        private sealed class LinkTransactionUi : IWalletTransactionUi
        {
            private readonly WalletAuthService authService;
            private readonly IWalletAuthUi authUi;
            private readonly WalletUiTransactionDialogs transactionDialogs;
            private readonly WalletUiModalHost modalHost;
            private readonly Action<string> setStatus;

            internal LinkTransactionUi(WalletAuthService authService, IWalletAuthUi authUi, WalletUiTransactionDialogs transactionDialogs, WalletUiModalHost modalHost, Action<string> setStatus)
            {
                this.authService = authService ?? throw new ArgumentNullException(nameof(authService));
                this.authUi = authUi ?? throw new ArgumentNullException(nameof(authUi));
                this.transactionDialogs = transactionDialogs ?? throw new ArgumentNullException(nameof(transactionDialogs));
                this.modalHost = modalHost ?? throw new ArgumentNullException(nameof(modalHost));
                this.setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
            }

            public Task<PromptResult> RequestPasswordAsync(string description, PlatformKind platform)
            {
                return authService.RequestPasswordAsync(description, platform, true, false, authUi, ignoreStoredPassword: false);
            }

            public Task<PromptResult> ShowSendProgressAsync(string description, int txCount)
            {
                return transactionDialogs.ShowSendProgressAsync(description, txCount);
            }

            public void PushSendingState()
            {
                setStatus("Sending transaction...");
            }

            public void PopSendingState()
            {
                setStatus(string.Empty);
            }

            public Task<(Hash hash, TransactionResult txResult, string error)> ShowConfirmationAsync(Hash hash, bool refreshBalanceAfterConfirmation)
            {
                return transactionDialogs.StartConfirmationAsync(hash, refreshBalanceAfterConfirmation);
            }

            public Task ShowErrorAsync(string message)
            {
                setStatus(string.IsNullOrEmpty(message) ? "Error" : message);
                return WalletUiModalHelper.ShowErrorAsync(modalHost, "Error", message ?? "Unknown error");
            }
        }
    }
}
