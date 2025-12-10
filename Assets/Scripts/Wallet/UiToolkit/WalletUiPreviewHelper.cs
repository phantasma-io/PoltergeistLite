using UnityEngine;
using UnityEngine.UIElements;

namespace Poltergeist.UiToolkit
{
    internal sealed class WalletUiPreviewHelper
    {
        private const float DefaultMatchValue = 0.5f;

        internal readonly struct PreviewDeviceProfile
        {
            public PreviewDeviceProfile(UiPreviewDevice device, int widthPx, int heightPx, float density)
            {
                Device = device;
                WidthPx = widthPx;
                HeightPx = heightPx;
                Density = density;
            }

            public UiPreviewDevice Device { get; }
            public int WidthPx { get; }
            public int HeightPx { get; }
            public float Density { get; }

            public int LogicalWidth => Mathf.Max(1, Mathf.RoundToInt(WidthPx / Mathf.Max(0.1f, Density)));
            public int LogicalHeight => Mathf.Max(1, Mathf.RoundToInt(HeightPx / Mathf.Max(0.1f, Density)));
        }

        // Play Store tablet screenshots must be 16:9 or 9:16; keep tablet profiles in 9:16 and above platform minima.
        private static readonly PreviewDeviceProfile[] PreviewDevices =
        {
            new PreviewDeviceProfile(UiPreviewDevice.Pixel_6, 1080, 2400, 3.0f),
            new PreviewDeviceProfile(UiPreviewDevice.IPhone_13, 1170, 2532, 3.0f),
            new PreviewDeviceProfile(UiPreviewDevice.Galaxy_S20, 1440, 3200, 3.0f),
            new PreviewDeviceProfile(UiPreviewDevice.IPad_Mini, 1488, 2266, 2.0f),
            new PreviewDeviceProfile(UiPreviewDevice.Tablet_7_Inch, 1080, 1920, 2.0f),
            new PreviewDeviceProfile(UiPreviewDevice.Tablet_10_Inch, 1440, 2560, 2.0f)
        };

        private Vector2Int? lastPreviewResolution;
        private Vector2Int? previousDesktopResolution;
        private FullScreenMode? previousFullscreenMode;

        public void ApplyPanelScale(PanelSettings target, global::Poltergeist.Settings settings, RuntimePlatform platform)
        {
            if (target == null)
            {
                return;
            }

            var hasPreviewProfile = TryGetPreviewProfile(settings, out var previewProfile);
            var previewActive = hasPreviewProfile && !IsMobilePlatform(platform);
            var baseReference = IsMobilePlatform(platform)
                ? new Vector2Int(1024, 576)
                // In preview mode keep the mobile baseline so scaling matches phones; only viewport size changes.
                : (previewActive ? new Vector2Int(1024, 576) : new Vector2Int(1920, 1080));
            var multiplier = settings?.uiScaleMultiplier ?? 1f;
            if (multiplier <= 0f)
            {
                multiplier = 1f;
            }

            var referenceResolution = ComputeScaledReference(baseReference, multiplier);
            target.referenceResolution = referenceResolution;
            target.match = previewActive && hasPreviewProfile
                ? ComputePreviewMatch(previewProfile, referenceResolution)
                : DefaultMatchValue;
        }

        public void ApplyPreviewViewport(UIDocument document, global::Poltergeist.Settings settings, RuntimePlatform platform)
        {
            var rootElement = document?.rootVisualElement;
            if (rootElement == null)
            {
                return;
            }

            var previewActive = TryGetPreviewProfile(settings, out var profile) && !IsMobilePlatform(platform);
            if (!previewActive)
            {
                RestorePreviewWindowSize();
                rootElement.style.width = new Length(100, LengthUnit.Percent);
                rootElement.style.maxWidth = StyleKeyword.Null;
                rootElement.style.height = new Length(100, LengthUnit.Percent);
                rootElement.style.maxHeight = StyleKeyword.Null;
                return;
            }

            ApplyPreviewWindowSize(profile, platform);
            var logicalWidth = profile.LogicalWidth;
            var logicalHeight = profile.LogicalHeight;

            rootElement.style.width = logicalWidth;
            rootElement.style.maxWidth = logicalWidth;
            rootElement.style.height = logicalHeight;
            rootElement.style.maxHeight = logicalHeight;
            rootElement.style.alignSelf = Align.Center;
        }

        public int GetMinSideRequirement(global::Poltergeist.Settings settings)
        {
            if (settings == null || !TryGetPreviewProfile(settings, out var profile))
            {
                return 0;
            }

            switch (profile.Device)
            {
                case UiPreviewDevice.Tablet_10_Inch:
                    return 1080;
                case UiPreviewDevice.Tablet_7_Inch:
                    return 320;
                default:
                    return 0;
            }
        }

        public float GetPreviewAspect(global::Poltergeist.Settings settings)
        {
            if (settings == null || !TryGetPreviewProfile(settings, out var profile) || profile.HeightPx <= 0)
            {
                return 0f;
            }

            return profile.WidthPx / (float)profile.HeightPx;
        }

        public bool TryGetPreviewProfile(global::Poltergeist.Settings settings, out PreviewDeviceProfile profile)
        {
            profile = default;
            if (settings == null || settings.uiPreviewDevice == UiPreviewDevice.Auto)
            {
                return false;
            }

            for (int i = 0; i < PreviewDevices.Length; i++)
            {
                if (PreviewDevices[i].Device == settings.uiPreviewDevice)
                {
                    profile = PreviewDevices[i];
                    return true;
                }
            }

            return false;
        }

        public void RestorePreviewWindowSize()
        {
            lastPreviewResolution = null;
            if (!previousDesktopResolution.HasValue)
            {
                return;
            }

            var resolution = previousDesktopResolution.Value;
            var fullscreen = previousFullscreenMode ?? Screen.fullScreenMode;
            previousDesktopResolution = null;
            previousFullscreenMode = null;
            Screen.SetResolution(resolution.x, resolution.y, fullscreen);
        }

        private void ApplyPreviewWindowSize(PreviewDeviceProfile profile, RuntimePlatform platform)
        {
            if (IsMobilePlatform(platform))
            {
                return;
            }

            var available = GetAvailableDesktopArea();
            // Fit the preview window into the current desktop display while keeping the device aspect ratio.
            var maxWidth = Mathf.Max(320, available.x - 40);
            var maxHeight = Mathf.Max(320, available.y - 40);
            if (profile.WidthPx <= 0 || profile.HeightPx <= 0)
            {
                return;
            }

            var resolution = FitIntoBoundsWithAspect(new Vector2Int(profile.WidthPx, profile.HeightPx), new Vector2Int(maxWidth, maxHeight));

            if (!previousDesktopResolution.HasValue)
            {
                previousDesktopResolution = new Vector2Int(Screen.width, Screen.height);
                previousFullscreenMode = Screen.fullScreenMode;
            }

            if (lastPreviewResolution.HasValue && lastPreviewResolution.Value == resolution && Screen.fullScreenMode == FullScreenMode.Windowed)
            {
                return;
            }

            lastPreviewResolution = resolution;
            Screen.SetResolution(resolution.x, resolution.y, FullScreenMode.Windowed);
        }

        private static Vector2Int ComputeScaledReference(Vector2Int baseReference, float multiplier)
        {
            var safeMultiplier = Mathf.Max(0.1f, multiplier);
            return new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt(baseReference.x / safeMultiplier)),
                Mathf.Max(1, Mathf.RoundToInt(baseReference.y / safeMultiplier)));
        }

        private static float ComputePreviewMatch(PreviewDeviceProfile profile, Vector2 referenceResolution)
        {
            var logicalWidth = Mathf.Max(1f, (float)profile.LogicalWidth);
            var logicalHeight = Mathf.Max(1f, (float)profile.LogicalHeight);
            var aspect = logicalHeight / logicalWidth;
            var refWidth = Mathf.Max(1f, referenceResolution.x);
            var refHeight = Mathf.Max(1f, referenceResolution.y);
            var numerator = Mathf.Log(refWidth / logicalWidth);
            var denominator = Mathf.Log(aspect) + Mathf.Log(refWidth) - Mathf.Log(refHeight);
            if (float.IsNaN(denominator) || float.IsInfinity(denominator) || Mathf.Abs(denominator) < 0.0001f)
            {
                return DefaultMatchValue;
            }

            var match = numerator / denominator;
            if (float.IsNaN(match) || float.IsInfinity(match))
            {
                return DefaultMatchValue;
            }

            return Mathf.Clamp01(match);
        }

        private static bool IsMobilePlatform(RuntimePlatform platform)
        {
            return platform == RuntimePlatform.Android || platform == RuntimePlatform.IPhonePlayer;
        }

        private static Vector2Int GetAvailableDesktopArea()
        {
            var mainDisplay = Display.main;
            if (mainDisplay != null && mainDisplay.systemWidth > 0 && mainDisplay.systemHeight > 0)
            {
                return new Vector2Int(mainDisplay.systemWidth, mainDisplay.systemHeight);
            }

            var currentResolution = Screen.currentResolution;
            if (currentResolution.width > 0 && currentResolution.height > 0)
            {
                return new Vector2Int(currentResolution.width, currentResolution.height);
            }

            return new Vector2Int(Screen.width, Screen.height);
        }

        private static Vector2Int FitIntoBoundsWithAspect(Vector2Int target, Vector2Int bounds)
        {
            var safeWidth = Mathf.Max(1, target.x);
            var safeHeight = Mathf.Max(1, target.y);
            var aspect = safeWidth / (float)safeHeight;

            var maxWidth = Mathf.Max(1, bounds.x);
            var maxHeight = Mathf.Max(1, bounds.y);

            var width = Mathf.Min(safeWidth, maxWidth);
            var height = Mathf.Min(safeHeight, maxHeight);

            if (width / aspect > maxHeight)
            {
                width = Mathf.FloorToInt(maxHeight * aspect);
                height = maxHeight;
            }
            else
            {
                height = Mathf.FloorToInt(width / aspect);
            }

            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            return new Vector2Int(width, height);
        }
    }
}
