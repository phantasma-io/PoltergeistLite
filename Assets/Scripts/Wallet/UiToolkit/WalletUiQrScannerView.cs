using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Unity.Core.Logging;

namespace Poltergeist.UiToolkit
{
    /// <summary>
    /// Drives the cross-device QR pairing scanner: opens the device camera, decodes QR frames
    /// with WalletQrScanner, and forwards a recognised pairing URI to the caller (which hands it
    /// to the deeplink endpoint). Releases the camera whenever the panel closes.
    /// </summary>
    public sealed class WalletUiQrScannerView
    {
        private const string LogPrefix = "[UITK][QR] ";
        private const long ScanIntervalMs = 150;

        private readonly WalletUiModalHost modalHost;
        private readonly Action<VisualElement> applyDefaultFont;
        private readonly string title;
        private readonly string hint;
        private readonly string rejectMessage;
        private readonly Func<string, string> tryAccept;
        private readonly Action<string> onAccepted;
        private readonly bool enableClipboardPaste;
        private readonly WalletQrScanner scanner = new WalletQrScanner();

        private VisualElement panel;
        private Image preview;
        private Label status;
        private Button pasteButton;
        private IVisualElementScheduledItem scanLoop;
        private IVisualElementScheduledItem pasteLoop;
        private WebCamTexture camera;
        private bool closed;
        private bool orientationApplied;

        public WalletUiQrScannerView(
            WalletUiModalHost modalHost,
            Action<VisualElement> applyDefaultFont,
            string title,
            string hint,
            string rejectMessage,
            Func<string, string> tryAccept,
            Action<string> onAccepted,
            bool enableClipboardPaste = false)
        {
            this.modalHost = modalHost ?? throw new ArgumentNullException(nameof(modalHost));
            this.applyDefaultFont = applyDefaultFont ?? (_ => { });
            this.title = title;
            this.hint = hint;
            this.rejectMessage = string.IsNullOrWhiteSpace(rejectMessage) ? "That QR was not recognised. Keep scanning." : rejectMessage;
            this.tryAccept = tryAccept ?? throw new ArgumentNullException(nameof(tryAccept));
            this.onAccepted = onAccepted ?? (_ => { });
            // When true, a clipboard Paste button is shown that accepts whatever tryAccept accepts and
            // acts like a scanned code. Callers that already have their own paste UI leave it false.
            this.enableClipboardPaste = enableClipboardPaste;
        }

        public void Open()
        {
            closed = false;
            orientationApplied = false;
            panel = WalletUiModalFactory.CreateQrScannerPanel(title, hint, Close, applyDefaultFont, enableClipboardPaste, out preview, out status, out pasteButton);
            // Releasing the camera on detach covers closes that do not go through Cancel
            // (navigation away, HideAll). CloseInternal is idempotent.
            panel.RegisterCallback<DetachFromPanelEvent>(_ => CloseInternal());
            modalHost.ShowPanel(panel);
            RequestCameraThenStart();
            // Clipboard is the camera-free pairing path (desktop has no camera): poll it so the Paste
            // button auto-enables the moment a valid pairing link is on the clipboard. Pairing-only.
            if (enableClipboardPaste)
            {
                RefreshPasteState();
                pasteLoop = panel.schedule.Execute(RefreshPasteState).Every(500);
            }
        }

        private void RequestCameraThenStart()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += _ => StartCamera();
                callbacks.PermissionDenied += _ => SetStatus("Camera permission denied. Cancel and grant it to scan.");
                callbacks.PermissionDeniedAndDontAskAgain += _ => SetStatus("Camera permission denied. Enable it in system settings.");
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera, callbacks);
                return;
            }
#endif
            StartCamera();
        }

        private void StartCamera()
        {
            if (closed)
            {
                return;
            }

            if (WebCamTexture.devices == null || WebCamTexture.devices.Length == 0)
            {
                SetStatus("No camera available on this device.");
                return;
            }

            try
            {
                // Prefer a back-facing camera for scanning a screen across the table.
                string deviceName = null;
                foreach (var device in WebCamTexture.devices)
                {
                    if (!device.isFrontFacing)
                    {
                        deviceName = device.name;
                        break;
                    }
                }

                camera = string.IsNullOrEmpty(deviceName) ? new WebCamTexture() : new WebCamTexture(deviceName);
                if (preview != null)
                {
                    preview.image = camera;
                }
                camera.Play();
            }
            catch (Exception e)
            {
                SetStatus("Cannot open the camera.");
                Log.WriteWarning($"{LogPrefix}camera open failed: {e.Message}");
                return;
            }

            scanLoop = panel.schedule.Execute(ScanTick).Every(ScanIntervalMs);
        }

        // Back-camera frames arrive pre-rotated (portrait Android reports 90). Rotate the preview
        // by the reported angle to bring it upright - the + sign was confirmed on-device. No mirror:
        // the back camera is not mirrored. Display-only; QR decode is orientation-invariant.
        private void ApplyPreviewOrientation()
        {
            if (preview == null || camera == null)
            {
                return;
            }

            preview.style.rotate = new Rotate(new Angle(camera.videoRotationAngle, AngleUnit.Degree));
            preview.style.scale = new Scale(Vector3.one);
            preview.style.visibility = Visibility.Visible;
        }

        private void ScanTick()
        {
            if (closed || camera == null || !camera.isPlaying || camera.width <= 16 || !camera.didUpdateThisFrame)
            {
                return;
            }

            if (!orientationApplied)
            {
                ApplyPreviewOrientation();
                orientationApplied = true;
            }

            Color32[] pixels;
            try
            {
                pixels = camera.GetPixels32();
            }
            catch
            {
                return;
            }

            var text = scanner.TryDecode(pixels, camera.width, camera.height);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var accepted = tryAccept(text);
            if (string.IsNullOrEmpty(accepted))
            {
                SetStatus(rejectMessage);
                return;
            }

            Log.Write($"{LogPrefix}QR accepted");
            CloseInternal();
            onAccepted(accepted);
        }

        // Clipboard pairing path (camera-free, primarily for desktop). Keep Paste enabled and
        // clickable ONLY while the clipboard holds something the endpoint accepts, so a click can
        // never forward garbage; an accepted paste then behaves exactly like a scanned QR.
        private void RefreshPasteState()
        {
            if (closed || pasteButton == null)
            {
                return;
            }

            // Generic: enable only when the clipboard holds something THIS scanner would accept,
            // using the SAME tryAccept predicate as the camera path. The view stays content-agnostic
            // (the caller decides what is acceptable); it knows nothing about pairing or addresses.
            var clipboard = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
            var isAcceptable = !string.IsNullOrEmpty(tryAccept(clipboard));
            WalletUiCommon.SetButtonEnabledVisual(pasteButton, isAcceptable, WalletUiTheme.TextPrimary, WalletUiTheme.TextMuted);
            // Re-wire on every tick so the handler is attached exactly when (and only when) the
            // clipboard is acceptable; the unconditional removal first keeps it from stacking.
            pasteButton.clicked -= PasteFromClipboard;
            if (isAcceptable)
            {
                pasteButton.clicked += PasteFromClipboard;
            }
        }

        private void PasteFromClipboard()
        {
            if (closed)
            {
                return;
            }

            var clipboard = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
            var accepted = tryAccept(clipboard);
            if (string.IsNullOrEmpty(accepted))
            {
                // Clipboard changed between the poll tick and the click: do nothing rather than
                // forward a non-pairing string.
                return;
            }

            Log.Write($"{LogPrefix}clipboard pairing link accepted");
            CloseInternal();
            onAccepted(accepted);
        }

        private void SetStatus(string message)
        {
            if (status != null)
            {
                status.text = message;
            }
        }

        private void Close()
        {
            CloseInternal();
        }

        private void CloseInternal()
        {
            if (closed)
            {
                return;
            }
            closed = true;

            scanLoop?.Pause();
            scanLoop = null;

            pasteLoop?.Pause();
            pasteLoop = null;

            if (camera != null)
            {
                try
                {
                    if (camera.isPlaying)
                    {
                        camera.Stop();
                    }
                }
                catch
                {
                    // ignore teardown errors
                }

                if (preview != null)
                {
                    preview.image = null;
                }
                UnityEngine.Object.Destroy(camera);
                camera = null;
            }

            modalHost.HidePanel();
        }
    }
}
