using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Unity.Core.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poltergeist.Wallet;

namespace Poltergeist
{
    public partial class WalletGUI : MonoBehaviour
    {
        #region MODAL PROMPTS
        private string[] ModalNone = new string[] { };
        private string[] ModalOk = new string[] { "Ok" };
        // ModalOkCopy automatically processes "Copy" button press
        private string[] ModalOkCopy = new string[] { "Ok", "Copy to clipboard" };
        // ModalOkCopy_NoAutoCopy requires "Copy" button press callback to be implemented
        private string[] ModalOkCopy_NoAutoCopy = new string[] { "Ok", "Copy to clipboard" };
        private string[] ModalOkView = new string[] { "Ok", "View" };
        private string[] ModalConfirmCancel = new string[] { "Confirm", "Cancel" };
        private string[] ModalSignCancel = new string[] { "Sign", "Cancel" };
        private string[] ModalSendCancel = new string[] { "Send", "Cancel" };
        private string[] ModalYesNo = new string[] { "Yes" , "No" };
        private string[] ModalHexWifCancel = new string[] { "HEX format", "WIF format", "Cancel" };

        private WalletModalContext modalContext => WalletApplicationContext.Instance.Modals;

        // Legacy field aliases pointing to modalContext to reduce churn while refactoring.
        private string[] modalOptions { get => modalContext.Options; set => modalContext.Options = value; }
        private int modalConfirmDelay { get => modalContext.ConfirmDelay; set => modalContext.ConfirmDelay = value; }
        private bool modalRedirected { get => modalContext.Redirected; set => modalContext.Redirected = value; }
        private float modalTime { get => modalContext.Time; set => modalContext.Time = value; }
        private ModalState modalState { get => modalContext.State; set => modalContext.State = value; }
        private Action<PromptResult, string> modalCallback { get => modalContext.Callback; set => modalContext.Callback = value; }
        private string modalInput { get => modalContext.Input; set => modalContext.Input = value; }
        private string modalInputKey { get => modalContext.InputKey; set => modalContext.InputKey = value; }
        private int modalMinInputLength { get => modalContext.MinInputLength; set => modalContext.MinInputLength = value; }
        private int modalMaxInputLength { get => modalContext.MaxInputLength; set => modalContext.MaxInputLength = value; }
        private string modalCaption { get => modalContext.Caption; set => modalContext.Caption = value; }
        private Vector2 modalCaptionScroll { get => modalContext.CaptionScroll; set => modalContext.CaptionScroll = value; }
        private string modalTitle { get => modalContext.Title; set => modalContext.Title = value; }
        private int modalMaxLines { get => modalContext.MaxLines; set => modalContext.MaxLines = value; }
        private string modalHintsLabel { get => modalContext.HintsLabel; set => modalContext.HintsLabel = value; }
        private Dictionary<string, string> modalHints { get => modalContext.Hints; set => modalContext.Hints = value; }
        private PromptResult modalResult { get => modalContext.Result; set => modalContext.Result = value; }
        private int modalLineCount { get => modalContext.LineCount; set => modalContext.LineCount = value; }
        private Texture2D _promptPicture { get => modalContext.PromptPicture; set => modalContext.PromptPicture = value; }

        
        private string GetAdditionalDetails()
        {
            var accountManager = AccountManager.Instance;

            var details = "<size=-5>\n";
            details += $"\nWallet version: {UnityEngine.Application.version} built on: {Poltergeist.Build.Info.Instance.BuildTime} UTC";
            details += $"\nEnvironment: Nexus: {accountManager.Settings.nexusName}";
            details += $"\nRPC: {accountManager.Settings.phantasmaRPCURL}";
            details += $"\nFee price: {accountManager.Settings.feePrice}";
            details += $"\nFee limit: {accountManager.Settings.feeLimit}";
            if(accountManager.Settings.devMode)
            {
                details += $"\nDeveloper mode: {accountManager.Settings.devMode}";
                details += $"\nNo validation mode: {accountManager.Settings.devMode_NoValidation}";
            }
            details += "</size>";

            return details;
        }


        private void ShowModal(string title, string caption, ModalState state, int minInputLength, int maxInputLength, string[] options, int multiLine, Action<PromptResult, string> callback, int confirmDelay = 0, string defaultValue = "")
        {
            if (modalContext.State == ModalState.None)
            {
                modalContext.Time = Time.time;
            }

            modalContext.Result = PromptResult.Waiting;
            modalContext.Input = defaultValue;
            modalContext.InputKey = null;
            modalContext.State = state;
            modalContext.Title = title;

            modalContext.MinInputLength = minInputLength;
            modalContext.MaxInputLength = maxInputLength;

            modalContext.Caption = caption;
            modalContext.CaptionScroll = Vector2.zero;
            modalContext.Callback = callback;
            modalContext.Options = options;
            modalContext.ConfirmDelay = confirmDelay;
            modalContext.HintsLabel = "...";
            modalContext.Hints = null;
            modalContext.MaxLines = multiLine;
            hintComboBox.SelectedItemIndex = -1;
            hintComboBox.ListScroll = Vector2.zero;
            modalContext.LineCount = 0;
            // Counting lines in label. Since labels are wrapped if they are longer than ~65 symbols (~30-40 for vertical layout),
            // we count longer labels too. But labels wrapping based not only on length,
            // but on content also, so we add 2x multiplier to be on a safe side.
            // TODO: Make a better algorithm capable of counting exact number of lines for label depending on label's width and font size.
            Array.ForEach(modalContext.Caption.Split("\n".ToCharArray()), x => modalContext.LineCount += (x.ToString().Length / ((VerticalLayout) ? 30 : 65)) * 2 + 1);
        }

        public void BeginWaitingModal(string caption)
        {
            ShowModal("Please wait...", caption, ModalState.Message, 0, 0, ModalNone, 1, (result, input) =>
            {
            });
        }

        public void EndWaitingModal()
        {
            if (modalContext.Options.Length == 0)
            {
                modalContext.State = ModalState.None;
            }
        }

        public void PromptBox(string caption, string[] options, Action<PromptResult> callback, int confirmDelay = 0)
        {
            ShowModal("Confirmation", caption, ModalState.Message, 0, 0, options, 1, (result, input) =>
            {
                _promptPicture = null;
                callback(result);
            }, confirmDelay);
        }

        public void MessageBox(MessageKind kind, string caption, Action callback = null)
        {
            // try to have focus for Phantasma Link requests
            AppFocus.Instance.StartFocus();

            string title;
            string[] options;
            switch (kind)
            {
                case MessageKind.Success:
                    title = "Success";
                    options = ModalOk;
                    break;

                case MessageKind.Error:
                    title = "Error";
                    options = ModalOkCopy;
                    caption += GetAdditionalDetails();
                    Log.Write($"Error MessageBox: {caption}");
                    break;

                default:
                    title = "Message";
                    options = ModalOk;
                    break;
            }

            ShowModal(title, caption, ModalState.Message, 0, 0, options, 1, (result, input) =>
            {
                callback?.Invoke();
            });
        }
        
        public void ShowUpdateModal(string title, string caption, Action callback = null)
        {
            ShowModal( title,  caption,  ModalState.Message, 0, 0, ModalOkView, 2,  (result, input) =>
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
                });
        }

        private static string AddIndent(string input, string indent)
        {
            var lines = input.Split('\n');
            return string.Join("\n", lines.Select((line, index) => index == 0 ? line : indent + line));
        }

        public void TxResultMessage(Hash hash, TransactionResult txResult, string error, string successCustomMessage = null, string failureCustomMessage = null)
        {
            var printDetails = false;

            if(hash == Hash.Null && txResult == null && error == null)
            {
                // User cancelled tx
                return;
            }

            var accountManager = AccountManager.Instance;

            var success = string.IsNullOrEmpty(error) && hash != Hash.Null;

            // Timeout is not possible for Ethereum tx,
            // since RequestConfirmation() returns success immediatly for Eth.
            var timeout = error == "timeout";

            var message = "";
            if(success && !string.IsNullOrEmpty(successCustomMessage))
            {
                message = successCustomMessage;
            }
            else if(!success && hash == Hash.Null)
            {
                // Hash is unavailable - tx wasn't transferred.
                // Just printing error as is.
            }
            else if(!success && !string.IsNullOrEmpty(failureCustomMessage))
            {
                message = failureCustomMessage;
            }
            else
            {
                if(success)
                {
                    message = "The transaction has successfully completed, but it may take up to 30 secs until the change is reflected in your wallet balance";
                }
                else
                {
                    if (timeout)
                    {
                        message = "Your transaction has been broadcasted but its state cannot be determined.\nPlease use explorer to ensure transaction is confirmed successfully and funds are transferred (button 'View' below).\n";
                    }
                    else
                    {
                        //if(error != "Transaction failed")
                        //{
                        //    message = "Transaction failed";
                        //}
                    }
                }
            }

            if (!string.IsNullOrEmpty(error) && !timeout)
            {
                message += "\nError: " + AddIndent(error, "    ");

                if(txResult != null)
                {
                    message += "\nResult: " + txResult.Result;
                    message += "\nComment: " + txResult.DebugComment;
                }

                printDetails = true;
            }

            if(hash != Hash.Null)
            {
                message += "\nTransaction hash:\n" + hash;
            }

            if (printDetails)
            {
                message += GetAdditionalDetails();
            }

            ShowModal(success ? "Success" : (timeout ? "Attention" : "Failure"),
                message,
                ModalState.Message, 0, 0, ModalOkView, 0, (viewTxChoice, input) =>
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
                });
        }
        #endregion
    }

}
