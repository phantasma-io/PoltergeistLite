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
        private readonly Action<string> onPairingUri;
        private readonly WalletQrScanner scanner = new WalletQrScanner();

        private VisualElement panel;
        private Image preview;
        private Label status;
        private IVisualElementScheduledItem scanLoop;
        private WebCamTexture camera;
        private bool closed;

        public WalletUiQrScannerView(WalletUiModalHost modalHost, Action<VisualElement> applyDefaultFont, Action<string> onPairingUri)
        {
            this.modalHost = modalHost ?? throw new ArgumentNullException(nameof(modalHost));
            this.applyDefaultFont = applyDefaultFont ?? (_ => { });
            this.onPairingUri = onPairingUri ?? (_ => { });
        }

        public void Open()
        {
            closed = false;
            panel = WalletUiModalFactory.CreateQrScannerPanel(Close, applyDefaultFont, out preview, out status);
            // Releasing the camera on detach covers closes that do not go through Cancel
            // (navigation away, HideAll). CloseInternal is idempotent.
            panel.RegisterCallback<DetachFromPanelEvent>(_ => CloseInternal());
            modalHost.ShowPanel(panel);
            RequestCameraThenStart();
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

        private void ScanTick()
        {
            if (closed || camera == null || !camera.isPlaying || camera.width <= 16 || !camera.didUpdateThisFrame)
            {
                return;
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

            if (!WalletQrScanner.IsPairingUri(text))
            {
                SetStatus("That QR is not a Phantasma pairing code. Keep scanning.");
                return;
            }

            var uri = text.Trim();
            Log.Write($"{LogPrefix}pairing QR decoded");
            CloseInternal();
            onPairingUri(uri);
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
