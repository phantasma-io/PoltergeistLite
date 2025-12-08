using System;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Coordinates balance view building and refresh actions.
    /// </summary>
    public sealed class WalletBalancePresenter
    {
        private readonly WalletBalanceViewBuilder builder;
        private readonly WalletBalanceViewState state;
        private readonly WalletDataProvider dataProvider;
        private readonly Func<AccountManager> accountProvider;

        public WalletBalancePresenter(WalletBalanceViewBuilder builder, WalletBalanceViewState state, WalletDataProvider dataProvider, Func<AccountManager> accountProvider)
        {
            this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            this.accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public WalletBalanceViewState State => state;

        public WalletBalanceViewSnapshot BuildSnapshot()
        {
            return builder.Build(dataProvider);
        }

        public void Refresh(bool force)
        {
            var accountManager = accountProvider();
            accountManager?.RefreshBalances(force, accountManager.CurrentPlatform);
        }
    }
}
