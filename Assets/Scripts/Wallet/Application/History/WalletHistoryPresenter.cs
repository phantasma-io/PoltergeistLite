using System;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Coordinates history view building and refresh actions.
    /// </summary>
    public sealed class WalletHistoryPresenter
    {
        private readonly WalletHistoryViewBuilder builder;
        private readonly WalletHistoryViewState state;
        private readonly WalletDataProvider dataProvider;
        private readonly Func<AccountManager> accountProvider;

        public WalletHistoryPresenter(WalletHistoryViewBuilder builder, WalletHistoryViewState state, WalletDataProvider dataProvider, Func<AccountManager> accountProvider)
        {
            this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            this.accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public WalletHistoryViewState State => state;

        public WalletHistoryViewSnapshot BuildSnapshot()
        {
            return builder.Build(dataProvider);
        }

        public void Refresh(bool force)
        {
            var accountManager = accountProvider();
            accountManager?.RefreshHistory(force, accountManager.CurrentPlatform);
        }
    }
}
