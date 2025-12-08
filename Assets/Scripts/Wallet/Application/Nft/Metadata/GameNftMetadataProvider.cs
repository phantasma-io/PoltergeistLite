using System;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Metadata provider for GAME NFTs.
    /// </summary>
    public sealed class GameNftMetadataProvider : INftMetadataProvider
    {
        public bool CanHandle(string symbol)
        {
            return string.Equals(symbol, "GAME", StringComparison.OrdinalIgnoreCase);
        }

        public bool TryGet(string symbol, string tokenId, out NftMetadata metadata)
        {
            metadata = NftMetadata.Empty;
            if (!CanHandle(symbol) || string.IsNullOrEmpty(tokenId))
            {
                return false;
            }

            var item = GameStore.GetNft(tokenId);
            if (string.IsNullOrEmpty(item.ID))
            {
                return false;
            }

            var meta = item.meta;
            var name = meta?.name_english ?? string.Empty;
            var type = meta?.meta_type ?? item.series ?? string.Empty;
            var rarity = 0;
            var mintDate = item.parsed_rom.timestampDT();

            metadata = new NftMetadata(name, type, rarity, mintDate);
            return true;
        }
    }
}
