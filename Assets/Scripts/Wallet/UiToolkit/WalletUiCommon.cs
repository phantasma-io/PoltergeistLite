using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poltergeist;
using Poltergeist.Wallet;

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

        internal static VisualElement BuildNavBar(out Button balances, out Button history, out Button account, out Button exit, Action onBalances, Action onHistory, Action onAccount, Action onExit)
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
            exit = CreateNavButton("Exit", onExit);

            var buttons = new[] { balances, history, account, exit };
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
            var source = string.IsNullOrWhiteSpace(name) ? kind.ToString() : name;
            source = source.Replace("_", string.Empty).Replace(" ", string.Empty);
            return $"[{source.ToUpperInvariant()}]";
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
