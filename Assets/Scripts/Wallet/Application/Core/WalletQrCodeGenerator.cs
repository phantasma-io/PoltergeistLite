using UnityEngine;
using ZXing;
using ZXing.QrCode;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Generates QR code textures for wallet data.
    /// </summary>
    public sealed class WalletQrCodeGenerator
    {
        public Texture2D Generate(string text, int size = 256)
        {
            var encoded = new Texture2D(size, size);
            var color32 = Encode(text, size, size);
            encoded.SetPixels32(color32);
            encoded.Apply();
            return encoded;
        }

        private static Color32[] Encode(string textForEncoding, int width, int height)
        {
            var writer = new BarcodeWriter
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions
                {
                    Height = height,
                    Width = width
                }
            };
            return writer.Write(textForEncoding);
        }
    }
}
