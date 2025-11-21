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
        public WalletNftViewSnapshot Build(AccountManager accountManager, string symbol, string filterName, string filterType, int filterRarity, int filterMinted, int pageSize, int pageNumber)
        {
            if (accountManager == null)
            {
                return new WalletNftViewSnapshot(true, true, "Account manager is not available yet.", 0, 0, 0, Array.Empty<TokenDataResult>(), Array.Empty<string>());
            }

            var nfts = accountManager.CurrentNfts;
            var isRefreshing = accountManager.NftsRefreshing;

            if (nfts == null)
            {
                var error = accountManager.rpcAvailablePhantasma == 0
                    ? "Please check your internet connection. All Phantasma RPC servers are unavailable."
                    : "Loading NFTs...";

                return new WalletNftViewSnapshot(isRefreshing, true, error, 0, 0, 0, Array.Empty<TokenDataResult>(), Array.Empty<string>());
            }

            var filtered = new List<TokenDataResult>();

            foreach (var x in nfts)
            {
                if (string.Equals(symbol, "TTRS", StringComparison.OrdinalIgnoreCase))
                {
                    var item = TtrsStore.GetNft(x.Id);

                    if (!string.IsNullOrEmpty(filterName) && !item.item_info.name_english.ToUpper().Contains(filterName.ToUpper()))
                    {
                        continue;
                    }

                    if (filterType != "All" && item.item_info.display_type_english != filterType)
                    {
                        continue;
                    }

                    if (filterRarity != (int)ttrsNftRarity.All && (int)item.item_info.rarity != filterRarity)
                    {
                        continue;
                    }

                    if (!MintedFilterMatch(filterMinted, item.timestampDT()))
                    {
                        continue;
                    }

                    filtered.Add(x);
                }
                else if (string.Equals(symbol, "GAME", StringComparison.OrdinalIgnoreCase))
                {
                    var item = GameStore.GetNft(x.Id);

                    var nameEng = item.meta?.name_english ?? string.Empty;
                    if (!string.IsNullOrEmpty(filterName) && !nameEng.ToUpper().Contains(filterName.ToUpper()))
                    {
                        continue;
                    }

                    if (!MintedFilterMatch(filterMinted, item.parsed_rom.timestampDT()))
                    {
                        continue;
                    }

                    filtered.Add(x);
                }
                else
                {
                    var rom = accountManager.GetNftRom(x.Id);
                    if (rom == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(filterName) && !rom.GetName().ToUpper().Contains(filterName.ToUpper()))
                    {
                        continue;
                    }

                    if (!MintedFilterMatch(filterMinted, rom.GetDate()))
                    {
                        continue;
                    }

                    filtered.Add(x);
                }
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
