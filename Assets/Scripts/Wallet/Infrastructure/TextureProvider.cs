using System;
using System.Collections.Generic;
using Poltergeist;
using UnityEngine;

// Runtime provider of wallet UI textures: NFT type placeholders loaded from
// Resources, and token icons resolved from the RPC icon cache or bundled
// Resources, kept in a per-symbol cache. Lives in the scene as a singleton.
public class TextureProvider : MonoBehaviour
{
    public static TextureProvider Instance { get; private set; }

    public Texture NftAudioPlaceholder { get; private set; }
    public Texture NftPhotoPlaceholder { get; private set; }
    public Texture NftVideoPlaceholder { get; private set; }

    private readonly Dictionary<string, Texture> _tokenIcons = new Dictionary<string, Texture>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        NftAudioPlaceholder = Resources.Load<Texture>("Common/nft_audio_placeholder");
        NftPhotoPlaceholder = Resources.Load<Texture>("Common/nft_photo_placeholder");
        NftVideoPlaceholder = Resources.Load<Texture>("Common/nft_video_placeholder");
    }

    public void UnloadTokens()
    {
        _tokenIcons.Clear();
    }

    public Texture GetTokenIcon(string symbol, PlatformKind platform)
    {
        symbol ??= string.Empty;

        if (symbol.Length > 0 && _tokenIcons.TryGetValue(symbol, out var cached) && cached != null)
        {
            return cached;
        }

        var texture = ResolveTokenTexture(symbol, platform);
        _tokenIcons[symbol] = texture;
        return texture;
    }

    private static Texture ResolveTokenTexture(string symbol, PlatformKind platform)
    {
        if (TokenIconCache.TryGetTexture(symbol, out var fromRpc) && fromRpc != null)
        {
            return fromRpc;
        }

        var bundled = LoadBundledIcon("Common/Tokens/", symbol)
            ?? LoadBundledIcon("Common/", symbol);
        if (bundled != null)
        {
            return bundled;
        }

        return Resources.Load<Texture>($"Common/Tokens/UNKNOWN_TOKEN_{platform.ToString().ToUpperInvariant()}");
    }

    // Resources lookups are case-sensitive on some file systems, so try the
    // symbol as given and then its upper/lower variants before giving up.
    private static Texture LoadBundledIcon(string basePath, string symbol)
    {
        if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        foreach (var candidate in IconNameVariants(symbol))
        {
            var texture = Resources.Load<Texture>(basePath + candidate);
            if (texture != null)
            {
                return texture;
            }
        }

        return null;
    }

    private static IEnumerable<string> IconNameVariants(string symbol)
    {
        yield return symbol;

        var upper = symbol.ToUpperInvariant();
        if (!string.Equals(upper, symbol, StringComparison.Ordinal))
        {
            yield return upper;
        }

        var lower = symbol.ToLowerInvariant();
        if (!string.Equals(lower, symbol, StringComparison.Ordinal) && !string.Equals(lower, upper, StringComparison.Ordinal))
        {
            yield return lower;
        }
    }
}
