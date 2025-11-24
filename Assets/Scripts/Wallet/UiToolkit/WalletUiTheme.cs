using UnityEngine;

namespace Poltergeist.UiToolkit
{
    /// <summary>
    /// Centralized palette and sizing values for the new UITK wallet UI.
    /// Mirrors the legacy IMGUI look so screens stay consistent while we migrate.
    /// </summary>
    internal static class WalletUiTheme
    {
        private static Font cachedFont;

        public static Font DefaultFont => cachedFont ??= Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Base surfaces
        public static readonly Color ScreenBackground = Hex("#0b0c12");
        public static readonly Color HeaderBackground = Hex("#34374b");
        public static readonly Color HeaderBorder = Hex("#4a4d61");
        public static readonly Color PanelBackground = Hex("#1a1c24");
        public static readonly Color CardBackground = Hex("#2b2e3d");
        public static readonly Color CardBorder = Hex("#3b3f50");
        public static readonly Color Divider = Hex("#2d3040");

        // Text
        public static readonly Color TextPrimary = Color.white;
        public static readonly Color TextSecondary = Hex("#c7ccd6");
        public static readonly Color TextMuted = Hex("#9ba1ae");

        // Buttons
        public static readonly Color ActionButton = Hex("#c8d9f1");
        public static readonly Color ActionButtonText = Hex("#101520");
        public static readonly Color ActionButtonBorder = Hex("#e2edff");
        public static readonly Color SecondaryButton = Hex("#2a2d3c");
        public static readonly Color SecondaryButtonBorder = Hex("#3b3f50");

        // Overlays / modal
        public static readonly Color Overlay = new Color(0f, 0f, 0f, 0.55f);
        public static readonly Color ModalBackground = Hex("#262938");
        public static readonly Color ModalBorder = Hex("#3c4052");
        public static readonly Color InputBackground = Hex("#1c1f2c");
        public static readonly Color InputBorder = Hex("#42475c");

        // Radii / spacing
        public const int RadiusLarge = 10;
        public const int RadiusMedium = 8;
        public const int RadiusSmall = 4;

        public static Color Hex(string value)
        {
            if (ColorUtility.TryParseHtmlString(value, out var color))
            {
                return color;
            }

            return Color.white;
        }
    }
}
