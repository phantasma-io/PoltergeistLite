using System;
using System.Collections.Generic;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Coordinates NFT view state and snapshot building for UI layers.
    /// </summary>
    public sealed class WalletNftPresenter
    {
        private readonly WalletNftViewBuilder builder;
        private readonly WalletNftViewState state;
        private readonly WalletNftSource nftSource;
        private readonly Func<AccountManager> accountProvider;

        public WalletNftPresenter(WalletNftViewBuilder builder, WalletNftViewState state, WalletNftSource nftSource, Func<AccountManager> accountProvider)
        {
            this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.nftSource = nftSource ?? throw new ArgumentNullException(nameof(nftSource));
            this.accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public WalletNftViewState State => state;

        public WalletNftViewSnapshot BuildSnapshot(string symbol)
        {
            return builder.Build(nftSource, symbol, state);
        }

        public bool UpdateFilters(string filterName, int filterTypeIndex, string filterType, int filterRarity, int filterMinted)
        {
            return state.UpdateFilters(filterName, filterTypeIndex, filterType, filterRarity, filterMinted);
        }

        public void Refresh(string symbol, bool force)
        {
            var accountManager = accountProvider();
            accountManager?.RefreshNft(force, symbol);
        }

        public void ResetSorting()
        {
            var accountManager = accountProvider();
            accountManager?.ResetNftsSorting();
        }

        public void ResetFiltersAndPagination()
        {
            state.ResetFilters();
            state.ResetPagination();
        }

        public void ClearSelection()
        {
            state.ClearSelection();
        }

        public void Select(IEnumerable<string> ids)
        {
            state.Select(ids);
        }

        public void InvertSelection(IEnumerable<string> ids)
        {
            state.InvertSelection(ids);
        }

        public bool ToggleSelection(string id)
        {
            return state.ToggleSelection(id);
        }

        public bool IsSelected(string id)
        {
            return state.IsSelected(id);
        }

        public void PruneSelection(IEnumerable<string> validIds)
        {
            state.PruneSelection(validIds);
        }

        public IReadOnlyList<string> SelectionSnapshot()
        {
            return state.SelectionSnapshot();
        }
    }
}
