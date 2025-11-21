using System;
using UnityEngine;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Convenience helpers for common modal scenarios on top of WalletModalService.
    /// </summary>
    public sealed class WalletModalActions
    {
        private readonly WalletModalService _service;
        private readonly Func<bool> _isVerticalLayout;
        private readonly Action _resetUiHints;

        public WalletModalActions(WalletModalService service, Func<bool> isVerticalLayout, Action resetUiHints)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _isVerticalLayout = isVerticalLayout ?? throw new ArgumentNullException(nameof(isVerticalLayout));
            _resetUiHints = resetUiHints ?? (() => { });
        }

        public void YesNo(string caption, Action<PromptResult> callback, int confirmDelay = 0)
        {
            _service.PromptBox(caption, _service.ModalYesNo, callback, confirmDelay, _isVerticalLayout(), _resetUiHints);
        }

        public string[] ConfirmCancelOptions => _service.ModalConfirmCancel;
        public string[] OkCopyNoAutoCopyOptions => _service.ModalOkCopyNoAutoCopy;
        public string[] HexWifCancelOptions => _service.ModalHexWifCancel;

        public void ConfirmCancel(string caption, Action<PromptResult> callback, int confirmDelay = 0)
        {
            _service.PromptBox(caption, _service.ModalConfirmCancel, callback, confirmDelay, _isVerticalLayout(), _resetUiHints);
        }

        public void SignCancel(string caption, Action<PromptResult> callback, int confirmDelay = 0)
        {
            _service.PromptBox(caption, _service.ModalSignCancel, callback, confirmDelay, _isVerticalLayout(), _resetUiHints);
        }

        public void SendCancel(string caption, Action<PromptResult> callback, int confirmDelay = 0)
        {
            _service.PromptBox(caption, _service.ModalSendCancel, callback, confirmDelay, _isVerticalLayout(), _resetUiHints);
        }

        public void HexOrWif(string title, string caption, Action<PromptResult, string> callback)
        {
            _service.ShowModal(title, caption, ModalState.Message, 0, 0, _service.ModalHexWifCancel, 0, callback, _isVerticalLayout(), _resetUiHints);
        }

        public void CopyableMessage(string title, string caption, Action<PromptResult, string> callback = null, int minInputLength = 0, int maxInputLength = 0, bool closeOnCopy = true, string copyValue = null)
        {
            var valueToCopy = copyValue ?? caption ?? string.Empty;
            _service.ShowModal(title, caption, ModalState.Message, minInputLength, maxInputLength, _service.ModalOkCopyNoAutoCopy, 0, callback, _isVerticalLayout(), _resetUiHints, 0, string.Empty, () =>
            {
                GUIUtility.systemCopyBuffer = valueToCopy;
            }, closeOnCopy);
        }
    }
}
