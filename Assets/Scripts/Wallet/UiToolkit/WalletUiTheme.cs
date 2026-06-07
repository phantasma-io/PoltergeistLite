using UnityEngine;

namespace Poltergeist.UiToolkit
{
    /// <summary>
    /// Centralized palette and sizing values for the new UITK wallet UI.
    /// Centralized visual tokens for runtime UI Toolkit screens.
    /// </summary>
    internal static class WalletUiTheme
    {
        private static Font cachedFont;
        private static Texture2D cachedScreenGradient;
        private static Texture2D cachedPanelGradient;
        private static Texture2D cachedCardGradient;

        public static Font DefaultFont => cachedFont ??= Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Base surfaces
        // Keep hues (purple top, carrot bottom) but bias distribution toward purple.
        // Current softened hues v3 (purple -> warm): if too dull, restore previous sets below.
        public static readonly Color ScreenBackgroundTop = Hex("#3e2c5c");
        public static readonly Color ScreenBackgroundBottom = Hex("#a65528");
        // Baselines for quick rollback:
        // - v2 purple: #443066
        // - v2 orange: #ba612b
        // - v1 purple: #4c2f7a
        // - v1 orange: #c96a2f
        // - Original bright orange: #d86d30
        public static readonly Color ScreenBackground = new Color(ScreenBackgroundTop.r, ScreenBackgroundTop.g, ScreenBackgroundTop.b, 1f);
        public static readonly Color ScreenGlass = new Color(1f, 1f, 1f, 0.06f);
        public static readonly Color ScreenGlassStrong = new Color(1f, 1f, 1f, 0.1f);
        public static readonly Color HeaderBackground = Color.clear;
        public static readonly Color HeaderBorder = Color.clear;
        public static readonly Color PanelBackground = Hex("#232b57");
        public static readonly Color CardBackground = Hex("#1e2f63");
        public static readonly Color CardBorder = Hex("#2c3f78");
        public static readonly Color Divider = Hex("#304274");
        public static readonly Color HighlightEdge = Hex("#3c4e85");

        // Network badges
        public static readonly Color BadgeTestnet = Hex("#f39c4a");
        public static readonly Color BadgeDevnet = Hex("#b175f6");
        public static readonly Color BadgeLocalnet = Hex("#4caf6d"); // pleasant green
        public static readonly Color BadgeCustom = Hex("#c84a4a");   // darker alerting red

        // Accents (match the reference screenshot)
        public static readonly Color AccentPrimary = Hex("#6baee6");
        public static readonly Color AccentPrimarySoft = Hex("#8ac6f5");

        // Text
        public static readonly Color TextPrimary = Color.white;
        public static readonly Color TextSecondary = Hex("#d7ddf2");
        public static readonly Color TextMuted = Hex("#aab4d4");

        // Buttons
        public static readonly Color ActionButton = AccentPrimary;
        public static readonly Color ActionButtonText = Color.white;
        public static readonly Color ActionButtonBorder = AccentPrimarySoft;
        public static readonly Color SecondaryButton = Hex("#1e2b4a");
        public static readonly Color SecondaryButtonBorder = Hex("#26365a");

        // Overlays / modal
        public static readonly Color Overlay = new Color(0f, 0f, 0f, 0.55f);
        public static readonly Color ModalBackground = Hex("#1b202d");
        public static readonly Color ModalBorder = Hex("#2f3545");
        public static readonly Color InputBackground = Hex("#161c27");
        public static readonly Color InputBorder = Hex("#323a4c");

        // Radii / spacing
        public const int RadiusLarge = 12;
        public const int RadiusMedium = 12;
        public const int RadiusSmall = 8;

        public static Texture2D GetScreenGradientTexture()
        {
            if (cachedScreenGradient != null)
            {
                return cachedScreenGradient;
            }

            // 4px vertical gradient: ~75% purple, ~25% warm orange.
            cachedScreenGradient = new Texture2D(1, 4, TextureFormat.RGBA32, false)
            {
                name = "WalletUiScreenGradient"
            };
            cachedScreenGradient.SetPixel(0, 0, ScreenBackgroundTop);
            cachedScreenGradient.SetPixel(0, 1, ScreenBackgroundTop);
            cachedScreenGradient.SetPixel(0, 2, Color.Lerp(ScreenBackgroundTop, ScreenBackgroundBottom, 0.35f));
            cachedScreenGradient.SetPixel(0, 3, ScreenBackgroundBottom);
            cachedScreenGradient.wrapMode = TextureWrapMode.Clamp;
            cachedScreenGradient.Apply();
            return cachedScreenGradient;
        }

        public static Texture2D GetPanelGradientTexture()
        {
            if (cachedPanelGradient != null)
            {
                return cachedPanelGradient;
            }

            cachedPanelGradient = new Texture2D(1, 2, TextureFormat.RGBA32, false)
            {
                name = "WalletUiPanelGradient"
            };
            cachedPanelGradient.SetPixel(0, 0, PanelBackground);
            cachedPanelGradient.SetPixel(0, 1, new Color(PanelBackground.r * 0.9f, PanelBackground.g * 0.9f, PanelBackground.b * 0.9f, PanelBackground.a));
            cachedPanelGradient.wrapMode = TextureWrapMode.Clamp;
            cachedPanelGradient.Apply();
            return cachedPanelGradient;
        }

        public static Texture2D GetCardGradientTexture()
        {
            if (cachedCardGradient != null)
            {
                return cachedCardGradient;
            }

            cachedCardGradient = new Texture2D(1, 2, TextureFormat.RGBA32, false)
            {
                name = "WalletUiCardGradient"
            };
            cachedCardGradient.SetPixel(0, 0, CardBackground);
            cachedCardGradient.SetPixel(0, 1, new Color(CardBackground.r * 0.88f, CardBackground.g * 0.88f, CardBackground.b * 0.88f, CardBackground.a));
            cachedCardGradient.wrapMode = TextureWrapMode.Clamp;
            cachedCardGradient.Apply();
            return cachedCardGradient;
        }

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
