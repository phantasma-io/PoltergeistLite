using System;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Metadata provider for TTRS NFTs.
    /// </summary>
    public sealed class TtrsNftMetadataProvider : INftMetadataProvider
    {
        public bool CanHandle(string symbol)
        {
            return string.Equals(symbol, "TTRS", StringComparison.OrdinalIgnoreCase);
        }

        public bool TryGet(string symbol, string tokenId, out NftMetadata metadata)
        {
            metadata = NftMetadata.Empty;
            if (!CanHandle(symbol) || string.IsNullOrEmpty(tokenId))
            {
                return false;
            }

            var item = TtrsStore.GetNft(tokenId);
            if (string.IsNullOrEmpty(item.id))
            {
                return false;
            }

            var name = item.item_info.name_english ?? string.Empty;
            var type = item.item_info.display_type_english ?? string.Empty;
            var rarity = (int)item.item_info.rarity;
            var mintDate = item.timestampDT();

            metadata = new NftMetadata(name, type, rarity, mintDate);
            return true;
        }
    }
}
