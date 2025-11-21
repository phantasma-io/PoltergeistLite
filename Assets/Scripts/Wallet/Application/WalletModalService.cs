using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Unity.Core.Logging;
using PhantasmaPhoenix.Protocol;
using Poltergeist;
using Poltergeist.Build;
using PhantasmaPhoenix.Cryptography;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Manages modal lifecycle and populates modal context; rendering is handled separately.
    /// </summary>
    public sealed class WalletModalService
    {
        private readonly WalletModalContext _context;

        private readonly string[] _modalNone = Array.Empty<string>();
        private readonly string[] _modalOk = new[] { "Ok" };
        private readonly string[] _modalOkCopy = new[] { "Ok", "Copy to clipboard" };
        private readonly string[] _modalOkView = new[] { "Ok", "View" };
        private readonly string[] _modalConfirmCancel = new[] { "Confirm", "Cancel" };
        private readonly string[] _modalSignCancel = new[] { "Sign", "Cancel" };
        private readonly string[] _modalSendCancel = new[] { "Send", "Cancel" };
        private readonly string[] _modalYesNo = new[] { "Yes", "No" };
        private readonly string[] _modalOkCopyNoAutoCopy = new[] { "Ok", "Copy to clipboard" };
        private readonly string[] _modalHexWifCancel = new[] { "HEX format", "WIF format", "Cancel" };

        public WalletModalService(WalletModalContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public WalletModalContext Context => _context;

        public string[] ModalNone => _modalNone;
        public string[] ModalOk => _modalOk;
        public string[] ModalOkCopy => _modalOkCopy;
        public string[] ModalOkView => _modalOkView;
        public string[] ModalConfirmCancel => _modalConfirmCancel;
        public string[] ModalSignCancel => _modalSignCancel;
        public string[] ModalSendCancel => _modalSendCancel;
        public string[] ModalYesNo => _modalYesNo;
        public string[] ModalOkCopyNoAutoCopy => _modalOkCopyNoAutoCopy;
        public string[] ModalHexWifCancel => _modalHexWifCancel;

        public void ShowModal(string title, string caption, ModalState state, int minInputLength, int maxInputLength, string[] options, int multiLine, Action<PromptResult, string> callback, bool verticalLayout, Action resetUiHints = null, int confirmDelay = 0, string defaultValue = "")
        {
            if (_context.State == ModalState.None)
            {
                _context.Time = Time.time;
            }

            _context.Result = PromptResult.Waiting;
            _context.Input = defaultValue ?? string.Empty;
            _context.InputKey = null;
            _context.State = state;
            _context.Title = title ?? string.Empty;

            _context.MinInputLength = minInputLength;
            _context.MaxInputLength = maxInputLength;

            _context.Caption = caption ?? string.Empty;
            _context.CaptionScroll = Vector2.zero;
            _context.Callback = callback;
            _context.Options = options ?? Array.Empty<string>();
            _context.ConfirmDelay = confirmDelay;
            _context.HintsLabel = "...";
            _context.Hints = null;
            _context.MaxLines = multiLine;
            _context.LineCount = 0;

            Array.ForEach(_context.Caption.Split("\n".ToCharArray()), x => _context.LineCount += (x.Length / (verticalLayout ? 30 : 65)) * 2 + 1);

            resetUiHints?.Invoke();
        }

        public void BeginWaitingModal(string caption, bool verticalLayout, Action resetUiHints = null)
        {
            ShowModal("Please wait...", caption, ModalState.Message, 0, 0, _modalNone, 1, (result, input) => { }, verticalLayout, resetUiHints);
        }

        public void EndWaitingModal()
        {
            if (_context.Options.Length == 0)
            {
                _context.State = ModalState.None;
            }
        }

        public void PromptBox(string caption, string[] options, Action<PromptResult> callback, int confirmDelay, bool verticalLayout, Action resetUiHints = null)
        {
            ShowModal("Confirmation", caption, ModalState.Message, 0, 0, options, 1, (result, input) =>
            {
                _context.PromptPicture = null;
                callback?.Invoke(result);
            }, verticalLayout, resetUiHints, confirmDelay);
        }

        public void MessageBox(MessageKind kind, string caption, Action callback, bool verticalLayout, Action resetUiHints = null)
        {
            // try to have focus for Phantasma Link requests
            AppFocus.Instance.StartFocus();

            string title;
            string[] options;
            switch (kind)
            {
                case MessageKind.Success:
                    title = "Success";
                    options = _modalOk;
                    break;

                case MessageKind.Error:
                    title = "Error";
                    options = _modalOkCopy;
                    caption += GetAdditionalDetails();
                    Log.Write($"Error MessageBox: {caption}");
                    break;

                default:
                    title = "Message";
                    options = _modalOk;
                    break;
            }

            ShowModal(title, caption, ModalState.Message, 0, 0, options, 1, (result, input) =>
            {
                callback?.Invoke();
            }, verticalLayout, resetUiHints);
        }

        public void ShowUpdateModal(string title, string caption, Action callback, bool verticalLayout, Action resetUiHints = null)
        {
            ShowModal(title, caption, ModalState.Message, 0, 0, _modalOkView, 2, (result, input) =>
            {
                Debug.Log("Update modal result: " + result + ", input: " + input + ", callback: " + callback);
                if (result == PromptResult.Failure)
                {
                    Application.OpenURL(UpdateChecker.UPDATE_URL);
                }

                if (result == PromptResult.Success)
                {
                    callback?.Invoke();
                }
            }, verticalLayout, resetUiHints);
        }

        public void TxResultMessage(Hash hash, TransactionResult txResult, string error, string successCustomMessage, string failureCustomMessage, Func<string> additionalDetails, bool verticalLayout, Action resetUiHints = null)
        {
            var printDetails = false;

            if (hash == Hash.Null && txResult == null && error == null)
            {
                return;
            }

            var accountManager = AccountManager.Instance;

            var success = string.IsNullOrEmpty(error) && hash != Hash.Null;

            var timeout = error == "timeout";

            var message = "";
            if (success && !string.IsNullOrEmpty(successCustomMessage))
            {
                message = successCustomMessage;
            }
            else if (!success && hash == Hash.Null)
            {
                // Hash is unavailable - tx wasn't transferred.
            }
            else if (!success && !string.IsNullOrEmpty(failureCustomMessage))
            {
                message = failureCustomMessage;
            }
            else
            {
                if (success)
                {
                    message = "The transaction has successfully completed, but it may take up to 30 secs until the change is reflected in your wallet balance";
                }
                else
                {
                    if (timeout)
                    {
                        message = "Your transaction has been broadcasted but its state cannot be determined.\nPlease use explorer to ensure transaction is confirmed successfully and funds are transferred (button 'View' below).\n";
                    }
                }
            }

            if (!string.IsNullOrEmpty(error) && !timeout)
            {
                message += "\nError: " + AddIndent(error, "    ");

                if (txResult != null)
                {
                    message += "\nResult: " + txResult.Result;
                    message += "\nComment: " + txResult.DebugComment;
                }

                printDetails = true;
            }

            if (hash != Hash.Null)
            {
                message += "\nTransaction hash:\n" + hash;
            }

            if (printDetails)
            {
                message += additionalDetails?.Invoke();
            }

            ShowModal(success ? "Success" : (timeout ? "Attention" : "Failure"),
                message,
                ModalState.Message, 0, 0, _modalOkView, 0, (viewTxChoice, input) =>
                {
                    if (viewTxChoice == PromptResult.Failure)
                    {
                        switch (accountManager.CurrentPlatform)
                        {
                            case PlatformKind.Phantasma:
                                Application.OpenURL(accountManager.GetPhantasmaTransactionURL(hash.ToString()));
                                break;
                        }
                    }
                }, verticalLayout, resetUiHints);
        }

        private static string AddIndent(string input, string indent)
        {
            var lines = input.Split('\n');
            return string.Join("\n", lines.Select((line, index) => index == 0 ? line : indent + line));
        }

        private string GetAdditionalDetails()
        {
            var accountManager = AccountManager.Instance;

            var details = "<size=-5>\n";
            details += $"\nWallet version: {UnityEngine.Application.version} built on: {Build.Info.Instance.BuildTime} UTC";
            details += $"\nEnvironment: Nexus: {accountManager.Settings.nexusName}";
            details += $"\nRPC: {accountManager.Settings.phantasmaRPCURL}";
            details += $"\nFee price: {accountManager.Settings.feePrice}";
            details += $"\nFee limit: {accountManager.Settings.feeLimit}";
            if (accountManager.Settings.devMode)
            {
                details += $"\nDeveloper mode: {accountManager.Settings.devMode}";
                details += $"\nNo validation mode: {accountManager.Settings.devMode_NoValidation}";
            }
            details += "</size>";

            return details;
        }
    }
}
