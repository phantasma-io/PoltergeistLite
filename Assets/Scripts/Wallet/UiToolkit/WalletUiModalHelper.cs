using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Cryptography;
using Poltergeist;

namespace Poltergeist.UiToolkit
{
    /// <summary>
    /// Shared helpers for common modal patterns (info, confirm, errors) built on top of WalletUiModalHost.
    /// </summary>
    public static class WalletUiModalHelper
    {
        /// <summary>
        /// Shows a configurable prompt with sane defaults for allowEmpty/input handling to avoid scattered wrappers.
        /// </summary>
        public static Task<(PromptResult result, string input)> ShowPromptAsync(
            WalletUiModalHost host,
            string title,
            string message,
            int minLength,
            int maxLength,
            bool allowEmpty = false,
            bool hasInput = true,
            bool isPassword = false,
            bool multiline = false,
            string primaryLabel = "Confirm",
            string secondaryLabel = "Cancel",
            bool showSecondary = true,
            string initialValue = "",
            PromptResult successResult = PromptResult.Success,
            PromptResult cancelResult = PromptResult.Failure,
            Action onBeforeShow = null,
            Action onAfterHide = null)
        {
            if (host == null)
            {
                return Task.FromResult((PromptResult.Failure, string.Empty));
            }

            var effectiveAllowEmpty = allowEmpty || !hasInput || minLength <= 0;

            return host.ShowPromptAsync(
                string.IsNullOrWhiteSpace(title) ? string.Empty : title,
                message ?? string.Empty,
                minLength,
                maxLength,
                effectiveAllowEmpty,
                hasInput,
                isPassword,
                multiline,
                string.IsNullOrWhiteSpace(primaryLabel) ? "Confirm" : primaryLabel,
                string.IsNullOrWhiteSpace(secondaryLabel) ? "Cancel" : secondaryLabel,
                showSecondary,
                initialValue ?? string.Empty,
                successResult,
                cancelResult,
                onBeforeShow,
                onAfterHide);
        }

        public static Task<(PromptResult result, string input)> ShowInfoAsync(WalletUiModalHost host, string title, string message, Action onBeforeShow = null, Action onAfterHide = null)
        {
            if (host == null)
            {
                return Task.FromResult((PromptResult.Failure, string.Empty));
            }

            return host.ShowPromptAsync(
                string.IsNullOrWhiteSpace(title) ? "Info" : title,
                message ?? string.Empty,
                0,
                0,
                allowEmpty: true,
                hasInput: false,
                isPassword: false,
                multiline: false,
                primaryLabel: "Close",
                secondaryLabel: "Cancel",
                showSecondary: false,
                onBeforeShow: onBeforeShow,
                onAfterHide: onAfterHide);
        }

        public static async Task<PromptResult> ShowConfirmAsync(WalletUiModalHost host, string title, string message, string confirmLabel = "Confirm", string cancelLabel = "Cancel", Action onBeforeShow = null, Action onAfterHide = null)
        {
            if (host == null)
            {
                return PromptResult.Failure;
            }

            var (result, _) = await host.ShowPromptAsync(
                string.IsNullOrWhiteSpace(title) ? "Confirm" : title,
                message ?? string.Empty,
                0,
                0,
                allowEmpty: true,
                hasInput: false,
                isPassword: false,
                multiline: false,
                primaryLabel: string.IsNullOrWhiteSpace(confirmLabel) ? "Confirm" : confirmLabel,
                secondaryLabel: string.IsNullOrWhiteSpace(cancelLabel) ? "Cancel" : cancelLabel,
                showSecondary: true,
                onBeforeShow: onBeforeShow,
                onAfterHide: onAfterHide);

            return result;
        }

        /// <summary>
        /// Generic list dialog with a scrollable set of rows (title + subtitle) and a single Close action.
        /// Intended for large lists (e.g., skipped items) to avoid stuffing long text into labels.
        /// </summary>
        public static Task<PromptResult> ShowListDialogAsync(
            WalletUiModalHost host,
            string title,
            string caption,
            IReadOnlyList<(string title, string subtitle)> items,
            string closeLabel = "Close",
            Action onBeforeShow = null,
            Action onAfterHide = null)
        {
            if (host == null)
            {
                return Task.FromResult(PromptResult.Failure);
            }

            var tcs = new TaskCompletionSource<PromptResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Complete(PromptResult result)
            {
                host.HidePanel(onAfterHide);
                tcs.TrySetResult(result);
            }

            var panel = WalletUiCommon.CreateModalPanel(720, 960);
            panel.style.maxWidth = new Length(95, LengthUnit.Percent);
            panel.style.maxHeight = new Length(90, LengthUnit.Percent);
            panel.style.minHeight = 0;
            panel.style.flexShrink = 1;
            panel.style.flexGrow = 0;
            panel.style.overflow = Overflow.Hidden;

            var titleLabel = new Label(string.IsNullOrWhiteSpace(title) ? "Items" : title)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 20,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 6
                }
            };
            WalletUiCommon.ApplyDefaultFont(titleLabel);
            panel.Add(titleLabel);

            if (!string.IsNullOrWhiteSpace(caption))
            {
                var captionLabel = new Label(caption)
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        fontSize = 14,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        marginBottom = 10,
                        whiteSpace = WhiteSpace.Normal
                    }
                };
                WalletUiCommon.ApplyDefaultFont(captionLabel);
                panel.Add(captionLabel);
            }

            var countLabel = new Label(items == null ? "0 item(s)" : $"{items.Count} item(s)")
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 13,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 4
                }
            };
            WalletUiCommon.ApplyDefaultFont(countLabel);
            panel.Add(countLabel);

            var listWrapper = WalletUiCommon.BuildScrollContainer(
                out var list,
                onScrollChanged: null,
                shouldBlockWheel: () => false,
                paddingLeft: 12f,
                paddingRight: 12f,
                paddingTop: 6f,
                paddingBottom: 12f,
                marginTop: 6f,
                marginBottom: 10f,
                maxWidth: 0f,
                alignSelf: Align.Stretch);
            list.style.flexGrow = 1;
            list.style.flexShrink = 1;
            list.style.flexBasis = 0;
            list.style.minHeight = 0;
            list.style.maxHeight = 540;

            var hasItems = items != null && items.Count > 0;
            if (hasItems)
            {
                var estimatedHeight = Mathf.Clamp(items.Count * 56f, 180f, 540f);
                listWrapper.style.height = estimatedHeight;
                listWrapper.style.minHeight = 180;
                listWrapper.style.maxHeight = 540;

                var rowIndex = 1;
                foreach (var entry in items)
                {
                    list.Add(CreateListRow(rowIndex, entry.title, entry.subtitle));
                    rowIndex++;
                }
            }
            else
            {
                listWrapper.style.minHeight = 120;
                var emptyLabel = new Label("No items to display.")
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        fontSize = 14,
                        unityTextAlign = TextAnchor.MiddleCenter,
                        marginTop = 6,
                        marginBottom = 6
                    }
                };
                WalletUiCommon.ApplyDefaultFont(emptyLabel);
                list.Add(emptyLabel);
            }

            panel.Add(listWrapper);

            var buttons = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexEnd,
                    marginTop = 8,
                    flexShrink = 0
                }
            };
            WalletUiCommon.ApplyDefaultFont(buttons);

            var closeBtn = WalletUiCommon.CreateOutlineButton(string.IsNullOrWhiteSpace(closeLabel) ? "Close" : closeLabel, () => Complete(PromptResult.Success), 16, 36);
            closeBtn.style.minWidth = 120;
            buttons.Add(closeBtn);
            panel.Add(buttons);

            host.ShowPanel(panel, onBeforeShow);
            return tcs.Task;
        }

        /// <summary>
        /// Address input dialog that combines manual entry, clipboard paste guard and quick selection from existing wallets.
        /// Keeps the UX consistent across screens that need destination picking without cloning ad-hoc prompts.
        /// </summary>
        public static Task<(PromptResult result, string address)> ShowAddressInputDialogAsync(
            WalletUiModalHost host,
            string title,
            string caption,
            IReadOnlyList<Account> accounts,
            string confirmLabel = "Confirm",
            string cancelLabel = "Cancel",
            string initialValue = "",
            Action onBeforeShow = null,
            Action onAfterHide = null)
        {
            if (host == null)
            {
                return Task.FromResult((PromptResult.Failure, string.Empty));
            }

            var tcs = new TaskCompletionSource<(PromptResult result, string address)>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Only keep unique, valid Phantasma addresses to avoid noisy or unusable entries in the picker.
            var validAccounts = new List<Account>();
            var seenAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (accounts != null)
            {
                foreach (var acc in accounts)
                {
                    var address = acc.phaAddress?.Trim();
                    if (string.IsNullOrWhiteSpace(address) || !Address.IsValidAddress(address) || !seenAddresses.Add(address))
                    {
                        continue;
                    }

                    validAccounts.Add(acc);
                }
            }

            var panel = WalletUiCommon.CreateModalPanel(720, 960);
            panel.style.maxWidth = new Length(95, LengthUnit.Percent);
            panel.style.maxHeight = new Length(90, LengthUnit.Percent);
            panel.style.flexShrink = 1;
            panel.style.flexGrow = 0;
            panel.style.overflow = Overflow.Hidden;

            var titleLabel = new Label(string.IsNullOrWhiteSpace(title) ? "Destination address" : title)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 20,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 6
                }
            };
            WalletUiCommon.ApplyDefaultFont(titleLabel);
            panel.Add(titleLabel);

            if (!string.IsNullOrWhiteSpace(caption))
            {
                var captionLabel = new Label(caption)
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        fontSize = 14,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        marginBottom = 10,
                        whiteSpace = WhiteSpace.Normal
                    }
                };
                WalletUiCommon.ApplyDefaultFont(captionLabel);
                panel.Add(captionLabel);
            }

            var inputRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 6
                }
            };
            WalletUiCommon.ApplyDefaultFont(inputRow);

            var destinationField = new TextField
            {
                value = initialValue ?? string.Empty,
                multiline = false,
                isPasswordField = false,
                maxLength = 64
            };
            WalletUiCommon.StyleModalInput(destinationField, false, 44);
            destinationField.style.marginBottom = 0;
            destinationField.style.flexGrow = 1;
            destinationField.style.flexShrink = 1;
            destinationField.style.flexBasis = 0;
            destinationField.style.minWidth = 0;
            inputRow.Add(destinationField);

            var pasteButton = WalletUiCommon.CreateSecondaryButton("Paste", null, 14, 36);
            pasteButton.style.minWidth = 90;
            pasteButton.style.marginLeft = 8;
            inputRow.Add(pasteButton);

            panel.Add(inputRow);

            var statusLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 12,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 6,
                    display = DisplayStyle.None
                }
            };
            WalletUiCommon.ApplyDefaultFont(statusLabel);
            panel.Add(statusLabel);

            var listHeader = new Label(validAccounts.Count == 0 ? "No wallets available" : $"{validAccounts.Count} wallet(s) on this device")
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 12,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 4
                }
            };
            WalletUiCommon.ApplyDefaultFont(listHeader);
            panel.Add(listHeader);

            var listWrapper = WalletUiCommon.BuildScrollContainer(
                out var list,
                onScrollChanged: null,
                shouldBlockWheel: () => false,
                paddingLeft: 10f,
                paddingRight: 10f,
                paddingTop: 6f,
                paddingBottom: 12f,
                marginTop: 4f,
                marginBottom: 8f,
                maxWidth: 0f,
                alignSelf: Align.Stretch);
            list.style.flexGrow = 1;
            list.style.flexShrink = 1;
            list.style.flexBasis = 0;
            list.style.minHeight = 0;

            var rowEntries = new List<(string address, string name, VisualElement row)>();
            IVisualElementScheduledItem pasteSchedule = null;
            Button confirmBtn = null;

            void UpdateRowSelection(string value)
            {
                var trimmed = value?.Trim() ?? string.Empty;
                foreach (var (address, _, row) in rowEntries)
                {
                    var isSelected = string.Equals(address, trimmed, StringComparison.OrdinalIgnoreCase);
                    StyleSelectedRow(row, isSelected);
                }
            }

            void UpdateStatus(string text)
            {
                statusLabel.text = text ?? string.Empty;
                statusLabel.style.display = string.IsNullOrWhiteSpace(text) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            bool IsInputValid(string value)
            {
                var trimmed = value?.Trim() ?? string.Empty;
                return Address.IsValidAddress(trimmed);
            }

            void UpdateConfirmState(string value)
            {
                var trimmed = value?.Trim() ?? string.Empty;
                var enabled = IsInputValid(trimmed);
                WalletUiCommon.SetButtonEnabledVisual(confirmBtn, enabled, WalletUiTheme.TextPrimary, WalletUiTheme.TextMuted);
            }

            void ApplySelection(string address)
            {
                var trimmed = address?.Trim() ?? string.Empty;
                destinationField.SetValueWithoutNotify(trimmed);
                UpdateRowSelection(trimmed);
                UpdateConfirmState(trimmed);
                UpdateStatus(string.Empty);
                RebuildList(trimmed, trimmed);
            }

            void RefreshPasteState()
            {
                var clipboard = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
                var isValid = Address.IsValidAddress(clipboard);
                WalletUiCommon.SetButtonEnabledVisual(pasteButton, isValid, WalletUiTheme.TextPrimary, WalletUiTheme.TextMuted);
                pasteButton.clicked -= PasteHandler;
                if (isValid)
                {
                    pasteButton.clicked += PasteHandler;
                }
            }

            void PasteHandler()
            {
                var clipboard = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
                if (!Address.IsValidAddress(clipboard))
                {
                    return;
                }

                ApplySelection(clipboard);
            }

            void Complete(PromptResult result, string value)
            {
                pasteSchedule?.Pause();
                pasteSchedule = null;
                host.HidePanel(onAfterHide);
                tcs.TrySetResult((result, result == PromptResult.Success ? value?.Trim() ?? string.Empty : string.Empty));
            }

            bool MatchesFilter(Account account, string filter)
            {
                if (string.IsNullOrWhiteSpace(filter))
                {
                    return true;
                }

                var trimmed = filter.Trim();
                if (trimmed.Length == 0)
                {
                    return true;
                }

                var name = account.name ?? string.Empty;
                var address = account.phaAddress ?? string.Empty;
                return name.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       address.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            void RebuildList(string filter, string selectedValue)
            {
                rowEntries.Clear();
                list.Clear();

                if (validAccounts.Count == 0)
                {
                    listWrapper.style.minHeight = 120;
                    listWrapper.style.maxHeight = 120;
                    listHeader.text = "No wallets available";
                    var emptyWallets = new Label("Add another wallet to enable quick picking.")
                    {
                        style =
                        {
                            color = WalletUiTheme.TextSecondary,
                            fontSize = 13,
                            unityTextAlign = TextAnchor.MiddleCenter,
                            marginTop = 10,
                            marginBottom = 10,
                            whiteSpace = WhiteSpace.Normal
                        }
                    };
                    WalletUiCommon.ApplyDefaultFont(emptyWallets);
                    list.Add(emptyWallets);
                    UpdateRowSelection(selectedValue);
                    return;
                }

                var matches = validAccounts.Where(acc => MatchesFilter(acc, filter)).ToList();
                if (matches.Count > 0)
                {
                    var estimatedHeight = Mathf.Clamp(matches.Count * 64f, 180f, 420f);
                    listWrapper.style.minHeight = estimatedHeight;
                    listWrapper.style.maxHeight = 420f;

                    foreach (var account in matches)
                    {
                        var name = string.IsNullOrWhiteSpace(account.name) ? "Wallet" : account.name;
                        var address = account.phaAddress?.Trim() ?? string.Empty;
                        var row = CreateAddressRow(name, address, () => ApplySelection(address));
                        rowEntries.Add((address, name, row));
                        list.Add(row);
                    }
                }
                else
                {
                    listWrapper.style.minHeight = 120;
                    listWrapper.style.maxHeight = 120;
                    var emptyLabel = new Label("No matching wallets.")
                    {
                        style =
                        {
                            color = WalletUiTheme.TextSecondary,
                            fontSize = 13,
                            unityTextAlign = TextAnchor.MiddleCenter,
                            marginTop = 10,
                            marginBottom = 10,
                            whiteSpace = WhiteSpace.Normal
                        }
                    };
                    WalletUiCommon.ApplyDefaultFont(emptyLabel);
                    list.Add(emptyLabel);
                }

                var headerCount = matches.Count;
                listHeader.text = headerCount == 0 ? "0 wallet(s)" : $"{headerCount} wallet(s)";
                UpdateRowSelection(selectedValue);
            }

            panel.Add(listWrapper);

            var buttons = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexEnd,
                    marginTop = 8,
                    flexShrink = 0
                }
            };
            WalletUiCommon.ApplyDefaultFont(buttons);

            var cancelBtn = WalletUiCommon.CreateSecondaryButton(string.IsNullOrWhiteSpace(cancelLabel) ? "Cancel" : cancelLabel, () => Complete(PromptResult.Failure, string.Empty), 16, 36);
            cancelBtn.style.minWidth = 110;
            buttons.Add(cancelBtn);

            confirmBtn = WalletUiCommon.CreateOutlineButton(string.IsNullOrWhiteSpace(confirmLabel) ? "Confirm" : confirmLabel, () =>
            {
                var trimmed = destinationField.value?.Trim() ?? string.Empty;
                if (!IsInputValid(trimmed))
                {
                    UpdateStatus("Enter a valid destination address.");
                    UpdateConfirmState(trimmed);
                    return;
                }

                Complete(PromptResult.Success, trimmed);
            }, 16, 36);
            confirmBtn.style.minWidth = 120;
            confirmBtn.style.marginLeft = 10;
            buttons.Add(confirmBtn);
            panel.Add(buttons);

            destinationField.RegisterValueChangedCallback(evt =>
            {
                var trimmed = evt.newValue?.Trim() ?? string.Empty;
                UpdateConfirmState(trimmed);
                UpdateStatus(string.Empty);
                RebuildList(trimmed, trimmed);
            });

            var initialInput = destinationField.value?.Trim() ?? string.Empty;
            UpdateRowSelection(initialInput);
            UpdateConfirmState(initialInput);
            RefreshPasteState();
            RebuildList(initialInput, initialInput);

            // Unity does not surface clipboard change events, so we poll while the modal is visible to keep Paste state in sync.
            pasteSchedule = panel.schedule.Execute(RefreshPasteState).Every(500);
            panel.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                pasteSchedule?.Pause();
                pasteSchedule = null;
            });

            host.ShowPanel(panel, onBeforeShow);
            return tcs.Task;
        }

        /// <summary>
        /// Displays a modal with vertically stacked action buttons to pick one of the supplied options.
        /// Returns the zero-based index of the chosen option or -1 when cancelled.
        /// </summary>
        public static Task<int> ShowChoiceDialogAsync(
            WalletUiModalHost host,
            string title,
            string caption,
            IReadOnlyList<(string title, string description)> options,
            string cancelLabel = "Cancel",
            Action onBeforeShow = null,
            Action onAfterHide = null)
        {
            if (host == null || options == null || options.Count == 0)
            {
                return Task.FromResult(-1);
            }

            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Complete(int choice)
            {
                host.HidePanel(onAfterHide);
                tcs.TrySetResult(choice);
            }

            var panel = WalletUiCommon.CreateModalPanel(520, 820);
            panel.style.maxWidth = new Length(95, LengthUnit.Percent);

            var titleLabel = new Label(string.IsNullOrWhiteSpace(title) ? "Choose option" : title)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 20,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 6
                }
            };
            WalletUiCommon.ApplyDefaultFont(titleLabel);
            panel.Add(titleLabel);

            if (!string.IsNullOrWhiteSpace(caption))
            {
                var captionLabel = new Label(caption)
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        fontSize = 14,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        marginBottom = 10,
                        whiteSpace = WhiteSpace.Normal
                    }
                };
                WalletUiCommon.ApplyDefaultFont(captionLabel);
                panel.Add(captionLabel);
            }

            var optionsContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    alignItems = Align.Stretch,
                    marginTop = 4,
                    marginBottom = 6
                }
            };
            WalletUiCommon.ApplyDefaultFont(optionsContainer);

            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                var optionIndex = i; // Capture per-iteration index to avoid all buttons resolving to the last option.
                var button = CreateChoiceButton(option.title, option.description, () => Complete(optionIndex));
                if (i > 0)
                {
                    button.style.marginTop = 8;
                }
                optionsContainer.Add(button);
            }
            panel.Add(optionsContainer);

            var actions = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.FlexEnd,
                    alignItems = Align.Center,
                    marginTop = 8,
                    flexShrink = 0
                }
            };
            WalletUiCommon.ApplyDefaultFont(actions);

            var cancelBtn = WalletUiCommon.CreateSecondaryButton(string.IsNullOrWhiteSpace(cancelLabel) ? "Cancel" : cancelLabel, () => Complete(-1), 16, 36);
            cancelBtn.style.minWidth = 120;
            actions.Add(cancelBtn);
            panel.Add(actions);

            host.ShowPanel(panel, onBeforeShow);
            return tcs.Task;
        }

        private static VisualElement CreateAddressRow(string name, string address, Action onSelect)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.FlexStart,
                    paddingLeft = 12,
                    paddingRight = 12,
                    paddingTop = 10,
                    paddingBottom = 10,
                    marginBottom = 8,
                    backgroundColor = WalletUiTheme.CardBackground,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.CardBorder,
                    borderRightColor = WalletUiTheme.CardBorder,
                    borderTopColor = WalletUiTheme.HighlightEdge,
                    borderBottomColor = WalletUiTheme.CardBorder,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall
                }
            };
            WalletUiCommon.ApplyDefaultFont(row);
            row.pickingMode = PickingMode.Position;

            var textColumn = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1,
                    flexShrink = 1,
                    minWidth = 0
                }
            };
            WalletUiCommon.ApplyDefaultFont(textColumn);

            var titleLabel = new Label(string.IsNullOrWhiteSpace(name) ? "Wallet" : name)
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 15,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.UpperLeft,
                    whiteSpace = WhiteSpace.Normal
                }
            };
            WalletUiCommon.ApplyDefaultFont(titleLabel);
            textColumn.Add(titleLabel);

            var subtitleLabel = new Label(string.IsNullOrWhiteSpace(address) ? "(no address)" : address)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 13,
                    unityTextAlign = TextAnchor.UpperLeft,
                    whiteSpace = WhiteSpace.Normal,
                    marginTop = 4
                }
            };
            WalletUiCommon.ApplyDefaultFont(subtitleLabel);
            textColumn.Add(subtitleLabel);

            row.Add(textColumn);
            row.RegisterCallback<ClickEvent>(_ => onSelect?.Invoke());
            StyleSelectedRow(row, false);
            return row;
        }

        private static void StyleSelectedRow(VisualElement row, bool isSelected)
        {
            if (row == null)
            {
                return;
            }

            var borderColor = isSelected ? WalletUiTheme.AccentPrimary : WalletUiTheme.CardBorder;
            row.style.backgroundColor = isSelected ? WalletUiTheme.PanelBackground : WalletUiTheme.CardBackground;
            row.style.borderLeftColor = borderColor;
            row.style.borderRightColor = borderColor;
            row.style.borderBottomColor = borderColor;
            row.style.borderTopColor = isSelected ? WalletUiTheme.AccentPrimarySoft : WalletUiTheme.HighlightEdge;
        }

        private static VisualElement CreateListRow(int index, string title, string subtitle)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.FlexStart,
                    paddingLeft = 12,
                    paddingRight = 12,
                    paddingTop = 10,
                    paddingBottom = 10,
                    marginBottom = 8,
                    backgroundColor = WalletUiTheme.CardBackground,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.CardBorder,
                    borderRightColor = WalletUiTheme.CardBorder,
                    borderTopColor = WalletUiTheme.HighlightEdge,
                    borderBottomColor = WalletUiTheme.CardBorder,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall
                }
            };
            WalletUiCommon.ApplyDefaultFont(row);

            var numberLabel = new Label($"{index}.")
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 14,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.UpperLeft,
                    marginRight = 10,
                    minWidth = 32
                }
            };
            WalletUiCommon.ApplyDefaultFont(numberLabel);
            row.Add(numberLabel);

            var textColumn = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1,
                    flexShrink = 1,
                    minWidth = 0
                }
            };
            WalletUiCommon.ApplyDefaultFont(textColumn);

            var titleLabel = new Label(string.IsNullOrWhiteSpace(title) ? "(empty)" : title)
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 15,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.UpperLeft,
                    whiteSpace = WhiteSpace.Normal
                }
            };
            WalletUiCommon.ApplyDefaultFont(titleLabel);
            textColumn.Add(titleLabel);

            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                var subtitleLabel = new Label(subtitle)
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        fontSize = 13,
                        unityTextAlign = TextAnchor.UpperLeft,
                        whiteSpace = WhiteSpace.Normal,
                        marginTop = 4
                    }
                };
                WalletUiCommon.ApplyDefaultFont(subtitleLabel);
                textColumn.Add(subtitleLabel);
            }

            row.Add(textColumn);
            return row;
        }

        private static Button CreateChoiceButton(string title, string description, Action onClick)
        {
            var button = WalletUiCommon.CreateOutlineButton(string.IsNullOrWhiteSpace(title) ? "Select" : title, onClick, 16, 56);
            button.text = string.Empty;
            button.style.flexDirection = FlexDirection.Column;
            button.style.alignItems = Align.FlexStart;
            button.style.justifyContent = Justify.Center;
            button.style.width = new Length(100, LengthUnit.Percent);
            button.style.whiteSpace = WhiteSpace.Normal;
            button.style.unityTextAlign = TextAnchor.UpperLeft;
            button.style.paddingTop = 12;
            button.style.paddingBottom = 12;
            button.style.paddingLeft = 14;
            button.style.paddingRight = 14;

            var titleLabel = new Label(string.IsNullOrWhiteSpace(title) ? "Select" : title)
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 16,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    whiteSpace = WhiteSpace.Normal
                }
            };
            WalletUiCommon.ApplyDefaultFont(titleLabel);
            button.Add(titleLabel);

            if (!string.IsNullOrWhiteSpace(description))
            {
                var descriptionLabel = new Label(description)
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        fontSize = 13,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        marginTop = 4,
                        whiteSpace = WhiteSpace.Normal
                    }
                };
                WalletUiCommon.ApplyDefaultFont(descriptionLabel);
                button.Add(descriptionLabel);
            }

            return button;
        }

        public static Task<(PromptResult result, string input)> ShowErrorAsync(WalletUiModalHost host, string title, string message, Action onBeforeShow = null, Action onAfterHide = null, bool showSecondary = false)
        {
            if (host == null)
            {
                return Task.FromResult((PromptResult.Failure, string.Empty));
            }

            return host.ShowPromptAsync(
                string.IsNullOrWhiteSpace(title) ? "Error" : title,
                message ?? string.Empty,
                0,
                0,
                allowEmpty: true,
                hasInput: false,
                isPassword: false,
                multiline: false,
                primaryLabel: "Close",
                secondaryLabel: "Cancel",
                showSecondary: showSecondary,
                successResult: PromptResult.Failure,
                cancelResult: PromptResult.Failure,
                onBeforeShow: onBeforeShow,
                onAfterHide: onAfterHide);
        }
    }
}
