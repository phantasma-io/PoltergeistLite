using System;
using System.Collections.Generic;
using System.Linq;
using PhantasmaPhoenix.RPC.Models;
using Poltergeist;
using Poltergeist.Wallet;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Builds filtered/paged NFT view snapshots reused by both legacy and new UI layers.
    /// </summary>
    public sealed class WalletNftViewBuilder
    {
        public WalletNftViewSnapshot Build(WalletNftSource source, string symbol, WalletNftViewState viewState)
        {
            if (viewState == null)
            {
                viewState = new WalletNftViewState();
            }

            return Build(source, symbol, viewState.FilterName, viewState.FilterType, viewState.FilterRarity, viewState.FilterMinted, viewState.PageSize, viewState.PageNumber);
        }

        public WalletNftViewSnapshot Build(WalletNftSource source, string symbol, string filterName, string filterType, int filterRarity, int filterMinted, int pageSize, int pageNumber)
        {
            if (source == null)
            {
                return new WalletNftViewSnapshot(true, true, "NFT source is not available yet.", 0, 0, 0, Array.Empty<TokenDataResult>(), Array.Empty<string>());
            }

            pageSize = Math.Max(1, pageSize);

            var nfts = source.CurrentNfts;
            var isRefreshing = source.IsRefreshing;

            if (nfts == null)
            {
                var error = source.RpcAvailablePhantasma == 0
                    ? "Please check your internet connection. All Phantasma RPC servers are unavailable."
                    : "Loading NFTs...";

                return new WalletNftViewSnapshot(isRefreshing, true, error, 0, 0, 0, Array.Empty<TokenDataResult>(), Array.Empty<string>());
            }

            source.SortTtrsNfts(symbol);
            nfts = source.CurrentNfts ?? nfts;

            var filtered = new List<TokenDataResult>();

            foreach (var x in nfts)
            {
                if (!source.TryGetMetadata(symbol, x.Id, out var meta) || !meta.HasValue)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(filterName) && !meta.Name.ToUpper().Contains(filterName.ToUpper()))
                {
                    continue;
                }

                if (filterType != "All" && !string.IsNullOrEmpty(filterType) && !string.Equals(meta.Type, filterType, StringComparison.Ordinal))
                {
                    continue;
                }

                if (filterRarity != (int)ttrsNftRarity.All && meta.Rarity != filterRarity)
                {
                    continue;
                }

                if (!MintedFilterMatch(filterMinted, meta.MintDate))
                {
                    continue;
                }

                filtered.Add(x);
            }

            var total = filtered.Count;
            var pageCount = total == 0 ? 0 : total / pageSize + (total % pageSize > 0 ? 1 : 0);
            var clampedPage = pageCount == 0 ? 0 : Math.Max(0, Math.Min(pageCount - 1, pageNumber));

            var start = pageSize * clampedPage;
            var end = Math.Min(pageSize * (clampedPage + 1), total);
            var pageIds = filtered.Skip(start).Take(end - start).Select(x => x.Id).ToList();

            return new WalletNftViewSnapshot(isRefreshing, false, string.Empty, total, clampedPage, pageCount, filtered, pageIds);
        }

        private bool MintedFilterMatch(int filterMinted, DateTime date)
        {
            if (filterMinted == (int)nftMinted.All)
            {
                return true;
            }

            if (filterMinted == (int)nftMinted.Last_15_Mins && DateTime.Compare(date, DateTime.Now.AddMinutes(-15)) >= 0)
            {
                return true;
            }

            if (filterMinted == (int)nftMinted.Last_Hour && DateTime.Compare(date, DateTime.Now.AddHours(-1)) >= 0)
            {
                return true;
            }

            if (filterMinted == (int)nftMinted.Last_24_Hours && DateTime.Compare(date, DateTime.Now.AddDays(-1)) >= 0)
            {
                return true;
            }

            if (filterMinted == (int)nftMinted.Last_Week && DateTime.Compare(date, DateTime.Now.AddDays(-7)) >= 0)
            {
                return true;
            }

            if (filterMinted == (int)nftMinted.Last_Month && DateTime.Compare(date, DateTime.Now.AddMonths(-1)) >= 0)
            {
                return true;
            }

            return false;
        }
    }
}
