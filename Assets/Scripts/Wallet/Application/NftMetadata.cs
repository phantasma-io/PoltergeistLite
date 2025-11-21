using System;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Normalized NFT metadata used for filtering and display.
    /// </summary>
    public readonly struct NftMetadata
    {
        public NftMetadata(string name, string type, int rarity, DateTime mintDate)
        {
            Name = name ?? string.Empty;
            Type = type ?? string.Empty;
            Rarity = rarity;
            MintDate = mintDate;
            HasValue = true;
        }

        public string Name { get; }
        public string Type { get; }
        public int Rarity { get; }
        public DateTime MintDate { get; }
        public bool HasValue { get; }

        public static NftMetadata Empty => new NftMetadata();
    }
}
