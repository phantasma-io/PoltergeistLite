using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Unity.Core.Logging;
using UnityEngine;

public static class TokenIconCache
{
    private const int MaxIconBytes = 256 * 1024; // 256 KB cap to prevent memory abuse
    private const int MaxIconDimension = 1024; // avoid ultra high-res textures sneaking in

    private static readonly HashSet<string> AllowedMimeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/jpg"
    };

    private static readonly Dictionary<string, Texture2D> IconTextures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

    public static void Reset()
    {
        foreach (var texture in IconTextures.Values)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        IconTextures.Clear();
    }

    public static void RebuildFromTokens(IEnumerable<TokenResult> tokens)
    {
        Reset();

        if (tokens == null)
        {
            return;
        }

        foreach (var token in tokens)
        {
            TryCacheIcon(token);
        }
    }

    public static bool TryGetTexture(string symbol, out Texture texture)
    {
        texture = null;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        if (IconTextures.TryGetValue(symbol, out var cached) && cached != null)
        {
            texture = cached;
            return true;
        }

        return false;
    }

    private static void TryCacheIcon(TokenResult token)
    {
        if (token == null || token.Metadata == null || token.Metadata.Length == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(token.Symbol))
        {
            return;
        }

        var iconValue = ExtractIconValue(token);
        if (string.IsNullOrWhiteSpace(iconValue))
        {
            return;
        }

        if (!TryParseDataUri(iconValue, out var mimeType, out var imageBytes))
        {
            Log.Write($"Token icon rejected for {token.Symbol}: invalid data URI.", Log.Level.Debug1);
            return;
        }

        if (!AllowedMimeTypes.Contains(mimeType))
        {
            Log.Write($"Token icon rejected for {token.Symbol}: unsupported MIME type '{mimeType}'.", Log.Level.Debug1);
            return;
        }

        if (imageBytes.Length == 0 || imageBytes.Length > MaxIconBytes)
        {
            Log.Write($"Token icon rejected for {token.Symbol}: size {imageBytes.Length} bytes is outside allowed range.", Log.Level.Debug1);
            return;
        }

        try
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, imageBytes, false))
            {
                UnityEngine.Object.Destroy(texture);
                Log.Write($"Token icon rejected for {token.Symbol}: failed to decode texture.", Log.Level.Debug1);
                return;
            }

            if (texture.width > MaxIconDimension || texture.height > MaxIconDimension)
            {
                Log.Write($"Token icon rejected for {token.Symbol}: resolution {texture.width}x{texture.height} exceeds {MaxIconDimension}px limit.", Log.Level.Debug1);
                UnityEngine.Object.Destroy(texture);
                return;
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            IconTextures[token.Symbol] = texture;
        }
        catch (Exception e)
        {
            Log.Write($"Token icon rejected for {token?.Symbol}: {e.Message}", Log.Level.Debug1);
        }
    }

    private static string ExtractIconValue(TokenResult token)
    {
        foreach (var property in token.Metadata)
        {
            if (ReferenceEquals(property, null))
            {
                continue;
            }

            var key = property.Key;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (key.Equals("icon", StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static bool TryParseDataUri(string value, out string mimeType, out byte[] payload)
    {
        mimeType = string.Empty;
        payload = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var commaIndex = trimmed.IndexOf(',');
        if (commaIndex <= 0)
        {
            return false;
        }

        var headerPart = trimmed.Substring(5, commaIndex - 5);
        var dataPart = trimmed.Substring(commaIndex + 1);
        if (string.IsNullOrWhiteSpace(headerPart) || string.IsNullOrWhiteSpace(dataPart))
        {
            return false;
        }

        var headerPieces = headerPart.Split(';');
        if (headerPieces.Length < 2)
        {
            return false;
        }

        var encoding = headerPieces[headerPieces.Length - 1].Trim();
        if (!encoding.Equals("base64", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        mimeType = headerPieces[0].Trim();
        if (!mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sanitized = SanitizeBase64Payload(dataPart);

        try
        {
            payload = Convert.FromBase64String(sanitized);
            return true;
        }
        catch (FormatException)
        {
            mimeType = string.Empty;
            payload = Array.Empty<byte>();
            return false;
        }
    }

    private static string SanitizeBase64Payload(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(payload.Length);
        foreach (var ch in payload)
        {
            if (!char.IsWhiteSpace(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
