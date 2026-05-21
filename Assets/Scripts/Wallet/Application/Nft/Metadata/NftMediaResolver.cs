using System;
using PhantasmaPhoenix.NFT.Extensions;
using PhantasmaPhoenix.RPC.Models;

namespace Poltergeist.Wallet
{
    public enum NftMediaKind
    {
        None,
        Image,
        Video,
        Audio
    }

    /// <summary>
    /// Normalized NFT media reference for UITK.
    /// It separates the raw metadata value from the externally openable URL so views do not need
    /// to understand custom schemes such as ipfs-video:// or ipfs-vid://.
    /// </summary>
    public readonly struct NftMediaReference
    {
        public NftMediaReference(NftMediaKind kind, string source, string openUrl, bool isInlineImage)
        {
            Kind = kind;
            Source = source ?? string.Empty;
            OpenUrl = openUrl ?? string.Empty;
            IsInlineImage = isInlineImage;
        }

        public NftMediaKind Kind { get; }
        public string Source { get; }
        public string OpenUrl { get; }
        public bool IsInlineImage { get; }

        // Any resolved http/https media can be opened externally, except inline data images that intentionally
        // stay inside the wallet process and do not expose a browser-openable URL.
        public bool CanOpenExternally => !IsInlineImage && Kind != NftMediaKind.None && !string.IsNullOrWhiteSpace(OpenUrl);
    }

    public static class NftMediaResolver
    {
        private const string IpfsGatewayPrefix = "https://gateway.ipfs.io/ipfs/";

        public static NftMediaReference Resolve(string symbol, TokenDataResult token)
        {
            if (token == null)
            {
                return default;
            }

            if (string.Equals(symbol, "TTRS", StringComparison.OrdinalIgnoreCase))
            {
                var item = global::TtrsStore.GetNft(token.Id);
                // TTRS historically renders from the store-level "img" field.
                // Switching to nested item_info.image_url regressed existing NFTs because it is not the exact
                // value the UITK path used before and can be empty while the top-level image remains valid.
                var ttrsImage = FirstNonEmpty(item.img, item.item_info.image_url);
                if (!string.IsNullOrWhiteSpace(ttrsImage))
                {
                    return ResolveSource(ttrsImage);
                }
            }

            if (string.Equals(symbol, "GAME", StringComparison.OrdinalIgnoreCase))
            {
                var item = global::GameStore.GetNft(token.Id);
                if (!string.IsNullOrWhiteSpace(item.ID) && !string.IsNullOrWhiteSpace(item.parsed_rom.img_url))
                {
                    return ResolveSource(item.parsed_rom.img_url);
                }
            }

            var videoUrl = FirstNonEmpty(
                token.GetPropertyValue("VideoURL"),
                token.GetPropertyValue("Video"),
                token.GetPropertyValue("video_url"));
            if (!string.IsNullOrWhiteSpace(videoUrl))
            {
                return ResolveSource(videoUrl, NftMediaKind.Video);
            }

            var audioUrl = FirstNonEmpty(
                token.GetPropertyValue("AudioURL"),
                token.GetPropertyValue("Audio"),
                token.GetPropertyValue("audio_url"));
            if (!string.IsNullOrWhiteSpace(audioUrl))
            {
                return ResolveSource(audioUrl, NftMediaKind.Audio);
            }

            var imageUrl = FirstNonEmpty(
                token.GetPropertyValue("ImageURL"),
                token.GetPropertyValue("Image"),
                token.GetPropertyValue("image_url"));
            return ResolveSource(imageUrl, NftMediaKind.Image);
        }

        /// <summary>
        /// Resolves the visual preview media used inside UITK cards/detail views.
        /// Preview intentionally prefers static image sources when both poster/image and video are present
        /// to preserve historical wallet behavior and avoid replacing already-working thumbnails with placeholders.
        /// </summary>
        public static NftMediaReference ResolvePreview(string symbol, TokenDataResult token)
        {
            if (token == null)
            {
                return default;
            }

            if (string.Equals(symbol, "TTRS", StringComparison.OrdinalIgnoreCase))
            {
                var item = global::TtrsStore.GetNft(token.Id);
                var ttrsImage = FirstNonEmpty(item.img, item.item_info.image_url);
                if (!string.IsNullOrWhiteSpace(ttrsImage))
                {
                    return ResolveSource(ttrsImage, NftMediaKind.Image);
                }
            }

            if (string.Equals(symbol, "GAME", StringComparison.OrdinalIgnoreCase))
            {
                var item = global::GameStore.GetNft(token.Id);
                if (!string.IsNullOrWhiteSpace(item.ID) && !string.IsNullOrWhiteSpace(item.parsed_rom.img_url))
                {
                    return ResolveSource(item.parsed_rom.img_url, NftMediaKind.Image);
                }
            }

            var imageUrl = FirstNonEmpty(
                token.GetPropertyValue("ImageURL"),
                token.GetPropertyValue("Image"),
                token.GetPropertyValue("image_url"));
            var videoUrl = FirstNonEmpty(
                token.GetPropertyValue("VideoURL"),
                token.GetPropertyValue("Video"),
                token.GetPropertyValue("video_url"));
            var audioUrl = FirstNonEmpty(
                token.GetPropertyValue("AudioURL"),
                token.GetPropertyValue("Audio"),
                token.GetPropertyValue("audio_url"));

            return ResolvePreviewSource(imageUrl, videoUrl, audioUrl);
        }

        /// <summary>
        /// Pure helper used by tests and by preview resolution.
        /// Image wins over video/audio to keep existing visible thumbnails stable.
        /// </summary>
        public static NftMediaReference ResolvePreviewSource(string imageSource, string videoSource = null, string audioSource = null)
        {
            if (!string.IsNullOrWhiteSpace(imageSource))
            {
                return ResolveSource(imageSource, NftMediaKind.Image);
            }

            if (!string.IsNullOrWhiteSpace(videoSource))
            {
                return ResolveSource(videoSource, NftMediaKind.Video);
            }

            if (!string.IsNullOrWhiteSpace(audioSource))
            {
                return ResolveSource(audioSource, NftMediaKind.Audio);
            }

            return default;
        }

        /// <summary>
        /// Public for tests and for any future callers that already have the raw media source.
        /// </summary>
        public static NftMediaReference ResolveSource(string source, NftMediaKind declaredKind = NftMediaKind.None)
        {
            var trimmed = source?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return default;
            }

            var kind = DetectKind(trimmed, declaredKind);
            var isInlineImage = kind == NftMediaKind.Image && trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
            var openUrl = NormalizeOpenUrl(trimmed);

            return new NftMediaReference(kind, trimmed, openUrl, isInlineImage);
        }

        private static NftMediaKind DetectKind(string source, NftMediaKind declaredKind)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return NftMediaKind.None;
            }

            if (source.StartsWith("ipfs-video://", StringComparison.OrdinalIgnoreCase)
                || source.StartsWith("ipfs-vid://", StringComparison.OrdinalIgnoreCase))
            {
                return NftMediaKind.Video;
            }

            if (source.StartsWith("ipfs-audio://", StringComparison.OrdinalIgnoreCase))
            {
                return NftMediaKind.Audio;
            }

            if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                return NftMediaKind.Image;
            }

            if (declaredKind != NftMediaKind.None)
            {
                return declaredKind;
            }

            if (HasKnownExtension(source, ".mp4", ".webm", ".m4v", ".mov"))
            {
                return NftMediaKind.Video;
            }

            if (HasKnownExtension(source, ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac"))
            {
                return NftMediaKind.Audio;
            }

            return NftMediaKind.Image;
        }

        private static bool HasKnownExtension(string source, params string[] extensions)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            foreach (var extension in extensions)
            {
                if (source.IndexOf(extension, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeOpenUrl(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            var trimmed = source.Trim();
            if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (trimmed.StartsWith("ipfs-video://", StringComparison.OrdinalIgnoreCase))
            {
                return ToIpfsGatewayUrl(trimmed.Substring("ipfs-video://".Length));
            }

            // Blood Rune Cards use the ipfs-vid:// scheme in VideoURL metadata.
            if (trimmed.StartsWith("ipfs-vid://", StringComparison.OrdinalIgnoreCase))
            {
                return ToIpfsGatewayUrl(trimmed.Substring("ipfs-vid://".Length));
            }

            if (trimmed.StartsWith("ipfs-audio://", StringComparison.OrdinalIgnoreCase))
            {
                return ToIpfsGatewayUrl(trimmed.Substring("ipfs-audio://".Length));
            }

            if (trimmed.StartsWith("ipfs://", StringComparison.OrdinalIgnoreCase))
            {
                return ToIpfsGatewayUrl(trimmed.Substring("ipfs://".Length));
            }

            if (IsLikelyIpfsCid(trimmed))
            {
                return IpfsGatewayPrefix + trimmed;
            }

            if (trimmed.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                trimmed = "https://" + trimmed.TrimStart('/');
            }

            return IsSafeHttpUrl(trimmed) ? trimmed : string.Empty;
        }

        private static string ToIpfsGatewayUrl(string value)
        {
            var path = value?.Trim().TrimStart('/') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (path.StartsWith("ipfs/", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring("ipfs/".Length);
            }

            return IpfsGatewayPrefix + path;
        }

        private static bool IsSafeHttpUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsLikelyIpfsCid(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains("/") || value.Contains("://"))
            {
                return false;
            }

            return value.StartsWith("Qm", StringComparison.Ordinal)
                || value.StartsWith("bafy", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("baga", StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }
    }
}
