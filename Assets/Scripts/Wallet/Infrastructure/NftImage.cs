using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Linq;
using PhantasmaPhoenix.Unity.Core.Logging;
using System.Threading;
using System.Threading.Tasks;
using Poltergeist.Wallet;

// Storing NFT images.
public static class NftImages
{
    private const int MaxInlineImageBytes = 2 * 1024 * 1024; // Guardrails for inline/base64 images.
    private const int MaxInlineImageDimension = 2048;
    private static readonly HashSet<string> AllowedInlineMimeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/jpg"
    };

    public static void Clear(string symbol = "")
    {
        if (string.IsNullOrEmpty(symbol))
        {
            Images.Clear();
        }
        else
        {
            var imagesToRemove = Images.Values.Cast<Image>().Where(x => x.Symbol.ToLower() == symbol.ToLower()).ToArray();
            foreach (var imageToRemove in imagesToRemove)
            {
                Images.Remove(imageToRemove.Url);
                Cache.ClearTexture($"{symbol.ToLower()}-image-{imageToRemove.NftId}");
            }
        }
    }

    public struct Image
    {
        public string Url;
        public Texture2D Texture;
        public string Symbol;
        public string NftId;
    }

    private static Hashtable Images = new Hashtable();

    public static bool CheckIfImageLoaded(string Url)
    {
        if (string.IsNullOrEmpty(Url))
            return false;

        return Images.Contains(Url);
    }

    public static Image GetImage(string Url)
    {
        if (string.IsNullOrEmpty(Url))
            return new Image();

        return Images.Contains(Url) ? (Image)Images[Url] : new Image();
    }

    public static bool TryCacheInlineImage(string symbol, string source, string nftId, out Texture2D texture)
    {
        texture = null;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (CheckIfImageLoaded(source))
        {
            texture = GetImage(source).Texture;
            return texture != null;
        }

        if (!TryGetInlineImageBytes(source, out var bytes) || bytes.Length == 0)
        {
            return false;
        }

        if (bytes.Length > MaxInlineImageBytes)
        {
            Log.WriteWarning($"NFT inline image skipped for {symbol}:{nftId} (size {bytes.Length} bytes).");
            return false;
        }

        try
        {
            var inlineTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(inlineTexture, bytes, false))
            {
                UnityEngine.Object.Destroy(inlineTexture);
                return false;
            }

            if (inlineTexture.width <= 0 || inlineTexture.height <= 0 ||
                inlineTexture.width > MaxInlineImageDimension || inlineTexture.height > MaxInlineImageDimension)
            {
                UnityEngine.Object.Destroy(inlineTexture);
                return false;
            }

            if (!ValidateLoadedTexture(ref inlineTexture, readLockedImages: true))
            {
                UnityEngine.Object.Destroy(inlineTexture);
                return false;
            }

            inlineTexture.wrapMode = TextureWrapMode.Clamp;
            inlineTexture.filterMode = FilterMode.Bilinear;
            texture = inlineTexture;

            var image = new Image
            {
                Url = source,
                Texture = inlineTexture,
                Symbol = symbol?.ToLowerInvariant() ?? string.Empty,
                NftId = nftId ?? string.Empty
            };

            lock (Images)
            {
                if (!CheckIfImageLoaded(image.Url))
                {
                    Images.Add(image.Url, image);
                }
            }

            return true;
        }
        catch (Exception e)
        {
            Log.WriteWarning($"NFT inline image decode failed for {symbol}:{nftId}: {e.Message}");
            return false;
        }
    }

    private static int imagesLoadedSimultaneously = 0;

    public static Texture2D CreateReadable(this Texture2D texture)
    {
        Texture2D output = new Texture2D(texture.width, texture.height);

        RenderTexture _renderTemp = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);

        Graphics.Blit(texture, _renderTemp);

        RenderTexture _renderActive = RenderTexture.active;

        RenderTexture.active = _renderTemp;

        output.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);

        RenderTexture.active = _renderActive;

        RenderTexture.ReleaseTemporary(_renderTemp);
        _renderTemp = null;

        output.Apply();

        return output;
    }

    private static bool[] invalidImageMask = new bool[]
    {
    false, false, false, true, true, false, false, false,
    false, false, false, true, true, false, false, false,
    false, false, false, false, false, false, false, false,
    false, false, false, true, true, false, false, false,
    false, false, false, false, true, true, false, false,
    false, true, true, false, false, true, true, false,
    false, true, true, false, false, true, true, false,
    false, false, true, true, true, true, false, false
    };

    public static bool ValidateLoadedTexture(ref Texture2D texture, bool readLockedImages = false)
    {
        if (texture.width == 8 && texture.height == 8)
        {
            Texture2D evaluateTexture = texture;

            if (!evaluateTexture.isReadable)
            {
                if (!readLockedImages)
                    return false;

                evaluateTexture = evaluateTexture.CreateReadable();
            }

            Color32[] pixels = evaluateTexture.GetPixels32();
            for (int i = 0, iC = pixels.Length; i < iC; i++)
            {
                Color32 pixel = pixels[i];
                if (invalidImageMask[i] != ((pixel.r == 255) && (pixel.g == 0) && (pixel.b == 0)))
                    return true;
            }

            return false;
        }

        return true;
    }

    public static async Task DownloadImageAsync(string symbol, string url, string nftId, CancellationToken cancellationToken = default)
    {
        // Log.Write("NFT image loading: URL: " + url);
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        // Trying to avoid downloading same image multiple times.
        if (CheckIfImageLoaded(url))
        {
            return;
        }

        var texture = Cache.GetTexture($"{symbol.ToLower()}-image-{nftId}", 0);
        if (texture != null)
        {
            var image = new Image();
            image.Url = url;
            image.Texture = texture;
            image.Symbol = symbol.ToLower();
            image.NftId = nftId;

            lock (Images)
            {
                if (!CheckIfImageLoaded(image.Url))
                    Images.Add(image.Url, image);
            }
            return;
        }

        while (imagesLoadedSimultaneously > 5)
        {
            await Task.Yield();

            // Trying to avoid downloading same image multiple times.
            if (CheckIfImageLoaded(url))
            {
                return;
            }
        }

        imagesLoadedSimultaneously++;

        try
        {
            var fullUrl = url;
            if (!fullUrl.Contains("/"))
            {
                // This is a pure IPFS hash.
                fullUrl = "https://gateway.ipfs.io/ipfs/" + fullUrl;
            }
            else if (fullUrl.StartsWith("ipfs://"))
            {
                fullUrl = "https://gateway.ipfs.io/ipfs/" + fullUrl.Substring("ipfs://".Length);
            }
            if (!fullUrl.Contains("://"))
            {
                Log.WriteWarning("NFT image loading: URL does not contain schema, defaulting to https://" + fullUrl);
                fullUrl = "https://" + fullUrl;
            }

            var normalizedUrl = NormalizeImageUrl(fullUrl);
            if (string.IsNullOrEmpty(normalizedUrl))
            {
                Log.WriteWarning($"NFT image loading: Invalid URL '{fullUrl}'. Skipping download.");
                return;
            }

            Log.Write("NFT image loading: Full URL: " + normalizedUrl);

            UnityWebRequest request;
            try
            {
                request = UnityWebRequestTexture.GetTexture(normalizedUrl);
            }
            catch (Exception e) when (e is UriFormatException || e is ArgumentException)
            {
                Log.WriteWarning($"NFT image loading: Failed to build request for '{normalizedUrl}': {e.Message}");
                return;
            }
            // Log.Write("NFT image loading: Sending request...");
            await SendRequestAsync(request, cancellationToken);
            // var downloadedBytes = request.downloadHandler?.data?.Length ?? 0;
            // Log.Write($"NFT image loading: Request finished. result={request.result}, status={request.responseCode}, bytes={downloadedBytes}, error={request.error}");
            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError || request.result == UnityWebRequest.Result.DataProcessingError)
            {
                Log.Write(request.error);

                var image = new Image();
                image.Url = url;
                image.Texture = null;
                image.Symbol = symbol.ToLower();
                image.NftId = nftId;

                lock (Images)
                {
                    if (!CheckIfImageLoaded(image.Url))
                        Images.Add(image.Url, image);
                }
            }
            else
            {
                var image = new Image();
                image.Url = url;
                image.Texture = ((DownloadHandlerTexture)request.downloadHandler).texture;
                image.Symbol = symbol.ToLower();
                image.NftId = nftId;

                if (!ValidateLoadedTexture(ref image.Texture, true))
                {
                    var invalidWidth = image.Texture ? image.Texture.width : 0;
                    var invalidHeight = image.Texture ? image.Texture.height : 0;
                    Log.Write($"NFT image loading: Invalid image. Size={invalidWidth}x{invalidHeight}");
                    image.Texture = null;
                }
                else
                {
                    // Log.Write($"NFT image loading: Texture validated. Size={image.Texture.width}x{image.Texture.height}");
                }

                lock (Images)
                {
                    if (!CheckIfImageLoaded(image.Url))
                        Images.Add(image.Url, image);
                }

                if (image.Texture)
                    Cache.AddTexture($"{symbol.ToLower()}-image-{nftId}", image.Texture);
            }
        }
        finally
        {
            imagesLoadedSimultaneously--;
        }
    }

    private static bool TryGetInlineImageBytes(string source, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var trimmed = source.Trim();
        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var commaIndex = trimmed.IndexOf(',');
            if (commaIndex <= 0 || commaIndex == trimmed.Length - 1)
            {
                return false;
            }

            var headerPart = trimmed.Substring(5, commaIndex - 5);
            var dataPart = trimmed.Substring(commaIndex + 1);
            var headerPieces = headerPart.Split(';');
            if (headerPieces.Length < 2)
            {
                return false;
            }

            var mimeType = headerPieces[0].Trim();
            if (!AllowedInlineMimeTypes.Contains(mimeType))
            {
                return false;
            }

            var encoding = headerPieces[headerPieces.Length - 1].Trim();
            if (!encoding.Equals("base64", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return TryDecodeBase64(dataPart, out bytes);
        }

        return false;
    }

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(value);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static Task SendRequestAsync(UnityWebRequest request, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                if (tcs.Task.IsCompleted)
                {
                    return;
                }

                request.Abort();
                tcs.TrySetCanceled(cancellationToken);
            });
        }

        var asyncOp = request.SendWebRequest();
        asyncOp.completed += _ => UnityTaskRunner.PostToMainThread(() =>
        {
            if (!tcs.Task.IsCompleted)
            {
                tcs.TrySetResult(true);
            }
        });

        return tcs.Task;
    }

    private static string NormalizeImageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var trimmed = url.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out _) ? trimmed : string.Empty;
    }
}
