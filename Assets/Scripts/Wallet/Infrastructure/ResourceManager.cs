using System;
using System.Collections.Generic;
using Poltergeist;
using UnityEngine;

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    public Texture WalletLogo { get; private set; }
    public Texture Dropshadow { get; private set; }
    public Texture NftAudioPlaceholder { get; private set; }
    public Texture NftPhotoPlaceholder { get; private set; }
    public Texture NftVideoPlaceholder { get; private set; }

    void Start()
    {
        WalletLogo = GetToken("soul", PlatformKind.Phantasma);
        Dropshadow = Resources.Load<Texture>("Common/dropshadow");
        NftAudioPlaceholder = Resources.Load<Texture>("Common/nft_audio_placeholder");
        NftPhotoPlaceholder = Resources.Load<Texture>("Common/nft_photo_placeholder");
        NftVideoPlaceholder = Resources.Load<Texture>("Common/nft_video_placeholder");
    }

    private Dictionary<string, Texture> _symbols = new Dictionary<string, Texture>();

    public void UnloadTokens()
    {
        _symbols.Clear();
    }

    private static Texture TryLoadTokenTexture(string basePath, string symbol)
    {
        if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        var texture = Resources.Load<Texture>($"{basePath}{symbol}");
        if (texture != null)
        {
            return texture;
        }

        var upper = symbol.ToUpperInvariant();
        if (!string.Equals(upper, symbol, StringComparison.Ordinal))
        {
            texture = Resources.Load<Texture>($"{basePath}{upper}");
            if (texture != null)
            {
                return texture;
            }
        }

        var lower = symbol.ToLowerInvariant();
        if (!string.Equals(lower, symbol, StringComparison.Ordinal) && !string.Equals(lower, upper, StringComparison.Ordinal))
        {
            texture = Resources.Load<Texture>($"{basePath}{lower}");
        }

        return texture;
    }

    //https://github.com/CityOfZion/neon-wallet/tree/dev/app/assets/nep5/png
    public Texture GetToken(string symbol, PlatformKind platform)
    {
        symbol ??= string.Empty;

        if (!string.IsNullOrEmpty(symbol) && _symbols.TryGetValue(symbol, out var cachedTexture) && cachedTexture != null)
        {
            return cachedTexture;
        }

        if (TokenIconCache.TryGetTexture(symbol, out var rpcTexture) && rpcTexture != null)
        {
            _symbols[symbol] = rpcTexture;
            return rpcTexture;
        }

        // Fallback sequence handles case-sensitive file systems and icons stored outside the Tokens folder.
        var texture = TryLoadTokenTexture("Common/Tokens/", symbol)
            ?? TryLoadTokenTexture("Common/", symbol);

        if (texture == null)
        {
            texture = Resources.Load<Texture>("Common/Tokens/UNKNOWN_TOKEN_" + platform.ToString().ToUpper());
        }

        _symbols[symbol] = texture;

        return texture;
    }

    public static Texture2D TextureFromColor(Color color)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        texture.SetPixels(new Color[] { color, color, color, color });
        texture.Apply();
        return texture;
    }
}
