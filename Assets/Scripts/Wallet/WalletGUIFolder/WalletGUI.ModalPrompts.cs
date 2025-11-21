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
        private string[] ModalNone => modalService.ModalNone;
        private string[] ModalOk => modalService.ModalOk;
        private string[] ModalOkCopy => modalService.ModalOkCopy;
        // ModalOkCopy_NoAutoCopy copy action is provided via modal copy handlers/callback
        private string[] ModalOkCopy_NoAutoCopy => modalService.ModalOkCopyNoAutoCopy;
        private string[] ModalOkView => modalService.ModalOkView;
        private string[] ModalConfirmCancel => modalService.ModalConfirmCancel;
        private string[] ModalSignCancel => modalService.ModalSignCancel;
        private string[] ModalSendCancel => modalService.ModalSendCancel;
        private string[] ModalYesNo => modalService.ModalYesNo;
        private string[] ModalHexWifCancel => modalService.ModalHexWifCancel;

        private WalletModalContext modalContext => WalletApplicationContext.Instance.Modals;


        private void ResetModalUiHints()
        {
            hintComboBox.SelectedItemIndex = -1;
            hintComboBox.ListScroll = Vector2.zero;
        }

        private void ShowModal(string title, string caption, ModalState state, int minInputLength, int maxInputLength, string[] options, int multiLine, Action<PromptResult, string> callback, int confirmDelay = 0, string defaultValue = "", Action onCopy = null, bool closeOnCopy = false)
        {
            modalService.ShowModal(title, caption, state, minInputLength, maxInputLength, options, multiLine, callback, VerticalLayout, ResetModalUiHints, confirmDelay, defaultValue, onCopy, closeOnCopy);
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
            modalService.TxResultMessage(hash, txResult, error, successCustomMessage, failureCustomMessage, VerticalLayout, ResetModalUiHints);
        }
        #endregion
    }

}
