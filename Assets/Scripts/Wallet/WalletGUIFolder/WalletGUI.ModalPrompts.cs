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


        private void ResetModalUiHints()
        {
            hintComboBox.SelectedItemIndex = -1;
            hintComboBox.ListScroll = Vector2.zero;
        }

        private void ShowModal(string title, string caption, ModalState state, int minInputLength, int maxInputLength, string[] options, int multiLine, Action<PromptResult, string> callback, int confirmDelay = 0, string defaultValue = "")
        {
            modalService.ShowModal(title, caption, state, minInputLength, maxInputLength, options, multiLine, callback, VerticalLayout, ResetModalUiHints, confirmDelay, defaultValue);
        }

        public void BeginWaitingModal(string caption)
        {
            modalService.BeginWaitingModal(caption, VerticalLayout, ResetModalUiHints);
        }

        public void EndWaitingModal()
        {
            modalService.EndWaitingModal();
        }

        public void PromptBox(string caption, string[] options, Action<PromptResult> callback, int confirmDelay = 0)
        {
            modalService.PromptBox(caption, options, callback, confirmDelay, VerticalLayout, ResetModalUiHints);
        }

        public void MessageBox(MessageKind kind, string caption, Action callback = null)
        {
            modalService.MessageBox(kind, caption, callback, VerticalLayout, ResetModalUiHints);
        }
        
        public void ShowUpdateModal(string title, string caption, Action callback = null)
        {
            modalService.ShowUpdateModal(title, caption, callback, VerticalLayout, ResetModalUiHints);
        }

        private static string AddIndent(string input, string indent)
        {
            var lines = input.Split('\n');
            return string.Join("\n", lines.Select((line, index) => index == 0 ? line : indent + line));
        }

        public void TxResultMessage(Hash hash, TransactionResult txResult, string error, string successCustomMessage = null, string failureCustomMessage = null)
        {
            modalService.TxResultMessage(hash, txResult, error, successCustomMessage, failureCustomMessage, GetAdditionalDetails, VerticalLayout, ResetModalUiHints);
        }
        #endregion
    }

}
