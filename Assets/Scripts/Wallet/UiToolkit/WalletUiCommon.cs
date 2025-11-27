using System;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poltergeist;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Unity.Core.Logging;

namespace Poltergeist.UiToolkit
{
    /// <summary>
    /// Shared UI building blocks for the UITK wallet screens.
    /// Keeps styling consistent with the legacy IMGUI look while we migrate.
    /// </summary>
    internal static class WalletUiCommon
    {
        private const string AppTitle = "Poltergeist Lite";
        private static readonly Color SoftOutlineWhite = WalletUiTheme.Hex("#dfe3f0");

        internal static HeaderElements BuildHeader(string subtitleText, VisualElement rightContent = null, bool showSubtitle = false)
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
                    unityBackgroundScaleMode = ScaleMode.StretchToFill,
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

            var subtitle = new Label(subtitleText ?? string.Empty);
            ApplyDefaultFont(subtitle);

            var network = new Label(string.Empty);
            ApplyDefaultFont(network);

            header.Add(titleRow);

            if (rightContent != null)
            {
                rightContent.style.position = Position.Absolute;
                rightContent.style.right = 16;
                rightContent.style.top = new Length(50, LengthUnit.Percent);
                rightContent.style.translate = new Translate(new Length(0, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
                header.Add(rightContent);
            }

            return new HeaderElements(header, subtitle, network);
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

            var subtitleGroup = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    position = Position.Absolute,
                    left = 0,
                    right = 0
                }
            };

            var subtitle = new Label(subtitleText ?? string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 18,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleCenter
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

            row.Add(leftLabel);
            row.Add(subtitleGroup);

            return new SubHeaderElements(row, leftLabel, subtitle, network);
        }

        // Shared header + subheader block so all screens stay consistent; callers can tweak margins for edge cases.
        internal static HeaderBlockElements BuildHeaderBlock(string headerSubtitle, string subHeaderSubtitle, string subHeaderLeft = "", VisualElement rightContent = null, bool showHeaderSubtitle = false, float headerMarginBottom = 12f, float subHeaderMarginTop = 8f, float subHeaderMarginBottom = 10f)
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

            var header = BuildHeader(headerSubtitle, rightContent, showHeaderSubtitle);
            header.Root.style.marginBottom = headerMarginBottom;
            header.Root.style.width = new Length(100, LengthUnit.Percent);
            header.Root.style.alignSelf = Align.Stretch;
            container.Add(header.Root);

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

        // Builds bottom nav bar with common actions; settings is optional (pass null action to hide).
        internal static VisualElement BuildNavBar(out Button balances, out Button history, out Button account, out Button settings, out Button exit, Action onBalances, Action onHistory, Action onAccount, Action onSettings, Action onExit)
        {
            var bar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.SpaceBetween,
                    alignItems = Align.Center,
                    paddingTop = 12,
                    paddingBottom = 12,
                    paddingLeft = 10,
                    paddingRight = 10,
                    marginTop = 10,
                    minHeight = 68,
                    backgroundColor = Color.clear,
                    backgroundImage = new StyleBackground(),
                    unityBackgroundScaleMode = ScaleMode.StretchToFill,
                    borderTopWidth = 0,
                    borderBottomWidth = 0,
                    borderLeftWidth = 0,
                    borderRightWidth = 0,
                    borderTopColor = WalletUiTheme.HeaderBorder,
                    borderBottomColor = WalletUiTheme.HeaderBorder,
                    borderLeftColor = WalletUiTheme.HeaderBorder,
                    borderRightColor = WalletUiTheme.HeaderBorder,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium
                }
            };
            ApplyDefaultFont(bar);

            balances = CreateNavButton("Balances", onBalances);
            history = CreateNavButton("History", onHistory);
            account = CreateNavButton("Account", onAccount);
            settings = onSettings != null ? CreateNavButton("Settings", onSettings) : null;
            exit = CreateNavButton("Exit", onExit);

            var buttons = settings != null
                ? new[] { balances, history, account, settings, exit }
                : new[] { balances, history, account, exit };
            for (var i = 0; i < buttons.Length; i++)
            {
                var btn = buttons[i];
                btn.style.flexGrow = 1;
                if (i > 0)
                {
                    btn.style.marginLeft = 8;
                }
                bar.Add(btn);
            }

            return bar;
        }

        // Wallet mode tabs: balances/history/account/exit. Centralized to avoid diverging button sets per screen.
        internal static VisualElement BuildWalletNavBar(out Button balances, out Button history, out Button account, out Button exit, Action onBalances, Action onHistory, Action onAccount, Action onExit)
        {
            return BuildNavBar(out balances, out history, out account, out _, out exit, onBalances, onHistory, onAccount, null, onExit);
        }

        // Main screen footer (wallet list, settings pre-login).
        // Generic footer builder with dark filled buttons (shared across main and settings screens).
        internal static VisualElement BuildFooter(params (string text, Action onClick)[] entries)
        {
            var bar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.SpaceBetween,
                    alignItems = Align.Center,
                    paddingTop = 14,
                    paddingBottom = 14,
                    paddingLeft = 12,
                    paddingRight = 12,
                    marginTop = 14,
                    marginBottom = 6,
                    minHeight = 72,
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

            for (var i = 0; i < entries.Length; i++)
            {
                var (text, onClick) = entries[i];
                var btn = CreateMainFooterButton(text, onClick);
                btn.style.flexGrow = 1;
                btn.style.marginLeft = i == 0 ? 0 : 8;
                bar.Add(btn);
            }

            return bar;
        }

        internal static VisualElement BuildMainFooter(Action onNewWallet, Action onImportWallet, Action onManageWallets, Action onSettings)
        {
            return BuildFooter(
                ("New wallet", onNewWallet),
                ("Import", onImportWallet),
                ("Manage", onManageWallets),
                ("Settings", onSettings)
            );
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

        // Standardized scroll section for list screens (balances/history/accounts) to keep layout and padding consistent.
        internal static VisualElement BuildListSection(
            out ScrollView listView,
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
            listView = CreateScrollView(onScrollChanged, shouldBlockWheel, paddingBottom);
            listView.style.backgroundImage = new StyleBackground();
            listView.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
            listView.style.borderTopLeftRadius = 0;
            listView.style.borderTopRightRadius = 0;
            listView.style.borderBottomLeftRadius = 0;
            listView.style.borderBottomRightRadius = 0;
            listView.style.borderLeftWidth = 0;
            listView.style.borderRightWidth = 0;
            listView.style.borderTopWidth = 0;
            listView.style.borderBottomWidth = 0;
            listView.style.paddingLeft = paddingLeft;
            listView.style.paddingRight = paddingRight;
            listView.style.paddingTop = paddingTop;
            listView.style.paddingBottom = paddingBottom;
            listView.style.marginTop = marginTop;
            listView.style.marginBottom = marginBottom;
            listView.style.alignSelf = alignSelf;
            listView.style.width = new Length(100, LengthUnit.Percent);
            if (maxWidth > 0f)
            {
                listView.style.maxWidth = maxWidth;
            }

            ApplyDefaultFont(listView);

            var wrapper = CreateScrollWrapper();
            wrapper.Add(listView);
            return wrapper;
        }

        // Scroll container for form-like screens (Settings) with optional margin; keeps wrapper/scroll styling consistent.
        internal static VisualElement BuildScrollContainer(
            out ScrollView scrollView,
            Action<float> onScrollChanged,
            Func<bool> shouldBlockWheel = null,
            float paddingBottom = 0f,
            float marginTop = 0f)
        {
            scrollView = CreateScrollView(onScrollChanged, shouldBlockWheel, paddingBottom);
            if (marginTop > 0f)
            {
                scrollView.style.marginTop = marginTop;
            }

            var wrapper = CreateScrollWrapper();
            wrapper.Add(scrollView);
            return wrapper;
        }

        internal static Button CreateNavButton(string text, Action onClick)
        {
            var btn = new Button
            {
                text = text,
                style =
                {
                    backgroundColor = WalletUiTheme.SecondaryButton,
                    color = Color.white,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 18,
                    minHeight = 52,
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
                    borderLeftColor = WalletUiTheme.SecondaryButtonBorder,
                    borderRightColor = WalletUiTheme.SecondaryButtonBorder,
                    borderTopColor = WalletUiTheme.SecondaryButtonBorder,
                    borderBottomColor = WalletUiTheme.SecondaryButtonBorder
                }
            };
            ApplyDefaultFont(btn);
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.clicked += () => onClick?.Invoke();
            return btn;
        }

        internal static Button CreateMainFooterButton(string text, Action onClick)
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
                    borderBottomColor = WalletUiTheme.SecondaryButtonBorder
                }
            };
            ApplyDefaultFont(btn);
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
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
                if (shouldBlockWheel != null && shouldBlockWheel())
                {
                    evt.StopImmediatePropagation();
                    evt.PreventDefault();
                    return;
                }

                var scroller = scroll.verticalScroller;
                if (scroller == null || scroll.contentContainer == null)
                {
                    evt.StopImmediatePropagation();
                    evt.PreventDefault();
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
                evt.StopImmediatePropagation();
                evt.PreventDefault();
            }, TrickleDown.TrickleDown);

            return scroll;
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

            label.text = BuildNetworkLabel(nexusName, kind);
            label.style.color = GetNetworkColor(kind);
        }

        internal static string BuildVersionLabel()
        {
            return Application.version;
        }

        internal static string BuildNetworkLabel(string name, NexusKind kind)
        {
            switch (kind)
            {
                case NexusKind.Test_Net:
                    return "[TESTNET]";
                case NexusKind.Dev_Net:
                    return "[DEVNET]";
                case NexusKind.Local_Net:
                    return "[LOCALNET]";
                case NexusKind.Custom:
                    {
                        var source = string.IsNullOrWhiteSpace(name) ? "CUSTOM" : name;
                        source = source.Replace("_", string.Empty).Replace(" ", string.Empty);
                        return $"[{source.ToUpperInvariant()}]";
                    }
                default:
                    return string.Empty;
            }
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
                    backgroundColor = WalletUiTheme.PanelBackground,
                    backgroundImage = new StyleBackground(WalletUiTheme.GetPanelGradientTexture()),
                    unityBackgroundScaleMode = ScaleMode.StretchToFill,
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

    internal sealed class HeaderElements
    {
        internal HeaderElements(VisualElement root, Label subtitleLabel, Label networkLabel)
        {
            Root = root;
            SubtitleLabel = subtitleLabel;
            NetworkLabel = networkLabel;
        }

        internal VisualElement Root { get; }
        internal Label SubtitleLabel { get; }
        internal Label NetworkLabel { get; }
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
