using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poltergeist;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Unity.Core.Logging;
using PhantasmaPhoenix.RPC.Models;

namespace Poltergeist.UiToolkit
{
    internal enum WalletUiStatusIntent
    {
        None = 0,
        TransientShort,
        TransientLong
    }

    /// <summary>
    /// Shared UI building blocks for the UITK wallet screens.
    /// Keeps styling consistent with the legacy IMGUI look while we migrate.
    /// </summary>
    internal static class WalletUiCommon
    {
        private const string AppTitle = "Poltergeist Lite";
        private static readonly Color SoftOutlineWhite = WalletUiTheme.Hex("#dfe3f0");
        internal const float StatusAutoHideSeconds = 3f;
        internal const float StatusAutoHideLongSeconds = 5f;

        private sealed class StatusLabelState
        {
            public IVisualElementScheduledItem ScheduledItem;
            public int Version;
        }

        private static readonly Dictionary<Label, StatusLabelState> StatusStates = new Dictionary<Label, StatusLabelState>();

        internal static HeaderElements BuildHeader(VisualElement rightContent = null)
        {
            var header = new VisualElement
            {
                style =
                {
                    minHeight = 76,
                    backgroundColor = WalletUiTheme.HeaderBackground,
                    borderLeftWidth = 0,
                    borderRightWidth = 0,
                    borderTopWidth = 0,
                    borderBottomWidth = 0,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium,
                    paddingLeft = 18,
                    paddingRight = 18,
                    paddingTop = 8,
                    paddingBottom = 8,
                    flexDirection = FlexDirection.Column,
                    justifyContent = Justify.Center,
                    position = Position.Relative,
                    backgroundImage = new StyleBackground(),
                    borderTopColor = WalletUiTheme.HeaderBorder,
                    borderBottomColor = WalletUiTheme.HeaderBorder,
                    borderLeftColor = WalletUiTheme.HeaderBorder,
                    borderRightColor = WalletUiTheme.HeaderBorder
                }
            };
            ApplyDefaultFont(header);

            var titleRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    width = new Length(100, LengthUnit.Percent)
                }
            };

            var titleGroup = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center
                }
            };

            var titleLabel = new Label(AppTitle)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 30,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            ApplyDefaultFont(titleLabel);

            var versionLabel = new Label(BuildVersionLabel())
            {
                style =
                {
                    fontSize = 15,
                    color = WalletUiTheme.TextSecondary,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginLeft = 10
                }
            };
            ApplyDefaultFont(versionLabel);

            titleGroup.Add(titleLabel);
            titleGroup.Add(versionLabel);

            titleRow.Add(titleGroup);

            header.Add(titleRow);

            if (rightContent != null)
            {
                rightContent.style.position = Position.Absolute;
                rightContent.style.right = 16;
                rightContent.style.top = new Length(50, LengthUnit.Percent);
                rightContent.style.translate = new Translate(new Length(0, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
                header.Add(rightContent);
            }

            return new HeaderElements(header);
        }

        internal static SubHeaderElements BuildSubHeader(string subtitleText, string leftText = "")
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexStart,
                    width = new Length(100, LengthUnit.Percent),
                    marginBottom = 14,
                    position = Position.Relative
                }
            };
            ApplyDefaultFont(row);

            // Left label keeps a fixed footprint; we mirror it on the right to keep the subtitle centered without absolute positioning.
            var leftLabel = new Label(leftText ?? string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 14,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    minWidth = 220,
                    maxWidth = 360,
                    marginRight = 16
                }
            };
            ApplyDefaultFont(leftLabel);
            leftLabel.style.flexShrink = 0;

            var subtitleGroup = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    flexGrow = 1,
                    flexShrink = 1,
                    minWidth = 0
                }
            };

            var subtitle = new Label(subtitleText ?? string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 18,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    whiteSpace = WhiteSpace.Normal,
                    minWidth = 80,
                    maxWidth = new Length(100, LengthUnit.Percent),
                    flexShrink = 1
                }
            };
            ApplyDefaultFont(subtitle);

            var network = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 15,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginLeft = 8
                }
            };
            ApplyDefaultFont(network);

            subtitleGroup.Add(subtitle);
            subtitleGroup.Add(network);

            // Spacer matches left label dimensions so subtitle + badge stay centered even while RPC/network badge toggles visibility.
            var rightSpacer = new VisualElement
            {
                style =
                {
                    minWidth = leftLabel.style.minWidth,
                    maxWidth = leftLabel.style.maxWidth,
                    marginLeft = leftLabel.style.marginRight,
                    flexShrink = 0,
                    flexGrow = 0
                }
            };

            row.Add(leftLabel);
            row.Add(subtitleGroup);
            row.Add(rightSpacer);

            return new SubHeaderElements(row, leftLabel, subtitle, network);
        }

        // Shared status strip builder to keep status messages visually consistent across screens.
        internal static Label CreateStatusLabel(TextAnchor alignment = TextAnchor.MiddleCenter, Align alignSelf = Align.Stretch)
        {
            var statusLabel = new Label(string.Empty)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 14,
                    color = WalletUiTheme.TextSecondary,
                    unityTextAlign = alignment,
                    alignSelf = alignSelf,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 4,
                    paddingBottom = 4,
                    backgroundColor = WalletUiTheme.PanelBackground,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    minHeight = 24,
                    marginTop = 0,
                    marginBottom = 0
                }
            };
            ApplyDefaultFont(statusLabel);
            statusLabel.style.visibility = Visibility.Hidden;
            statusLabel.style.display = DisplayStyle.Flex;
            statusLabel.style.flexShrink = 0;
            return statusLabel;
        }

        private static StatusLabelState GetStatusState(Label label)
        {
            if (!StatusStates.TryGetValue(label, out var state))
            {
                state = new StatusLabelState();
                StatusStates[label] = state;
            }

            return state;
        }

        private static void CancelScheduledClear(StatusLabelState state)
        {
            if (state?.ScheduledItem != null)
            {
                state.ScheduledItem.Pause();
                state.ScheduledItem = null;
            }
        }

        private static float GetAutoHideSeconds(WalletUiStatusIntent intent, float overrideSeconds)
        {
            if (overrideSeconds > 0f)
            {
                return overrideSeconds;
            }

            switch (intent)
            {
                case WalletUiStatusIntent.TransientShort:
                    return StatusAutoHideSeconds;
                case WalletUiStatusIntent.TransientLong:
                    return StatusAutoHideLongSeconds;
                default:
                    return 0f;
            }
        }

        internal static void UpdateStatusLabel(Label label, string text, float marginTopWhenVisible = 6f, float marginBottomWhenVisible = 10f, float autoHideSeconds = 0f, WalletUiStatusIntent intent = WalletUiStatusIntent.None)
        {
            if (label == null)
            {
                return;
            }

            var hasText = !string.IsNullOrWhiteSpace(text);
            label.text = hasText ? text : string.Empty;
            label.style.visibility = hasText ? Visibility.Visible : Visibility.Hidden;
            label.style.display = DisplayStyle.Flex;
            label.style.marginTop = hasText ? marginTopWhenVisible : 0f;
            label.style.marginBottom = hasText ? marginBottomWhenVisible : 0f;
            label.style.minHeight = hasText ? 24f : 0f;

            var state = GetStatusState(label);
            state.Version++;
            CancelScheduledClear(state);

            var resolvedAutoHide = GetAutoHideSeconds(intent, autoHideSeconds);
            if (!hasText || resolvedAutoHide <= 0f)
            {
                return;
            }

            var versionAtSchedule = state.Version;
            var delayMs = Mathf.Max(1, Mathf.RoundToInt(resolvedAutoHide * 1000f));
            state.ScheduledItem = label.schedule.Execute(() =>
            {
                if (!StatusStates.TryGetValue(label, out var currentState) || currentState.Version != versionAtSchedule)
                {
                    return;
                }

                CancelScheduledClear(currentState);
                UpdateStatusLabel(label, string.Empty, marginTopWhenVisible, marginBottomWhenVisible);
            }).StartingIn(delayMs);
        }

        // Shared header + subheader block so all screens stay consistent; callers can tweak margins for edge cases.
        internal static HeaderBlockElements BuildHeaderBlock(string subHeaderSubtitle, string subHeaderLeft = "", VisualElement rightContent = null, VisualElement middleContent = null, float headerMarginBottom = 12f, float subHeaderMarginTop = 8f, float subHeaderMarginBottom = 10f)
        {
            var container = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    width = new Length(100, LengthUnit.Percent),
                    alignSelf = Align.Stretch
                }
            };
            ApplyDefaultFont(container);

            var header = BuildHeader(rightContent);
            header.Root.style.marginBottom = headerMarginBottom;
            header.Root.style.width = new Length(100, LengthUnit.Percent);
            header.Root.style.alignSelf = Align.Stretch;
            container.Add(header.Root);

            if (middleContent != null)
            {
                ApplyDefaultFont(middleContent);
                middleContent.style.alignSelf = Align.Center;
                container.Add(middleContent);
            }

            var subHeader = BuildSubHeader(subHeaderSubtitle, subHeaderLeft);
            subHeader.Root.style.marginTop = subHeaderMarginTop;
            subHeader.Root.style.marginBottom = subHeaderMarginBottom;
            subHeader.Root.style.width = new Length(100, LengthUnit.Percent);
            subHeader.Root.style.alignSelf = Align.Stretch;
            container.Add(subHeader.Root);

            return new HeaderBlockElements(container, header, subHeader);
        }

        internal static AccountInfoElements BuildAccountInfo(Action onCopy, Action onExplorer, bool showTitleRow = true)
        {
            var container = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    marginTop = 6,
                    marginBottom = 6
                }
            };
            ApplyDefaultFont(container);

            var titleRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center
                }
            };

            var accountLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 16,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    unityFontStyleAndWeight = FontStyle.Bold
                }
            };
            ApplyDefaultFont(accountLabel);

            var networkLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 14,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginLeft = 8
                }
            };
            ApplyDefaultFont(networkLabel);

            titleRow.Add(accountLabel);
            titleRow.Add(networkLabel);
            container.Add(titleRow);
            if (!showTitleRow)
            {
                titleRow.style.display = DisplayStyle.None;
            }

            var addressLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 14,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginTop = 2
                }
            };
            ApplyDefaultFont(addressLabel);
            container.Add(addressLabel);

            var buttons = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    marginTop = 10,
                    marginBottom = 6
                }
            };

            var copy = CreateSecondaryButton("Copy Address", onCopy, 14, 36);
            copy.style.minWidth = 140;
            buttons.Add(copy);

            var explorer = CreateSecondaryButton("Explorer", onExplorer, 14, 36);
            explorer.style.marginLeft = 10;
            explorer.style.minWidth = 140;
            buttons.Add(explorer);

            container.Add(buttons);

            return new AccountInfoElements(container, accountLabel, addressLabel, networkLabel);
        }

        // Unified footer builder for all screens (nav bars + main footers). Styles live only here.
        internal static VisualElement BuildFooter(out Button[] buttons, params (string text, Action onClick)[] entries)
        {
            var bar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    justifyContent = Justify.Center,
                    alignItems = Align.Stretch,
                    paddingTop = 14,
                    paddingBottom = 14,
                    paddingLeft = 12,
                    paddingRight = 12,
                    marginTop = 14,
                    marginBottom = 6,
                    minHeight = 72,
                    width = new Length(100, LengthUnit.Percent),
                    alignSelf = Align.Stretch,
                    backgroundColor = WalletUiTheme.HeaderBackground,
                    borderTopWidth = 1,
                    borderTopColor = WalletUiTheme.HeaderBorder,
                    borderBottomWidth = 1,
                    borderBottomColor = WalletUiTheme.HeaderBorder,
                    borderLeftWidth = 1,
                    borderLeftColor = WalletUiTheme.HeaderBorder,
                    borderRightWidth = 1,
                    borderRightColor = WalletUiTheme.HeaderBorder,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium,
                    flexShrink = 0
                }
            };
            ApplyDefaultFont(bar);

            buttons = new Button[entries.Length];
            for (var i = 0; i < entries.Length; i++)
            {
                var (text, onClick) = entries[i];
                var btn = CreateFooterButton(text, onClick);
                btn.style.flexGrow = 1;
                btn.style.flexShrink = 1;
                btn.style.flexBasis = 0;
                btn.style.minWidth = 140;
                btn.style.marginLeft = 0;
                btn.style.marginRight = 8;
                btn.style.marginBottom = 8;
                buttons[i] = btn;
            }

            var buttonRow = CreateButtonRow(0f, buttons);
            buttonRow.style.marginTop = 0;
            buttonRow.style.marginBottom = 0;
            buttonRow.style.paddingLeft = 0;
            buttonRow.style.paddingRight = 0;
            buttonRow.style.paddingTop = 0;
            buttonRow.style.paddingBottom = 0;
            buttonRow.style.justifyContent = Justify.FlexStart;
            buttonRow.style.alignItems = Align.Center;
            buttonRow.style.width = new Length(100, LengthUnit.Percent);
            buttonRow.style.alignSelf = Align.Stretch;
            buttonRow.style.flexWrap = Wrap.Wrap;

            bar.Add(buttonRow);
            return bar;
        }

        internal static VisualElement BuildMainFooter(Action onNewWallet, Action onManageWallets, Action onSettings)
        {
            return BuildFooter(
                out _,
                ("New wallet", onNewWallet),
                ("Manage", onManageWallets),
                ("Settings", onSettings)
            );
        }

        internal static VisualElement BuildWalletNavBar(out Button balances, out Button history, out Button account, out Button exit, Action onBalances, Action onHistory, Action onAccount, Action onExit)
        {
            var bar = BuildFooter(
                out var buttons,
                ("Balances", onBalances),
                ("History", onHistory),
                ("Account", onAccount),
                ("Wallets", onExit)
            );

            balances = buttons.Length > 0 ? buttons[0] : null;
            history = buttons.Length > 1 ? buttons[1] : null;
            account = buttons.Length > 2 ? buttons[2] : null;
            exit = buttons.Length > 3 ? buttons[3] : null;
            return bar;
        }

        // Applies the default full-screen layout for all UITK screens to avoid drift between views.
        internal static void ConfigureScreenRoot(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1;
            root.style.flexShrink = 1;
            root.style.flexBasis = 0;
            root.style.width = new Length(100, LengthUnit.Percent);
            root.style.height = new Length(100, LengthUnit.Percent);
            root.style.minHeight = 0;
            root.style.minWidth = 0;
            root.style.alignItems = Align.Stretch;
            root.style.overflow = Overflow.Hidden;
        }

        // Shared content container used across screens (balances/history/accounts/settings) to keep scroll math consistent.
        internal static VisualElement CreateScreenContent(float paddingLeft = 8f, float paddingRight = 8f, float paddingTop = 0f, float paddingBottom = 0f, float maxWidth = 1680f, bool fullHeight = true, bool hiddenOverflow = true)
        {
            var content = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    width = new Length(100, LengthUnit.Percent),
                    maxWidth = maxWidth,
                    alignSelf = Align.Center,
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = 0,
                    minHeight = 0,
                    paddingLeft = paddingLeft,
                    paddingRight = paddingRight,
                    paddingTop = paddingTop,
                    paddingBottom = paddingBottom
                }
            };

            if (fullHeight)
            {
                content.style.height = new Length(100, LengthUnit.Percent);
            }

            if (hiddenOverflow)
            {
                content.style.overflow = Overflow.Hidden;
            }

            ApplyDefaultFont(content);
            return content;
        }

        // Wrapper for scroll views so the footer never gets squeezed; matches the pattern proven in Settings.
        internal static VisualElement CreateScrollWrapper()
        {
            var wrapper = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = 0,
                    minHeight = 0,
                    overflow = Overflow.Hidden
                }
            };
            ApplyDefaultFont(wrapper);
            return wrapper;
        }

        // Unified scroll container used by all screens (lists + forms) to keep desktop/mobile layout consistent.
        internal static VisualElement BuildScrollContainer(
            out ScrollView scrollView,
            Action<float> onScrollChanged,
            Func<bool> shouldBlockWheel = null,
            float paddingLeft = 8f,
            float paddingRight = 8f,
            float paddingTop = 8f,
            float paddingBottom = 80f,
            float marginTop = 6f,
            float marginBottom = 10f,
            float maxWidth = 1680f,
            Align alignSelf = Align.Center)
        {
            scrollView = CreateScrollView(onScrollChanged, shouldBlockWheel, paddingBottom);
            // Keep the ScrollView geometry stable: padding lives on the content container, while
            // margins/max-width live on the wrapper. This mirrors the Settings layout that does not
            // resize during scroll, and keeps the same code working on desktop and mobile.
            scrollView.style.backgroundImage = new StyleBackground();
            scrollView.style.borderTopLeftRadius = 0;
            scrollView.style.borderTopRightRadius = 0;
            scrollView.style.borderBottomLeftRadius = 0;
            scrollView.style.borderBottomRightRadius = 0;
            scrollView.style.borderLeftWidth = 0;
            scrollView.style.borderRightWidth = 0;
            scrollView.style.borderTopWidth = 0;
            scrollView.style.borderBottomWidth = 0;
            scrollView.contentContainer.style.paddingLeft = paddingLeft;
            scrollView.contentContainer.style.paddingRight = paddingRight;
            scrollView.contentContainer.style.paddingTop = paddingTop;
            if (paddingBottom > 0f)
            {
                scrollView.contentContainer.style.paddingBottom = paddingBottom;
            }
            scrollView.style.marginTop = 0;
            scrollView.style.marginBottom = 0;
            scrollView.style.alignSelf = Align.Stretch;
            scrollView.style.width = new Length(100, LengthUnit.Percent);

            ApplyDefaultFont(scrollView);

            var wrapper = CreateScrollWrapper();
            wrapper.style.marginTop = marginTop;
            wrapper.style.marginBottom = marginBottom;
            wrapper.style.alignSelf = alignSelf;
            wrapper.style.width = new Length(100, LengthUnit.Percent);
            if (maxWidth > 0f)
            {
                wrapper.style.maxWidth = maxWidth;
            }
            wrapper.Add(scrollView);
            return wrapper;
        }

        // Shared button row: always wraps on narrow widths, with uniform padding/spacing to keep layout consistent.
        internal static VisualElement CreateButtonRow(params Button[] buttons)
        {
            return CreateButtonRow(8f, buttons);
        }

        internal static VisualElement CreateButtonRow(float spacing, params Button[] buttons)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    alignSelf = Align.Center,
                    marginTop = 0,
                    marginBottom = 0,
                    flexWrap = Wrap.Wrap,
                    paddingLeft = 4,
                    paddingRight = 4,
                    paddingTop = 4,
                    paddingBottom = 4
                }
            };
            ApplyDefaultFont(row);

            if (buttons != null && buttons.Length > 0)
            {
                var index = 0;
                foreach (var button in buttons)
                {
                    if (button == null)
                    {
                        continue;
                    }

                    if (index > 0 && spacing > 0f)
                    {
                        button.style.marginLeft = spacing;
                    }

                    button.style.marginTop = 0;
                    button.style.marginBottom = 4;
                    button.style.alignSelf = Align.Center;
                    var minHeight = button.style.minHeight;
                    var currentMinHeight = minHeight.keyword == StyleKeyword.Undefined ? minHeight.value.value : 0f;
                    if (currentMinHeight < 32f)
                    {
                        button.style.minHeight = 32;
                    }

                    row.Add(button);
                    index++;
                }
            }

            return row;
        }

        internal static Button CreateFooterButton(string text, Action onClick)
        {
            var btn = new Button
            {
                text = text,
                style =
                {
                    backgroundColor = WalletUiTheme.SecondaryButton,
                    color = Color.white,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 22,
                    minHeight = 56,
                    paddingLeft = 20,
                    paddingRight = 20,
                    paddingTop = 14,
                    paddingBottom = 14,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.SecondaryButtonBorder,
                    borderRightColor = WalletUiTheme.SecondaryButtonBorder,
                    borderTopColor = WalletUiTheme.SecondaryButtonBorder,
                    borderBottomColor = WalletUiTheme.SecondaryButtonBorder,
                    flexBasis = 0,
                    flexShrink = 1
                }
            };
            ApplyDefaultFont(btn);
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            MakeButtonNonNavigable(btn);
            btn.clicked += () => onClick?.Invoke();
            return btn;
        }

        internal static Button CreatePrimaryButton(string text, Action onClick, int fontSize = 16, int minHeight = 44)
        {
            var btn = new Button
            {
                text = text,
                style =
                {
                    backgroundColor = WalletUiTheme.ActionButton,
                    color = WalletUiTheme.ActionButtonText,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = fontSize,
                    minHeight = minHeight,
                    paddingLeft = 18,
                    paddingRight = 18,
                    paddingTop = 12,
                    paddingBottom = 12,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.ActionButtonBorder,
                    borderRightColor = WalletUiTheme.ActionButtonBorder,
                    borderTopColor = WalletUiTheme.ActionButtonBorder,
                    borderBottomColor = WalletUiTheme.ActionButtonBorder
                }
            };
            ApplyDefaultFont(btn);
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            MakeButtonNonNavigable(btn);
            btn.clicked += () => onClick?.Invoke();
            return btn;
        }

        internal static Button CreateOutlineButton(string text, Action onClick, int fontSize = 18, int minHeight = 44)
        {
            var btn = new Button
            {
                text = text,
                style =
                {
                    backgroundColor = Color.clear,
                    color = SoftOutlineWhite,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = fontSize,
                    minHeight = minHeight,
                    paddingLeft = 18,
                    paddingRight = 18,
                    paddingTop = 10,
                    paddingBottom = 10,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium,
                    borderLeftWidth = 2,
                    borderRightWidth = 2,
                    borderTopWidth = 2,
                    borderBottomWidth = 2,
                    borderLeftColor = SoftOutlineWhite,
                    borderRightColor = SoftOutlineWhite,
                    borderTopColor = SoftOutlineWhite,
                    borderBottomColor = SoftOutlineWhite
                }
            };
            ApplyDefaultFont(btn);
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            MakeButtonNonNavigable(btn);
            btn.clicked += () => onClick?.Invoke();
            return btn;
        }

        internal static Button CreateListActionButton(string text, Action onClick, int minWidth = 110, int minHeight = 40)
        {
            var btn = CreatePrimaryButton(text, onClick, 14, minHeight);
            btn.style.minWidth = minWidth;
            btn.style.paddingLeft = 14;
            btn.style.paddingRight = 14;
            btn.style.paddingTop = 8;
            btn.style.paddingBottom = 8;
            btn.style.borderTopLeftRadius = WalletUiTheme.RadiusSmall;
            btn.style.borderTopRightRadius = WalletUiTheme.RadiusSmall;
            btn.style.borderBottomLeftRadius = WalletUiTheme.RadiusSmall;
            btn.style.borderBottomRightRadius = WalletUiTheme.RadiusSmall;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            MakeButtonNonNavigable(btn);
            return btn;
        }

        internal static Button CreateSecondaryButton(string text, Action onClick, int fontSize = 14, int minHeight = 32)
        {
            var btn = new Button
            {
                text = text,
                style =
                {
                    backgroundColor = WalletUiTheme.SecondaryButton,
                    color = Color.white,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = fontSize,
                    minHeight = minHeight,
                    paddingLeft = 14,
                    paddingRight = 14,
                    paddingTop = 8,
                    paddingBottom = 8,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.SecondaryButtonBorder,
                    borderRightColor = WalletUiTheme.SecondaryButtonBorder,
                    borderTopColor = WalletUiTheme.SecondaryButtonBorder,
                    borderBottomColor = WalletUiTheme.SecondaryButtonBorder
                }
            };
            ApplyDefaultFont(btn);
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            MakeButtonNonNavigable(btn);
            btn.clicked += () => onClick?.Invoke();
            return btn;
        }

        internal static void SetNavState(Button btn, bool isActive)
        {
            if (btn == null)
            {
                return;
            }

            btn.SetEnabled(!isActive);
            var border = isActive ? SoftOutlineWhite : WalletUiTheme.SecondaryButtonBorder;
            var borderWidth = isActive ? 2 : 1;
            btn.style.backgroundColor = WalletUiTheme.SecondaryButton;
            btn.style.color = Color.white;
            btn.style.borderLeftColor = border;
            btn.style.borderRightColor = border;
            btn.style.borderTopColor = border;
            btn.style.borderBottomColor = border;
            btn.style.borderLeftWidth = borderWidth;
            btn.style.borderRightWidth = borderWidth;
            btn.style.borderTopWidth = borderWidth;
            btn.style.borderBottomWidth = borderWidth;
        }

        // Centralized handler for modal-style Enter/Escape shortcuts to avoid duplicating hotkey wiring across dialogs.
        internal static void RegisterModalKeyHandlers(VisualElement panel, Action onPrimary, Action onSecondary = null, bool focusPanel = false)
        {
            if (panel == null || onPrimary == null)
            {
                return;
            }

            panel.focusable = true;
            panel.tabIndex = 0;
            panel.pickingMode = PickingMode.Position;

            if (focusPanel)
            {
                void FocusIfReady()
                {
                    if (panel.panel != null)
                    {
                        panel.Focus();
                    }
                }

                panel.schedule.Execute(FocusIfReady).StartingIn(30);
                panel.RegisterCallback<AttachToPanelEvent>(_ => panel.schedule.Execute(FocusIfReady).StartingIn(10));
            }

            EventCallback<KeyDownEvent> handler = null;
            handler = evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    onPrimary();
                    evt.StopImmediatePropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    onSecondary?.Invoke();
                    evt.StopImmediatePropagation();
                }
            };

            panel.RegisterCallback(handler, TrickleDown.TrickleDown);
        }

        /// <summary>
        /// Applies enabled/disabled state and keeps text color in sync so disabled actions are visually clear.
        /// </summary>
        internal static void SetButtonEnabledVisual(Button button, bool enabled, Color? enabledColor = null, Color? disabledColor = null)
        {
            if (button == null)
            {
                return;
            }

            button.SetEnabled(enabled);

            var active = enabledColor ?? (button.style.color.keyword != StyleKeyword.Null
                ? (Color?)button.style.color.value
                : WalletUiTheme.TextPrimary);
            var inactive = disabledColor ?? WalletUiTheme.TextMuted;

            if (enabled && active.HasValue)
            {
                button.style.color = active.Value;
            }
            else
            {
                button.style.color = inactive;
            }
        }

        internal static ScrollView CreateScrollView(Action<float> onScrollChanged = null, Func<bool> shouldBlockWheel = null, float paddingBottom = 0f)
        {
            // Manual wheel handling stays here to avoid UITK ScrollView.ReadSingleLineHeight nullrefs and to keep stable offsets.
            var scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = 0,
                    minHeight = 0,
                    backgroundColor = Color.clear,
                    overflow = Overflow.Hidden,
                    alignSelf = Align.Stretch
                }
            };
            ApplyDefaultFont(scroll);
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.contentContainer.style.flexDirection = FlexDirection.Column;
            scroll.contentContainer.style.alignItems = Align.Stretch;
            scroll.contentContainer.style.flexGrow = 0;
            scroll.contentContainer.style.flexShrink = 0;
            if (paddingBottom > 0f)
            {
                scroll.contentContainer.style.paddingBottom = paddingBottom;
            }

            if (scroll.verticalScroller != null)
            {
                scroll.verticalScroller.valueChanged += v => onScrollChanged?.Invoke(v);
            }

            scroll.RegisterCallback<WheelEvent>(evt =>
            {
                var focusController = scroll?.panel?.focusController;
                if (shouldBlockWheel != null && shouldBlockWheel())
                {
                    focusController?.IgnoreEvent(evt);
                    evt.StopImmediatePropagation();
                    return;
                }

                var scroller = scroll.verticalScroller;
                if (scroller == null || scroll.contentContainer == null)
                {
                    focusController?.IgnoreEvent(evt);
                    evt.StopImmediatePropagation();
                    return;
                }

                const float scrollStep = 120f;
                var delta = Mathf.Clamp(evt.delta.y, -1f, 1f);
                var low = scroller.lowValue;
                var high = scroller.highValue;
                if ((double)high <= (double)low)
                {
                    var viewportHeight = scroll.contentViewport?.worldBound.height ?? 0f;
                    var contentHeight = scroll.contentContainer.worldBound.height;
                    if (viewportHeight > 0f && contentHeight > viewportHeight)
                    {
                        high = contentHeight - viewportHeight;
                    }
                }

                if (high < low)
                {
                    high = low;
                }

                var target = Mathf.Clamp(scroller.value + delta * scrollStep, low, high);
                scroller.value = target;
                var offset = scroll.scrollOffset;
                offset.y = target;
                scroll.scrollOffset = offset;
                onScrollChanged?.Invoke(target);
                focusController?.IgnoreEvent(evt);
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);

            return scroll;
        }

        private static void MakeButtonNonNavigable(Button btn)
        {
            if (btn == null)
            {
                return;
            }

            // Disable keyboard focus to avoid hidden tab/enter activation; modal handlers manage Enter explicitly.
            btn.focusable = false;
            btn.tabIndex = -1;
            btn.pickingMode = PickingMode.Position;
        }

        // Blocks Tab navigation on a container to avoid hidden focus cycling that can trigger buttons via Enter.
        internal static EventCallback<KeyDownEvent> BlockTabNavigation(VisualElement element)
        {
            if (element == null)
            {
                return null;
            }

            EventCallback<KeyDownEvent> handler = evt =>
            {
                if (evt.keyCode == KeyCode.Tab)
                {
                    evt.StopImmediatePropagation();
                }
            };

            element.RegisterCallback(handler, TrickleDown.TrickleDown);
            return handler;
        }

        internal static void UnblockTabNavigation(VisualElement element, EventCallback<KeyDownEvent> handler)
        {
            if (element == null || handler == null)
            {
                return;
            }

            element.UnregisterCallback(handler, TrickleDown.TrickleDown);
        }

        // Logs scroll/geometry state for debugging scroll issues.
        internal static void LogScrollState(string reason, ScrollView scrollView, VisualElement root = null, VisualElement wrapper = null)
        {
            if (scrollView == null)
            {
                return;
            }

            try
            {
                var viewport = scrollView.contentViewport;
                var content = scrollView.contentContainer;
                var scroller = scrollView.verticalScroller;
                var sb = new StringBuilder();
                sb.Append("[UITK][Scroll] ").Append(reason)
                  .Append($" rootH={(root?.layout.height ?? 0f):F1}")
                  .Append($" wrapH={(wrapper?.layout.height ?? 0f):F1}")
                  .Append($" scrollH={(scrollView.layout.height):F1}")
                  .Append($" viewportH={(viewport?.layout.height ?? 0f):F1}")
                  .Append($" contentH={(content?.layout.height ?? 0f):F1}")
                  .Append($" children={content?.childCount ?? 0}")
                  .Append($" scroller=({(scroller?.value ?? 0f):F1}/{(scroller?.highValue ?? 0f):F1})")
                  .Append($" offset=({scrollView.scrollOffset.x:F1},{scrollView.scrollOffset.y:F1})");
                Log.Write(sb.ToString());
            }
            catch (Exception e)
            {
                Log.WriteWarning($"[UITK][Scroll] Failed to log '{reason}': {e.Message}");
            }
        }

        internal static void ApplyDefaultFont(VisualElement element)
        {
            var font = WalletUiTheme.DefaultFont;
            if (element == null || font == null)
            {
                return;
            }

            element.style.unityFont = font;
            element.style.unityFontDefinition = FontDefinition.FromFont(font);
        }

        internal static void ApplyNetworkBadge(Label label, string nexusName, NexusKind kind)
        {
            if (label == null)
            {
                return;
            }

            var text = BuildNetworkLabel(kind);
            if (string.IsNullOrWhiteSpace(text))
            {
                // Keep layout stable even when the badge is hidden to avoid subtitle jitter on first refresh.
                label.text = string.Empty;
                label.style.visibility = Visibility.Hidden;
                label.style.display = DisplayStyle.Flex;
                return;
            }

            var palette = GetNetworkBadgePalette(kind);
            label.text = text;
            label.style.display = DisplayStyle.Flex;
            label.style.visibility = Visibility.Visible;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 12;
            label.style.color = palette.text;
            label.style.backgroundColor = palette.background;
            label.style.borderLeftWidth = 1;
            label.style.borderRightWidth = 1;
            label.style.borderTopWidth = 1;
            label.style.borderBottomWidth = 1;
            label.style.borderLeftColor = palette.border;
            label.style.borderRightColor = palette.border;
            label.style.borderTopColor = palette.border;
            label.style.borderBottomColor = palette.border;
            label.style.borderTopLeftRadius = WalletUiTheme.RadiusSmall;
            label.style.borderTopRightRadius = WalletUiTheme.RadiusSmall;
            label.style.borderBottomLeftRadius = WalletUiTheme.RadiusSmall;
            label.style.borderBottomRightRadius = WalletUiTheme.RadiusSmall;
            label.style.paddingLeft = 10;
            label.style.paddingRight = 10;
            label.style.paddingTop = 4;
            label.style.paddingBottom = 4;
            label.style.marginLeft = 6;
            label.style.minHeight = 20;
            label.style.minWidth = 64; // Reserve width to avoid layout shifts when the badge text appears.
        }

        /// <summary>
        /// Builds the unified Soul Master badge so both the balances list and token dashboard stay visually aligned.
        /// </summary>
        internal static Label CreateSoulMasterBadge(float fontSize = 16f, float paddingHorizontal = 10f, float paddingVertical = 4f)
        {
            var label = new Label("★ SM ★")
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = fontSize,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    paddingLeft = paddingHorizontal,
                    paddingRight = paddingHorizontal,
                    paddingTop = paddingVertical,
                    paddingBottom = paddingVertical,
                    backgroundColor = Color.clear,
                    flexShrink = 0
                }
            };
            ApplyDefaultFont(label);
            label.style.minHeight = fontSize + paddingVertical * 2f;
            label.style.alignSelf = Align.Center;
            return label;
        }

        internal static bool IsSoulMaster(AccountManager accountManager)
        {
            if (accountManager == null || accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                return false;
            }

            var state = accountManager.CurrentState;
            return state != null && state.flags.HasFlag(AccountFlags.Master);
        }

        internal static string BuildVersionLabel()
        {
            return Application.version;
        }

        // Applies a full-size background image without tiling to replace deprecated unityBackgroundScaleMode usage.
        internal static void ApplyBackgroundFill(VisualElement element, StyleBackground background)
        {
            if (element == null)
            {
                return;
            }

            element.style.backgroundImage = background;
            element.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            element.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            element.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            element.style.backgroundSize = new BackgroundSize(new Length(100, LengthUnit.Percent), new Length(100, LengthUnit.Percent));
        }

        // Shared card styling (background + borders) to avoid duplicating radius/border settings across screens.
        internal static void ApplyCardStyle(
            VisualElement element,
            Texture2D backgroundTexture,
            float radius = -1f,
            Color? backgroundColor = null,
            Color? borderColor = null,
            Color? topBorderColor = null,
            float borderWidth = 1f)
        {
            if (element == null)
            {
                return;
            }

            var radiusValue = radius < 0f ? WalletUiTheme.RadiusMedium : radius;
            element.style.backgroundColor = backgroundColor ?? WalletUiTheme.CardBackground;
            if (backgroundTexture != null)
            {
                ApplyBackgroundFill(element, new StyleBackground(backgroundTexture));
            }

            element.style.borderTopLeftRadius = radiusValue;
            element.style.borderTopRightRadius = radiusValue;
            element.style.borderBottomLeftRadius = radiusValue;
            element.style.borderBottomRightRadius = radiusValue;

            var sideColor = borderColor ?? WalletUiTheme.CardBorder;
            var topColor = topBorderColor ?? WalletUiTheme.HighlightEdge;

            element.style.borderLeftColor = sideColor;
            element.style.borderRightColor = sideColor;
            element.style.borderBottomColor = sideColor;
            element.style.borderTopColor = topColor;

            element.style.borderLeftWidth = borderWidth;
            element.style.borderRightWidth = borderWidth;
            element.style.borderTopWidth = borderWidth;
            element.style.borderBottomWidth = borderWidth;
        }

        internal static string BuildNetworkLabel(NexusKind kind)
        {
            switch (kind)
            {
                case NexusKind.Test_Net:
                    return "TESTNET";
                case NexusKind.Dev_Net:
                    return "DEVNET";
                case NexusKind.Local_Net:
                    return "LOCALNET";
                case NexusKind.Custom:
                    return "CUSTOM";
                default:
                    return string.Empty;
            }
        }

        private static (Color background, Color border, Color text) GetNetworkBadgePalette(NexusKind kind)
        {
            Color baseColor;
            switch (kind)
            {
                case NexusKind.Test_Net:
                    baseColor = WalletUiTheme.BadgeTestnet;
                    break;
                case NexusKind.Dev_Net:
                    baseColor = WalletUiTheme.BadgeDevnet;
                    break;
                case NexusKind.Local_Net:
                    baseColor = WalletUiTheme.BadgeLocalnet;
                    break;
                case NexusKind.Custom:
                    baseColor = WalletUiTheme.BadgeCustom;
                    break;
                default:
                    baseColor = WalletUiTheme.AccentPrimary;
                    break;
            }

            var border = Color.Lerp(baseColor, Color.white, 0.2f);
            var background = Color.Lerp(baseColor, Color.black, 0.1f);
            return (background, border, Color.white);
        }

        internal static string BuildContextSubtitle(string label, string accountName, object platform)
        {
            var name = string.IsNullOrWhiteSpace(accountName) ? "Wallet" : accountName;
            var platformText = platform?.ToString() ?? string.Empty;
            return string.IsNullOrWhiteSpace(platformText)
                ? $"{label} for {name}"
                : $"{label} for {name} @ {platformText}";
        }

        internal static VisualElement CreateModalOverlay()
        {
            var overlay = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    right = 0,
                    top = 0,
                    bottom = 0,
                    backgroundColor = WalletUiTheme.Overlay,
                    justifyContent = Justify.Center,
                    alignItems = Align.Center,
                    display = DisplayStyle.None
                }
            };
            ApplyDefaultFont(overlay);
            overlay.pickingMode = PickingMode.Position;
            return overlay;
        }

        internal static VisualElement CreateModalPanel(float width = 720f, float maxWidth = 900f)
        {
            var panel = new VisualElement
            {
                style =
                {
                    width = width,
                    maxWidth = maxWidth,
                    paddingLeft = 22,
                    paddingRight = 22,
                    paddingTop = 18,
                    paddingBottom = 18,
                    borderTopLeftRadius = WalletUiTheme.RadiusLarge,
                    borderTopRightRadius = WalletUiTheme.RadiusLarge,
                    borderBottomLeftRadius = WalletUiTheme.RadiusLarge,
                    borderBottomRightRadius = WalletUiTheme.RadiusLarge,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.CardBorder,
                    borderRightColor = WalletUiTheme.CardBorder,
                    borderTopColor = WalletUiTheme.HighlightEdge,
                    borderBottomColor = WalletUiTheme.CardBorder,
                    flexDirection = FlexDirection.Column,
                    alignItems = Align.Stretch
                }
            };
            ApplyDefaultFont(panel);
            ApplyCardStyle(panel, WalletUiTheme.GetPanelGradientTexture(), WalletUiTheme.RadiusLarge, WalletUiTheme.PanelBackground, WalletUiTheme.CardBorder, WalletUiTheme.HighlightEdge);
            return panel;
        }

        internal static void StyleModalInput(TextField field, bool multiline = false, int minHeight = 40)
        {
            if (field == null)
            {
                return;
            }

            var s = field.style;
            s.fontSize = 14;
            s.unityTextAlign = TextAnchor.UpperLeft;
            s.marginBottom = 12;
            s.minHeight = minHeight;
            s.paddingLeft = 10;
            s.paddingRight = 10;
            s.paddingTop = 10;
            s.paddingBottom = 10;
            s.backgroundColor = WalletUiTheme.InputBackground;
            s.color = WalletUiTheme.TextPrimary;
            s.borderLeftWidth = 1;
            s.borderRightWidth = 1;
            s.borderTopWidth = 1;
            s.borderBottomWidth = 1;
            s.borderLeftColor = WalletUiTheme.InputBorder;
            s.borderRightColor = WalletUiTheme.InputBorder;
            s.borderTopColor = WalletUiTheme.HighlightEdge;
            s.borderBottomColor = WalletUiTheme.InputBorder;
            s.borderTopLeftRadius = WalletUiTheme.RadiusSmall;
            s.borderTopRightRadius = WalletUiTheme.RadiusSmall;
            s.borderBottomLeftRadius = WalletUiTheme.RadiusSmall;
            s.borderBottomRightRadius = WalletUiTheme.RadiusSmall;
            field.multiline = multiline;
            ApplyDefaultFont(field);
        }

        internal static Color GetNetworkColor(NexusKind kind)
        {
            Color c = WalletUiTheme.AccentPrimarySoft;
            switch (kind)
            {
                case NexusKind.Test_Net:
                    ColorUtility.TryParseHtmlString("#FF8A00", out c);
                    break;
                case NexusKind.Dev_Net:
                    ColorUtility.TryParseHtmlString("#FFD247", out c);
                    break;
                case NexusKind.Local_Net:
                    ColorUtility.TryParseHtmlString("#4CAF50", out c);
                    break;
                case NexusKind.Custom:
                    ColorUtility.TryParseHtmlString("#FF6F6F", out c);
                    break;
            }

            return c;
        }
    }

    /// <summary>
    /// Flexible spacer for horizontal rows to push trailing content without repeating raw VisualElement setup.
    /// </summary>
    internal sealed class HSpacer : VisualElement
    {
        internal HSpacer()
        {
            style.flexGrow = 1;
            style.flexShrink = 1;
            style.flexBasis = 0;
            style.minHeight = 0;
            style.alignSelf = Align.Stretch;
        }
    }

    /// <summary>
    /// Flexible spacer for vertical column layouts to occupy remaining space cleanly.
    /// </summary>
    internal sealed class VSpacer : VisualElement
    {
        internal VSpacer()
        {
            style.flexGrow = 1;
            style.minHeight = 0;
        }
    }

    internal sealed class HeaderElements
    {
        internal HeaderElements(VisualElement root)
        {
            Root = root;
        }

        internal VisualElement Root { get; }
    }

    internal sealed class SubHeaderElements
    {
        internal SubHeaderElements(VisualElement root, Label leftLabel, Label subtitleLabel, Label networkLabel)
        {
            Root = root;
            LeftLabel = leftLabel;
            SubtitleLabel = subtitleLabel;
            NetworkLabel = networkLabel;
        }

        internal VisualElement Root { get; }
        internal Label LeftLabel { get; }
        internal Label SubtitleLabel { get; }
        internal Label NetworkLabel { get; }
    }

    internal sealed class AccountInfoElements
    {
        internal AccountInfoElements(VisualElement root, Label accountLabel, Label addressLabel, Label networkLabel)
        {
            Root = root;
            AccountLabel = accountLabel;
            AddressLabel = addressLabel;
            NetworkLabel = networkLabel;
        }

        internal VisualElement Root { get; }
        internal Label AccountLabel { get; }
        internal Label AddressLabel { get; }
        internal Label NetworkLabel { get; }
    }

    internal sealed class HeaderBlockElements
    {
        internal HeaderBlockElements(VisualElement root, HeaderElements header, SubHeaderElements subHeader)
        {
            Root = root;
            Header = header;
            SubHeader = subHeader;
        }

        internal VisualElement Root { get; }
        internal HeaderElements Header { get; }
        internal SubHeaderElements SubHeader { get; }
    }
}
