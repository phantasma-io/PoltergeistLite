using System;
using System.Collections;
using System.IO;
using UnityEngine;
using PhantasmaPhoenix.Unity.Core.Logging;

namespace Poltergeist.UiToolkit
{
    internal sealed class WalletUiScreenshotHelper
    {
        private const string LogPrefix = "[UITK] ";
        private readonly MonoBehaviour host;
        private readonly Func<global::Poltergeist.Settings> settingsProvider;
        private readonly WalletUiPreviewHelper previewHelper;

        public WalletUiScreenshotHelper(
            MonoBehaviour host,
            Func<global::Poltergeist.Settings> settingsProvider,
            WalletUiPreviewHelper previewHelper)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
            this.previewHelper = previewHelper ?? throw new ArgumentNullException(nameof(previewHelper));
        }

        public void HandleHotkey()
        {
            var settings = settingsProvider();
            if (settings == null || !settings.devMode)
            {
                return;
            }

            var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!ctrl || !shift || !Input.GetKeyDown(KeyCode.S))
            {
                return;
            }

            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "screenshots");
                Directory.CreateDirectory(dir);
                var file = $"uitk-shot-{DateTime.Now:yyyyMMdd-HHmmss}.png";
                var path = Path.Combine(dir, file);
                var minSideRequirement = previewHelper.GetMinSideRequirement(settings);
                var aspect = previewHelper.GetPreviewAspect(settings);
                host.StartCoroutine(CaptureScreenshotAsync(minSideRequirement, aspect, path));
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{LogPrefix}Screenshot failed: {e}");
            }
        }

        private IEnumerator CaptureScreenshotAsync(int minSideRequirement, float targetAspect, string path)
        {
            yield return new WaitForEndOfFrame();

            var captureWidth = Screen.width;
            var captureHeight = Screen.height;
            if (captureWidth <= 0 || captureHeight <= 0)
            {
                yield break;
            }

            try
            {
                var rt = RenderTexture.GetTemporary(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                tex.Apply(false, false);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                tex = PrepareScreenshotTexture(tex, targetAspect, minSideRequirement);
                var finalWidth = tex.width;
                var finalHeight = tex.height;
                var png = tex.EncodeToPNG();
                UnityEngine.Object.Destroy(tex);
                File.WriteAllBytes(path, png);
                Log.Write($"{LogPrefix}Screenshot saved: {path}, final={finalWidth}x{finalHeight}, raw={captureWidth}x{captureHeight}, aspectTarget={targetAspect}, minSide={minSideRequirement}");
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{LogPrefix}Screenshot capture failed: {e}");
            }
        }

        private static Texture2D PrepareScreenshotTexture(Texture2D source, float targetAspect, int minSideRequirement)
        {
            if (source == null)
            {
                return source;
            }

            var aspect = targetAspect > 0f ? targetAspect : source.width / (float)source.height;

            var canvasWidth = source.width;
            var canvasHeight = Mathf.RoundToInt(canvasWidth / aspect);
            if (canvasHeight < source.height)
            {
                canvasHeight = source.height;
                canvasWidth = Mathf.RoundToInt(canvasHeight * aspect);
            }

            canvasWidth = Mathf.Max(canvasWidth, source.width);
            canvasHeight = Mathf.Max(canvasHeight, source.height);

            var minCanvasSide = Mathf.Min(canvasWidth, canvasHeight);
            var scale = minSideRequirement > 0 ? Mathf.Max(1f, Mathf.Ceil(minSideRequirement / Mathf.Max(1f, minCanvasSide))) : 1f;
            var targetWidth = Mathf.Max(1, Mathf.RoundToInt(canvasWidth * scale));
            var targetHeight = Mathf.Max(1, Mathf.RoundToInt(canvasHeight * scale));

            var fitScale = Mathf.Min(targetWidth / (float)source.width, targetHeight / (float)source.height);
            var drawWidth = Mathf.RoundToInt(source.width * fitScale);
            var drawHeight = Mathf.RoundToInt(source.height * fitScale);
            var offsetX = Mathf.RoundToInt((targetWidth - drawWidth) * 0.5f);
            var offsetY = Mathf.RoundToInt((targetHeight - drawHeight) * 0.5f);

            var rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                var background = WalletUiTheme.ScreenBackground;
                GL.Clear(true, true, background);
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, targetWidth, targetHeight, 0);
                Graphics.DrawTexture(new Rect(offsetX, targetHeight - offsetY - drawHeight, drawWidth, drawHeight), source);
                GL.PopMatrix();

                var result = new Texture2D(targetWidth, targetHeight, source.format, false);
                result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                result.Apply(false, false);
                UnityEngine.Object.Destroy(source);
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
