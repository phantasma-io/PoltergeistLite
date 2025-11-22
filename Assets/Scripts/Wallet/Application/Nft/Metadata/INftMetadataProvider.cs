namespace Poltergeist.Wallet
{
    /// <summary>
    /// Provides normalized NFT metadata for specific token symbols.
    /// </summary>
    public interface INftMetadataProvider
    {
        bool CanHandle(string symbol);
        bool TryGet(string symbol, string tokenId, out NftMetadata metadata);
    }
}
