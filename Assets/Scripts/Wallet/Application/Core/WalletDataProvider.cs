using System;
using System.Collections.Generic;
using System.Linq;
using PhantasmaPhoenix.NFT.Extensions;
using Poltergeist;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Provides UI-agnostic snapshots of wallet data for both legacy and new UI layers.
    /// </summary>
    public sealed class WalletDataProvider
    {
        private readonly Func<AccountManager> _accountManagerProvider;

        public WalletDataProvider(Func<AccountManager> accountManagerProvider)
        {
            _accountManagerProvider = accountManagerProvider ?? throw new ArgumentNullException(nameof(accountManagerProvider));
        }

        public WalletBalancesModel GetBalancesSnapshot()
        {
            var accountManager = _accountManagerProvider();

            if (accountManager == null)
            {
                return new WalletBalancesModel(string.Empty, PlatformKind.None, true, Array.Empty<WalletBalanceEntry>(), "Account manager is not available yet.");
            }

            var accountName = accountManager.HasSelection ? accountManager.CurrentAccount.name : string.Empty;
            var state = accountManager.CurrentState;
            var isRefreshing = accountManager.BalanceRefreshing;
            var balanceError = accountManager.GetBalanceError(accountManager.CurrentPlatform);

            if (state == null)
            {
                var error = balanceError
                    ?? (accountManager.rpcAvailablePhantasma == 0
                    ? "Please check your internet connection. All Phantasma RPC servers are unavailable."
                    : "Temporary error, cannot display balances...");

                return new WalletBalancesModel(accountName, accountManager.CurrentPlatform, isRefreshing, Array.Empty<WalletBalanceEntry>(), error);
            }

            var balances = state.balances?.Select(b =>
            {
                var fiatWorth = accountManager.GetTokenWorth(b.Symbol, b.Available, b.Decimals);
                return new WalletBalanceEntry(b.Symbol, b.Available, b.Staked, b.Claimable, b.Chain, b.Decimals, b.Burnable, b.Fungible, b.Ids, fiatWorth);
            }).ToList() ?? new List<WalletBalanceEntry>();

            return new WalletBalancesModel(state.name, accountManager.CurrentPlatform, isRefreshing, balances, balanceError ?? string.Empty);
        }

        public WalletHistoryModel GetHistorySnapshot()
        {
            var accountManager = _accountManagerProvider();

            if (accountManager == null)
            {
                return new WalletHistoryModel(string.Empty, PlatformKind.None, true, Array.Empty<WalletHistoryItem>(), "Account manager is not available yet.");
            }

            var accountName = accountManager.HasSelection ? accountManager.CurrentAccount.name : string.Empty;
            var isRefreshing = accountManager.HistoryRefreshing;
            var state = accountManager.CurrentState;
            var history = accountManager.CurrentHistory;

            if (history == null || state == null)
            {
                var error = accountManager.rpcAvailablePhantasma == 0
                    ? "Please check your internet connection. All Phantasma RPC servers are unavailable."
                    : "Temporary error, cannot display history...";

                return new WalletHistoryModel(accountName, accountManager.CurrentPlatform, isRefreshing, Array.Empty<WalletHistoryItem>(), error);
            }

            var entries = history.Select(x => new WalletHistoryItem(x.hash, x.date, x.url)).ToList();

            return new WalletHistoryModel(state.name, accountManager.CurrentPlatform, isRefreshing, entries, string.Empty);
        }

        public WalletNftModel GetNftSnapshot(string symbol)
        {
            var accountManager = _accountManagerProvider();

            if (accountManager == null)
            {
                return new WalletNftModel(string.Empty, PlatformKind.None, symbol, true, Array.Empty<WalletNftItem>(), "Account manager is not available yet.");
            }

            var accountName = accountManager.HasSelection ? accountManager.CurrentAccount.name : string.Empty;
            var isRefreshing = accountManager.NftsRefreshing;
            var state = accountManager.CurrentState;
            var nfts = accountManager.CurrentNfts;

            if (nfts == null || state == null)
            {
                var error = accountManager.rpcAvailablePhantasma == 0
                    ? "Please check your internet connection. All Phantasma RPC servers are unavailable."
                    : "Loading NFTs...";

                return new WalletNftModel(accountName, accountManager.CurrentPlatform, symbol, isRefreshing, Array.Empty<WalletNftItem>(), error);
            }

            var items = nfts.Select(x =>
            {
                // Basic metadata; detailed parsing stays in UI layer for now.
                if (string.Equals(symbol, "TTRS", StringComparison.OrdinalIgnoreCase))
                {
                    var item = global::TtrsStore.GetNft(x.Id);
                    var name = item.item_info.name_english ?? string.Empty;
                    var desc = item.mint == 0 ? string.Empty : $"Mint #{item.mint} {item.timestampDT():dd.MM.yyyy HH:mm:ss}";
                    var image = item.img ?? string.Empty;
                    return new WalletNftItem(x.Id, name, desc, image);
                }

                if (string.Equals(symbol, "GAME", StringComparison.OrdinalIgnoreCase))
                {
                    var item = global::GameStore.GetNft(x.Id);
                    var name = item.meta?.name_english ?? string.Empty;
                    var desc = item.mint == 0 ? string.Empty : $"Mint #{item.mint} {item.parsed_rom.timestampDT():dd.MM.yyyy HH:mm:ss}";
                    var image = item.parsed_rom.img_url ?? string.Empty;
                    return new WalletNftItem(x.Id, name, desc, image);
                }

                var token = accountManager.GetNft(x.Id);
                var rom = accountManager.GetNftRom(x.Id);
                var date = rom?.GetDate();

                var nftName = token?.GetPropertyValue("Name") ?? string.Empty;
                var nftDescription = token?.GetPropertyValue("Description") ?? string.Empty;
                var imageUrl = token?.GetPropertyValue("ImageURL") ?? string.Empty;

                if (string.IsNullOrEmpty(nftDescription) && date.HasValue && date.Value != DateTime.MinValue)
                {
                    nftDescription = date.Value.ToString("dd.MM.yyyy HH:mm:ss");
                }

                return new WalletNftItem(x.Id, nftName, nftDescription, imageUrl);
            }).ToList();

            return new WalletNftModel(state.name, accountManager.CurrentPlatform, symbol, isRefreshing, items, string.Empty);
        }
    }
}
