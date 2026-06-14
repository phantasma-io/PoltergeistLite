using System;
using System.Collections.Generic;
using UnityEngine;
using ZXing;
using ZXing.Common;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Decodes QR codes from camera/image pixels and recognises Phantasma Link v5 pairing
    /// URIs (cross-device QR pairing). The decoded URI is handed unchanged to the same
    /// LinkDeeplinkEndpoint that the OS deeplink path uses.
    /// </summary>
    public sealed class WalletQrScanner
    {
        // Restricting to QR and enabling TryHarder helps with noisier live camera frames.
        private readonly BarcodeReader reader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE }
            }
        };

        /// <summary>Returns the decoded QR text, or null when no QR is present in the frame.</summary>
        public string TryDecode(Color32[] pixels, int width, int height)
        {
            if (pixels == null || width <= 0 || height <= 0 || pixels.Length < width * height)
            {
                return null;
            }

            try
            {
                return reader.Decode(pixels, width, height)?.Text;
            }
            catch (Exception)
            {
                // Decode throws on malformed frames; treat as "no code this frame".
                return null;
            }
        }

        /// <summary>
        /// True when the text is a Phantasma Link pairing URI we can forward to the deeplink
        /// endpoint: the verified universal link or the custom scheme. The endpoint itself is
        /// the final authority and rejects anything that is not a valid pairing payload.
        /// </summary>
        public static bool IsPairingUri(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            return trimmed.StartsWith("phantasma:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("https://link.phantasma.info/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
