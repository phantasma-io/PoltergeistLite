using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.RPC.Models;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Cryptography;

namespace Poltergeist.UiToolkit
{
    /// <summary>
    /// Shared UITK dialogs for transaction progress and confirmation polling.
    /// </summary>
    public sealed class WalletUiTransactionDialogs : IDisposable
    {
        private readonly WalletUiModalHost modalHost;
        private readonly Func<AccountManager> accountProvider;
        private readonly Action<string> setStatus;

        private VisualElement sendProgressPanel;
        private Label sendProgressLabel;
        private VisualElement confirmationPanel;
        private Label confirmationLabel;

        private VisualElement modalWindow;
        private VisualElement chainPickerPanel;
        private VisualElement copyPanel;
        private VisualElement verificationPanel;

        private CancellationTokenSource confirmationCts;
        private Hash confirmationHash = Hash.Null;
        private int confirmationCheckCount;
        private bool confirmationRefreshBalance;
        private Action<Hash, TransactionResult, string> confirmationCallback;
        private Action<PromptResult> sendProgressCallback;

        public WalletUiTransactionDialogs(WalletUiModalHost modalHost, Func<AccountManager> accountProvider, Action<string> setStatus)
        {
            this.modalHost = modalHost ?? throw new ArgumentNullException(nameof(modalHost));
            this.accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
            this.setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));

            sendProgressPanel = BuildSendProgressPanel();
            confirmationPanel = BuildConfirmationPanel();
        }

        public void RegisterBlockingPanels(VisualElement modalWindow, VisualElement chainPickerPanel, VisualElement copyPanel, VisualElement verificationPanel)
        {
            this.modalWindow = modalWindow;
            this.chainPickerPanel = chainPickerPanel;
            this.copyPanel = copyPanel;
            this.verificationPanel = verificationPanel;
        }

        public void ShowSendProgress(string description, int txCount, Action<PromptResult> callback)
        {
            sendProgressCallback = callback;
            if (sendProgressLabel != null)
            {
                sendProgressLabel.text = string.IsNullOrWhiteSpace(description)
                    ? "Preparing transaction..."
                    : description;
            }

            HideOtherPanels();
            sendProgressPanel.style.display = DisplayStyle.Flex;
            modalHost.ShowPanel(sendProgressPanel);
        }

        public void StartConfirmation(Hash hash, bool refreshBalanceAfterConfirmation, Action<Hash, TransactionResult, string> callback)
        {
            confirmationHash = hash;
            confirmationRefreshBalance = refreshBalanceAfterConfirmation;
            confirmationCallback = callback;
            confirmationCheckCount = 0;

            confirmationCts?.Cancel();
            confirmationCts = new CancellationTokenSource();

            ShowConfirmationPanel(hash);
            PollConfirmationAsync(confirmationCts.Token);
        }

        public void HideTransactionPanels()
        {
            sendProgressPanel.style.display = DisplayStyle.None;
            confirmationPanel.style.display = DisplayStyle.None;
            modalHost.HidePanel();
            sendProgressCallback = null;
            confirmationCallback = null;
            confirmationCts?.Cancel();
            confirmationCts = null;
            confirmationHash = Hash.Null;
            confirmationCheckCount = 0;
        }

        public void Dispose()
        {
            confirmationCts?.Cancel();
            confirmationCts = null;
        }

        private VisualElement BuildSendProgressPanel()
        {
            var panel = WalletUiCommon.CreateModalPanel(540, 900);
            panel.style.display = DisplayStyle.None;

            var title = new Label("Preparing transaction")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 20,
                    color = WalletUiTheme.TextPrimary,
                    marginBottom = 10,
                    unityTextAlign = TextAnchor.MiddleCenter
                }
            };
            ApplyDefaultFont(title);
            panel.Add(title);

            sendProgressLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 15,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    whiteSpace = WhiteSpace.Normal,
                    marginBottom = 12
                }
            };
            ApplyDefaultFont(sendProgressLabel);
            panel.Add(sendProgressLabel);

            var buttons = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.FlexEnd,
                    marginTop = 8
                }
            };
            var cancelBtn = WalletUiCommon.CreateSecondaryButton("Cancel", OnSendProgressCancel, 16, 36);
            cancelBtn.style.minWidth = 110;
            var confirmBtn = WalletUiCommon.CreateOutlineButton("Send", OnSendProgressConfirm, 16, 36);
            confirmBtn.style.minWidth = 110;
            confirmBtn.style.marginLeft = 10;
            buttons.Add(cancelBtn);
            buttons.Add(confirmBtn);
            panel.Add(buttons);

            return panel;
        }

        private VisualElement BuildConfirmationPanel()
        {
            var panel = WalletUiCommon.CreateModalPanel(540, 900);
            panel.style.display = DisplayStyle.None;

            var title = new Label("Confirming transaction")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 20,
                    color = WalletUiTheme.TextPrimary,
                    marginBottom = 10,
                    unityTextAlign = TextAnchor.MiddleCenter
                }
            };
            ApplyDefaultFont(title);
            panel.Add(title);

            confirmationLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 15,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    whiteSpace = WhiteSpace.Normal,
                    marginBottom = 12
                }
            };
            ApplyDefaultFont(confirmationLabel);
            panel.Add(confirmationLabel);

            return panel;
        }

        private void OnSendProgressCancel()
        {
            CompleteSendProgress(PromptResult.Failure);
        }

        private void OnSendProgressConfirm()
        {
            CompleteSendProgress(PromptResult.Success);
        }

        private void CompleteSendProgress(PromptResult result)
        {
            sendProgressPanel.style.display = DisplayStyle.None;
            modalHost.HidePanel();

            var cb = sendProgressCallback;
            sendProgressCallback = null;
            cb?.Invoke(result);
        }

        private void ShowConfirmationPanel(Hash hash)
        {
            if (confirmationLabel != null)
            {
                confirmationLabel.text = $"Confirming transaction {hash}...";
            }

            HideOtherPanels();
            confirmationPanel.style.display = DisplayStyle.Flex;
            modalHost.ShowPanel(confirmationPanel);
        }

        private async void PollConfirmationAsync(CancellationToken token)
        {
            const int confirmationDelayMs = 3000;

            while (!token.IsCancellationRequested)
            {
                var accountManager = accountProvider();
                if (accountManager == null)
                {
                    CompleteConfirmation(Hash.Null, null, "Account is not ready.");
                    return;
                }

                var checkIndex = ++confirmationCheckCount;
                var tcs = new TaskCompletionSource<(TransactionResult txResult, string error)>();
                accountManager.RequestConfirmation(confirmationHash.ToString(), checkIndex, (txResult, error) =>
                {
                    tcs.TrySetResult((txResult, error));
                });

                (TransactionResult txResult, string error) = await tcs.Task;

                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (string.IsNullOrEmpty(error))
                {
                    CompleteConfirmation(confirmationHash, txResult, null);
                    return;
                }

                if (string.Equals(error, "pending", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateConfirmationLabel(checkIndex);
                }
                else
                {
                    CompleteConfirmation(confirmationHash, txResult, error);
                    return;
                }

                try
                {
                    await Task.Delay(confirmationDelayMs, token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }

        private void UpdateConfirmationLabel(int checkIndex)
        {
            if (confirmationLabel != null)
            {
                confirmationLabel.text = $"Confirming transaction {confirmationHash}... ({checkIndex})";
            }
        }

        private void CompleteConfirmation(Hash hash, TransactionResult txResult, string error)
        {
            confirmationCts?.Cancel();
            confirmationCts = null;
            confirmationCheckCount = 0;

            confirmationPanel.style.display = DisplayStyle.None;
            modalHost.HidePanel();

            if (string.IsNullOrEmpty(error))
            {
                setStatus($"Transaction sent: {hash}");
            }
            else
            {
                setStatus(error);
            }

            void InvokeCallback()
            {
                var cb = confirmationCallback;
                confirmationCallback = null;
                cb?.Invoke(hash, txResult, error);
            }

            if (string.IsNullOrEmpty(error) && confirmationRefreshBalance)
            {
                var accountManager = accountProvider();
                accountManager?.RefreshBalances(true, PlatformKind.None, () =>
                {
                    accountManager.RefreshHistory(true, accountManager.CurrentPlatform);
                    InvokeCallback();
                });
                return;
            }

            if (string.IsNullOrEmpty(error))
            {
                accountProvider()?.RefreshHistory(true, accountProvider()?.CurrentPlatform ?? PlatformKind.None);
            }

            InvokeCallback();
        }

        private void HideOtherPanels()
        {
            if (modalWindow != null)
            {
                modalWindow.style.display = DisplayStyle.None;
            }

            if (chainPickerPanel != null)
            {
                chainPickerPanel.style.display = DisplayStyle.None;
            }

            if (copyPanel != null)
            {
                copyPanel.style.display = DisplayStyle.None;
            }

            if (verificationPanel != null)
            {
                verificationPanel.style.display = DisplayStyle.None;
            }
        }

        private static void ApplyDefaultFont(VisualElement element)
        {
            WalletUiCommon.ApplyDefaultFont(element);
        }
    }
}
