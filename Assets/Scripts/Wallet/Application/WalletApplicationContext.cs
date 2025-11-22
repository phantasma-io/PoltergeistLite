using Poltergeist;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Aggregates UI-agnostic runtime state used by both legacy and modern UI layers.
    /// </summary>
    public sealed class WalletApplicationContext
    {
        private static WalletApplicationContext _instance;

        public static WalletApplicationContext Instance => _instance ??= new WalletApplicationContext();

        private WalletApplicationContext()
        {
            Navigation = new WalletNavigation();
            Messages = new WalletMessageQueue();
            Modals = new WalletModalContext();
            Data = new WalletDataProvider(() => AccountManager.Instance);
            NftSource = new WalletNftSource(() => AccountManager.Instance, new INftMetadataProvider[]
            {
                new TtrsNftMetadataProvider(),
                new GameNftMetadataProvider(),
                new DefaultNftMetadataProvider(() => AccountManager.Instance)
            });
            NftViewBuilder = new WalletNftViewBuilder();
            NftViewState = new WalletNftViewState();
            NftViewPresenter = new WalletNftPresenter(NftViewBuilder, NftViewState, NftSource, () => AccountManager.Instance);
            NftTransactions = new WalletNftTransactionBuilder(() => AccountManager.Instance);
            UiSignals = new WalletUiSignals(() => AccountManager.Instance);
            BalanceViewState = new WalletBalanceViewState();
            BalanceViewBuilder = new WalletBalanceViewBuilder();
            BalancePresenter = new WalletBalancePresenter(BalanceViewBuilder, BalanceViewState, Data, () => AccountManager.Instance);
            HistoryViewState = new WalletHistoryViewState();
            HistoryViewBuilder = new WalletHistoryViewBuilder();
            HistoryPresenter = new WalletHistoryPresenter(HistoryViewBuilder, HistoryViewState, Data, () => AccountManager.Instance);
        }

        public WalletNavigation Navigation { get; }

        public WalletMessageQueue Messages { get; }

        public WalletModalContext Modals { get; }

        public WalletDataProvider Data { get; }

        public WalletNftSource NftSource { get; }

        public WalletNftViewBuilder NftViewBuilder { get; }

        public WalletNftViewState NftViewState { get; }

        public WalletNftPresenter NftViewPresenter { get; }

        public WalletNftTransactionBuilder NftTransactions { get; }

        public WalletUiSignals UiSignals { get; }

        public WalletBalanceViewState BalanceViewState { get; }

        public WalletBalanceViewBuilder BalanceViewBuilder { get; }

        public WalletBalancePresenter BalancePresenter { get; }

        public WalletHistoryViewState HistoryViewState { get; }

        public WalletHistoryViewBuilder HistoryViewBuilder { get; }

        public WalletHistoryPresenter HistoryPresenter { get; }
    }
}
