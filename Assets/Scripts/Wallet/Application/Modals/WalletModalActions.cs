using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using UnityEngine;
using Poltergeist;

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
        private readonly WalletAmountValidator _amountValidator;

        public WalletModalActions(WalletModalService service, Func<bool> isVerticalLayout, Action resetUiHints, WalletAmountValidator amountValidator)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _isVerticalLayout = isVerticalLayout ?? throw new ArgumentNullException(nameof(isVerticalLayout));
            _resetUiHints = resetUiHints ?? (() => { });
            _amountValidator = amountValidator ?? throw new ArgumentNullException(nameof(amountValidator));
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

        public void Message(MessageKind kind, string caption, Action callback = null)
        {
            _service.MessageBox(kind, caption, callback, _isVerticalLayout(), _resetUiHints);
        }

        public void Error(string caption, Action callback = null)
        {
            Message(MessageKind.Error, caption, callback);
        }

        public void Success(string caption, Action callback = null)
        {
            Message(MessageKind.Success, caption, callback);
        }

        public void Info(string caption, Action callback = null)
        {
            Message(MessageKind.Default, caption, callback);
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

        public void RequireAmount(string description, string destination, string symbol, BigInteger min, BigInteger max, uint decimals, Action<BigInteger> callback)
        {
            var formattedMax = WalletAmountFormatter.Format(max, decimals, (int)Math.Min(decimals, 18));
            var caption = $"Enter {symbol} amount:\nMax: {formattedMax} {symbol}";
            if (!string.IsNullOrEmpty(destination))
            {
                caption += $"\nDestination: {destination}";
            }

            var maxInputLength = Math.Max(64, formattedMax.Length + (int)decimals + 2);

            _service.ShowModal(description, caption, ModalState.Input, 1, maxInputLength, ConfirmCancelOptions, 1, (result, input) =>
            {
                if (result == PromptResult.Failure)
                {
                    return;
                }

                var validation = _amountValidator.ParseAndValidate(input, symbol, min, max);
                if (!validation.Success)
                {
                    _service.MessageBox(MessageKind.Error, validation.Error, null, _isVerticalLayout(), _resetUiHints);
                    return;
                }

                callback?.Invoke(validation.Data);
            }, _isVerticalLayout(), _resetUiHints);

            _service.Context.Hints = new Dictionary<string, string>
            {
                { $"Max ({formattedMax} {symbol})", formattedMax.Replace(CultureInfo.InvariantCulture.NumberFormat.NumberGroupSeparator, string.Empty) }
            };
        }

        public void ShowModal(string title, string caption, ModalState state, int minInputLength, int maxInputLength, string[] options, int multiLine, Action<PromptResult, string> callback, int confirmDelay = 0, string defaultValue = "", Action onCopy = null, bool closeOnCopy = false)
        {
            _service.ShowModal(title, caption, state, minInputLength, maxInputLength, options, multiLine, callback, _isVerticalLayout(), _resetUiHints, confirmDelay, defaultValue, onCopy, closeOnCopy);
        }
    }
}
