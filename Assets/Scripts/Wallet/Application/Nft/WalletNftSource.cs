using System;
using System.Collections.Generic;
using System.Linq;
using PhantasmaPhoenix.NFT;
using PhantasmaPhoenix.NFT.Extensions;
using PhantasmaPhoenix.RPC.Models;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Default NFT source backed by AccountManager and pluggable metadata providers.
    /// </summary>
    public sealed class WalletNftSource
    {
        private readonly Func<AccountManager> _accountProvider;
        private readonly List<INftMetadataProvider> _metadataProviders;

        public WalletNftSource(Func<AccountManager> accountProvider, IEnumerable<INftMetadataProvider> metadataProviders)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
            _metadataProviders = metadataProviders?.ToList() ?? new List<INftMetadataProvider>();
        }

        private AccountManager Account => _accountProvider();

        public bool IsRefreshing => Account?.NftsRefreshing ?? true;

        public bool IsRefreshingForSymbol(string symbol)
        {
            return Account?.IsNftRefreshing(symbol) ?? true;
        }

        public int RpcAvailablePhantasma => Account?.rpcAvailablePhantasma ?? 0;

        public IReadOnlyList<TokenDataResult> CurrentNfts => Account?.CurrentNfts;

        public IReadOnlyList<TokenDataResult> GetNfts(string symbol)
        {
            return Account?.GetNfts(symbol);
        }

        public void SortTtrsNfts(string symbol)
        {
            Account?.SortTtrsNfts(symbol);
        }

        public TokenDataResult GetNft(string id)
        {
            return Account != null ? Account.GetNft(id) : default;
        }

        public TokenDataResult GetNft(string symbol, string id)
        {
            return Account != null ? Account.GetNft(symbol, id) : default;
        }

        public IRom GetNftRom(string id)
        {
            return Account?.GetNftRom(id);
        }

        public IRom GetNftRom(string symbol, string id)
        {
            return Account?.GetNftRom(symbol, id);
        }

        public bool TryGetMetadata(string symbol, string tokenId, out NftMetadata metadata)
        {
            foreach (var provider in _metadataProviders)
            {
                if (!provider.CanHandle(symbol))
                {
                    continue;
                }

                if (provider.TryGet(symbol, tokenId, out metadata))
                {
                    return true;
                }
            }

            // Fallback: keep item visible even if specific metadata is missing.
            metadata = new NftMetadata(tokenId, string.Empty, 0, DateTime.MinValue);
            return true;
        }
    }
}
