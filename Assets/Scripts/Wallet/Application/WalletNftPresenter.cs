using System;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Coordinates NFT view state and snapshot building for UI layers.
    /// </summary>
    public sealed class WalletNftPresenter
    {
        private readonly WalletNftViewBuilder builder;
        private readonly WalletNftViewState state;
        private readonly Func<AccountManager> accountProvider;

        public WalletNftPresenter(WalletNftViewBuilder builder, WalletNftViewState state, Func<AccountManager> accountProvider)
        {
            this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public WalletNftViewState State => state;

        public WalletNftViewSnapshot BuildSnapshot(string symbol)
        {
            var accountManager = accountProvider();
            return builder.Build(accountManager, symbol, state);
        }

        public bool UpdateFilters(string filterName, int filterTypeIndex, string filterType, int filterRarity, int filterMinted)
        {
            return state.UpdateFilters(filterName, filterTypeIndex, filterType, filterRarity, filterMinted);
        }

        public void ResetFiltersAndPagination()
        {
            state.ResetFilters();
            state.ResetPagination();
        }
    }
}

