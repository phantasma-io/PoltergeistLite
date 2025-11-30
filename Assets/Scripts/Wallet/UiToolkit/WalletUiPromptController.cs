using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Poltergeist.Wallet;

namespace Poltergeist.UiToolkit
{
    /// <summary>
    /// Centralized async prompt controller for simple modal dialogs (title/caption/input + OK/Cancel).
    /// Keeps validation and key handling in one place to avoid callback soup across views.
    /// </summary>
    public sealed class WalletUiPromptController
    {
        private readonly VisualElement overlay;
        private readonly VisualElement host;
        private readonly Action<VisualElement> applyDefaultFont;
        private readonly Action onShow;
        private readonly Action onHide;
        private readonly VisualElement panel;
        private readonly Label titleLabel;
        private readonly Label captionLabel;
        private readonly Label validationLabel;
        private readonly TextField inputField;
        private readonly Button primaryButton;
        private readonly Button secondaryButton;

        private TaskCompletionSource<(PromptResult result, string input)> promptTcs;
        private int minLength;
        private int maxLength;
        private bool allowEmpty;
        private bool hasInput;
        private bool isPassword;
        private bool multiline;
        private PromptResult successResult = PromptResult.Success;
        private PromptResult cancelResult = PromptResult.Failure;
        private EventCallback<KeyUpEvent> keyHandler;

        public WalletUiPromptController(VisualElement overlay, VisualElement host, Action<VisualElement> applyDefaultFont, Action onShow = null, Action onHide = null)
        {
            this.overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.applyDefaultFont = applyDefaultFont ?? throw new ArgumentNullException(nameof(applyDefaultFont));
            this.onShow = onShow;
            this.onHide = onHide;

            panel = WalletUiModalFactory.CreateModalWindow(OnPrimaryClicked, OnSecondaryClicked, applyDefaultFont, out titleLabel, out captionLabel, out inputField, out primaryButton, out secondaryButton);
            panel.focusable = true;
            panel.tabIndex = 0;
            panel.pickingMode = PickingMode.Position;
            validationLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 13,
                    marginBottom = 6,
                    display = DisplayStyle.None,
                    whiteSpace = WhiteSpace.Normal,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            this.applyDefaultFont(validationLabel);
            panel.Insert(panel.IndexOf(inputField), validationLabel);
            panel.style.display = DisplayStyle.None;
            host.style.flexGrow = 1;
            host.style.justifyContent = Justify.Center;
            host.style.alignItems = Align.Center;
            host.Add(panel);
        }

        public Task<(PromptResult result, string input)> ShowAsync(string title, string caption, int minLength, int maxLength, bool allowEmpty = false, bool hasInput = true, bool isPassword = true, bool multiline = false, string primaryLabel = null, string secondaryLabel = null, bool showSecondary = true, string initialValue = "", PromptResult successResult = PromptResult.Success, PromptResult cancelResult = PromptResult.Failure)
        {
            CancelActivePrompt(cancelResult);

            promptTcs = new TaskCompletionSource<(PromptResult result, string input)>(TaskCreationOptions.RunContinuationsAsynchronously);
            this.minLength = Math.Max(0, minLength);
            this.maxLength = maxLength;
            this.allowEmpty = allowEmpty || minLength <= 0;
            this.hasInput = hasInput;
            this.isPassword = isPassword;
            this.multiline = multiline;
            this.successResult = successResult;
            this.cancelResult = cancelResult;

            validationLabel.text = string.Empty;
            validationLabel.style.display = DisplayStyle.None;

            ConfigureButtons(primaryLabel, secondaryLabel, showSecondary);
            ConfigureInput(initialValue);

            titleLabel.text = title ?? string.Empty;
            captionLabel.text = caption ?? string.Empty;

            onShow?.Invoke();
            host.style.display = DisplayStyle.Flex;
            panel.style.display = DisplayStyle.Flex;
            overlay.style.display = DisplayStyle.Flex;
            RegisterKeyHandler();

            if (hasInput)
            {
                inputField.schedule.Execute(() => inputField.Focus()).StartingIn(50);
            }
            else
            {
                panel.schedule.Execute(() => panel.Focus()).StartingIn(20);
            }

            return promptTcs.Task;
        }

        public void CancelActivePrompt(PromptResult result = PromptResult.Failure)
        {
            if (promptTcs == null)
            {
                HidePanel();
                return;
            }

            CompletePrompt(result, string.Empty);
        }

        private void ConfigureButtons(string primaryLabel, string secondaryLabel, bool showSecondary)
        {
            primaryButton.text = string.IsNullOrWhiteSpace(primaryLabel) ? "Confirm" : primaryLabel;
            secondaryButton.text = string.IsNullOrWhiteSpace(secondaryLabel) ? "Cancel" : secondaryLabel;
            secondaryButton.style.display = showSecondary ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ConfigureInput(string initialValue)
        {
            inputField.isPasswordField = isPassword;
            inputField.maskChar = isPassword ? '*' : '\0';
            inputField.multiline = multiline;
            inputField.maxLength = maxLength > 0 ? maxLength : int.MaxValue;
            inputField.value = initialValue ?? string.Empty;
            inputField.visible = hasInput;
            inputField.SetEnabled(hasInput);
            WalletUiCommon.StyleModalInput(inputField, multiline, multiline ? 80 : 40);
        }

        private void OnPrimaryClicked()
        {
            if (promptTcs == null)
            {
                return;
            }

            var input = hasInput ? inputField.text ?? string.Empty : string.Empty;
            if (!allowEmpty)
            {
                if (maxLength > 0 && input.Length > maxLength)
                {
                    ShowValidation($"Input must be <= {maxLength} characters.");
                    inputField.schedule.Execute(() => inputField.Focus()).StartingIn(30);
                    return;
                }

                if (input.Length < minLength)
                {
                    ShowValidation($"Input must be >= {minLength} characters.");
                    inputField.schedule.Execute(() => inputField.Focus()).StartingIn(30);
                    return;
                }
            }

            CompletePrompt(successResult, input);
        }

        private void OnSecondaryClicked()
        {
            if (promptTcs == null)
            {
                return;
            }

            CompletePrompt(cancelResult, string.Empty);
        }

        private void CompletePrompt(PromptResult result, string input)
        {
            var tcs = promptTcs;
            promptTcs = null;
            HidePanel();
            tcs?.TrySetResult((result, input));
        }

        private void HidePanel()
        {
            UnregisterKeyHandler();
            panel.style.display = DisplayStyle.None;
            host.style.display = DisplayStyle.None;
            overlay.style.display = DisplayStyle.None;
            inputField.value = string.Empty;
            validationLabel.text = string.Empty;
            validationLabel.style.display = DisplayStyle.None;
            onHide?.Invoke();
        }

        private void ShowValidation(string message)
        {
            validationLabel.text = message ?? string.Empty;
            validationLabel.style.display = DisplayStyle.Flex;
        }

        private void RegisterKeyHandler()
        {
            UnregisterKeyHandler();
            keyHandler = evt =>
            {
                if (promptTcs == null)
                {
                    return;
                }

                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    OnPrimaryClicked();
                    evt.StopImmediatePropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    OnSecondaryClicked();
                    evt.StopImmediatePropagation();
                }
            };

            panel.RegisterCallback(keyHandler, TrickleDown.TrickleDown);
            overlay.RegisterCallback(keyHandler, TrickleDown.TrickleDown);
        }

        private void UnregisterKeyHandler()
        {
            if (keyHandler == null)
            {
                return;
            }

            panel.UnregisterCallback(keyHandler, TrickleDown.TrickleDown);
            overlay.UnregisterCallback(keyHandler, TrickleDown.TrickleDown);
            keyHandler = null;
        }
    }
}
