using System;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using Poltergeist.Wallet;

namespace Poltergeist.UiToolkit
{
    /// <summary>
    /// Shared UITK modal host: single overlay + prompt controller, with support for custom panels.
    /// Keeps modal styling/behavior centralized across screens.
    /// </summary>
    public sealed class WalletUiModalHost
    {
        private readonly VisualElement overlay;
        private readonly VisualElement promptContainer;
        private readonly VisualElement panelContainer;
        private readonly WalletUiPromptController promptController;
        private VisualElement activePanel;
        private bool promptActive;

        public WalletUiModalHost(VisualElement parent, Action<VisualElement> applyDefaultFont)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (applyDefaultFont == null) throw new ArgumentNullException(nameof(applyDefaultFont));

            overlay = WalletUiCommon.CreateModalOverlay();

            promptContainer = new VisualElement
            {
                style =
                {
                    flexGrow = 1,
                    justifyContent = Justify.Center,
                    alignItems = Align.Center,
                    width = new Length(100, LengthUnit.Percent),
                    height = new Length(100, LengthUnit.Percent),
                    display = DisplayStyle.None
                }
            };
            panelContainer = new VisualElement
            {
                style =
                {
                    flexGrow = 1,
                    justifyContent = Justify.Center,
                    alignItems = Align.Center,
                    width = new Length(100, LengthUnit.Percent),
                    height = new Length(100, LengthUnit.Percent),
                    display = DisplayStyle.None
                }
            };

            applyDefaultFont(promptContainer);
            applyDefaultFont(panelContainer);
            overlay.Add(promptContainer);
            overlay.Add(panelContainer);
            overlay.RegisterCallback<WheelEvent>(evt => evt.StopPropagation());
            overlay.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            overlay.RegisterCallback<PointerMoveEvent>(evt => evt.StopPropagation());
            parent.Add(overlay);

            promptController = new WalletUiPromptController(overlay, promptContainer, applyDefaultFont, OnPromptShown, OnPromptHidden);
        }

        /// <summary>
        /// Shows a prompt using the shared controller. Optional hooks allow screens to block/unblock their content.
        /// </summary>
        public async Task<(PromptResult result, string input)> ShowPromptAsync(
            string title,
            string caption,
            int minLength,
            int maxLength,
            bool allowEmpty,
            bool hasInput,
            bool isPassword,
            bool multiline,
            string primaryLabel,
            string secondaryLabel,
            bool showSecondary = true,
            string initialValue = "",
            PromptResult successResult = PromptResult.Success,
            PromptResult cancelResult = PromptResult.Failure,
            Action onBeforeShow = null,
            Action onAfterHide = null)
        {
            onBeforeShow?.Invoke();
            promptActive = true;
            try
            {
                return await promptController.ShowAsync(
                    title,
                    caption,
                    minLength,
                    maxLength,
                    allowEmpty,
                    hasInput,
                    isPassword,
                    multiline,
                    primaryLabel,
                    secondaryLabel,
                    showSecondary,
                    initialValue ?? string.Empty,
                    successResult,
                    cancelResult);
            }
            finally
            {
                promptActive = false;
                onAfterHide?.Invoke();
            }
        }

        /// <summary>
        /// Shows a custom panel inside the shared overlay. Clears previous panel.
        /// </summary>
        public void ShowPanel(VisualElement panel, Action onBeforeShow = null)
        {
            if (panel == null)
            {
                return;
            }

            promptController.CancelActivePrompt(PromptResult.Failure);
            onBeforeShow?.Invoke();
            panelContainer.Clear();
            panelContainer.Add(panel);
            panel.style.display = DisplayStyle.Flex;
            panelContainer.style.display = DisplayStyle.Flex;
            promptContainer.style.display = DisplayStyle.None;
            activePanel = panel;
            overlay.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// Hides the currently displayed panel (if any).
        /// </summary>
        public void HidePanel(Action onAfterHide = null)
        {
            if (activePanel != null)
            {
                activePanel.style.display = DisplayStyle.None;
                panelContainer.Clear();
                activePanel = null;
            }

            panelContainer.style.display = DisplayStyle.None;
            if (!promptActive)
            {
                overlay.style.display = DisplayStyle.None;
            }

            onAfterHide?.Invoke();
        }

        public void HideAll()
        {
            promptController.CancelActivePrompt(PromptResult.Failure);
            HidePanel();
        }

        public VisualElement Overlay => overlay;

        public VisualElement PanelContainer => panelContainer;

        /// <summary>
        /// Reattaches overlay as the last child of the given parent to guarantee z-order.
        /// Call this after adding other roots to the same parent.
        /// </summary>
        public void BringToFront(VisualElement parent)
        {
            if (parent == null || overlay == null)
            {
                return;
            }

            overlay.RemoveFromHierarchy();
            parent.Add(overlay);
        }

        private void OnPromptShown()
        {
            panelContainer.style.display = DisplayStyle.None;
            overlay.style.display = DisplayStyle.Flex;
        }

        private void OnPromptHidden()
        {
            if (activePanel == null)
            {
                overlay.style.display = DisplayStyle.None;
            }
        }
    }
}
