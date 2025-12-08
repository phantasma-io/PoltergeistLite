using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poltergeist.UiToolkit
{
    /// <summary>
    /// Reusable UI Toolkit factory for common wallet modals (input, chain picker, copy, verification).
    /// </summary>
    public static class WalletUiModalFactory
    {
        public static VisualElement CreateOverlay()
        {
            return WalletUiCommon.CreateModalOverlay();
        }

        public static VisualElement CreateModalWindow(Action onPrimary, Action onSecondary, Action<VisualElement> applyDefaultFont, out Label title, out Label caption, out TextField input, out Button primary, out Button secondary)
        {
            var window = WalletUiCommon.CreateModalPanel(540, 900);
            window.style.maxWidth = new Length(95, LengthUnit.Percent);

            title = new Label("Input")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 20,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 8
                }
            };
            applyDefaultFont?.Invoke(title);
            window.Add(title);

            caption = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 15,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    whiteSpace = WhiteSpace.Normal,
                    marginBottom = 10
                }
            };
            applyDefaultFont?.Invoke(caption);
            window.Add(caption);

            input = new TextField
            {
                multiline = false,
                isPasswordField = false,
                maskChar = '*',
            };
            WalletUiCommon.StyleModalInput(input, false, 40);
            window.Add(input);

            var modalButtons = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexEnd,
                    marginTop = 4
                }
            };
            applyDefaultFont?.Invoke(modalButtons);

            secondary = WalletUiCommon.CreateSecondaryButton("Cancel", onSecondary, 16, 36);
            secondary.style.minWidth = 110;
            primary = WalletUiCommon.CreateOutlineButton("Confirm", onPrimary, 16, 36);
            primary.style.minWidth = 110;
            primary.style.marginLeft = 10;
            modalButtons.Add(secondary);
            modalButtons.Add(primary);
            window.Add(modalButtons);

            return window;
        }

        public static VisualElement CreateChainPickerPanel(Action<string> onSelect, Action onCancel, Action<VisualElement> applyDefaultFont)
        {
            var panel = WalletUiCommon.CreateModalPanel(520, 820);
            panel.style.display = DisplayStyle.None;

            var title = new Label("Select chain")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 20,
                    color = WalletUiTheme.TextPrimary,
                    marginBottom = 10,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            applyDefaultFont?.Invoke(title);
            panel.Add(title);

            var caption = new Label("Choose which chain to use")
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 15,
                    marginBottom = 12,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            applyDefaultFont?.Invoke(caption);
            panel.Add(caption);

            var buttons = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.Center,
                    alignItems = Align.Center
                }
            };

            void AddChainButton(string text)
            {
                var btn = WalletUiCommon.CreateOutlineButton(text, () => onSelect?.Invoke(text), 18, 42);
                btn.style.minWidth = 140;
                if (buttons.childCount > 0)
                {
                    btn.style.marginLeft = 10;
                }
                buttons.Add(btn);
            }

            AddChainButton("Phantasma");
            AddChainButton("Ethereum");
            AddChainButton("Neo Legacy");

            panel.Add(buttons);

            var actions = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.FlexEnd,
                    marginTop = 16
                }
            };
            var cancelButton = WalletUiCommon.CreateSecondaryButton("Cancel", onCancel, 16, 36);
            cancelButton.style.minWidth = 110;
            actions.Add(cancelButton);
            panel.Add(actions);

            return panel;
        }

        public static VisualElement CreateCopyPanel(Action onCopy, Action onClose, Action<VisualElement> applyDefaultFont, out Label title, out Label caption, out TextField valueField)
        {
            var panel = WalletUiCommon.CreateModalPanel(540, 900);
            panel.style.maxWidth = new Length(95, LengthUnit.Percent);
            panel.style.display = DisplayStyle.None;

            title = new Label("Copy value")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 20,
                    color = WalletUiTheme.TextPrimary,
                    marginBottom = 10,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            applyDefaultFont?.Invoke(title);
            panel.Add(title);

            caption = new Label("Copy the value below")
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 15,
                    marginBottom = 10,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            applyDefaultFont?.Invoke(caption);
            panel.Add(caption);

            valueField = new TextField
            {
                multiline = true,
                isPasswordField = false,
                isReadOnly = true
            };
            WalletUiCommon.StyleModalInput(valueField, true, 80);
            panel.Add(valueField);

            var buttons = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.FlexEnd,
                    alignItems = Align.Center,
                    marginTop = 4
                }
            };

            var closeBtn = WalletUiCommon.CreateSecondaryButton("Close", onClose, 16, 36);
            closeBtn.style.minWidth = 110;
            buttons.Add(closeBtn);

            var copyBtn = WalletUiCommon.CreateOutlineButton("Copy", onCopy, 16, 36);
            copyBtn.style.minWidth = 110;
            copyBtn.style.marginLeft = 10;
            buttons.Add(copyBtn);

            panel.Add(buttons);

            return panel;
        }

        public static VisualElement CreateVerificationPanel(Action onOk, Action<VisualElement> applyDefaultFont, out Label messageLabel)
        {
            var panel = WalletUiCommon.CreateModalPanel(520, 820);
            panel.style.display = DisplayStyle.None;

            var title = new Label("Verification result")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 20,
                    color = WalletUiTheme.TextPrimary,
                    marginBottom = 10,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            applyDefaultFont?.Invoke(title);
            panel.Add(title);

            messageLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 15,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 12,
                    whiteSpace = WhiteSpace.Normal
                }
            };
            applyDefaultFont?.Invoke(messageLabel);
            panel.Add(messageLabel);

            var buttons = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.Center
                }
            };
            var okBtn = WalletUiCommon.CreateOutlineButton("OK", onOk, 16, 36);
            okBtn.style.minWidth = 110;
            buttons.Add(okBtn);
            panel.Add(buttons);

            return panel;
        }
    }
}
