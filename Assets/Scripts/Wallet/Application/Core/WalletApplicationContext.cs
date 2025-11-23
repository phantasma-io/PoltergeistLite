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
            AccountHintsService = new WalletAccountHintsService(() => AccountManager.Instance);
            ViewState = new WalletViewState();
            QrCodeGenerator = new WalletQrCodeGenerator();
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
            FeeRequirement = new WalletFeeRequirement(() => AccountManager.Instance);
            FeeService = new WalletFeeService(() => AccountManager.Instance);
            TransferService = new WalletTransferService(() => AccountManager.Instance, FeeRequirement);
            StakeService = new WalletStakeService(() => AccountManager.Instance);
            BurnService = new WalletBurnService(() => AccountManager.Instance, NftTransactions, FeeRequirement);
            NftTransferService = new WalletNftTransferService(() => AccountManager.Instance, NftTransactions, FeeRequirement);
            AccountAdminService = new WalletAccountAdminService(() => AccountManager.Instance);
            AmountValidator = new WalletAmountValidator(() => AccountManager.Instance);
            AuthService = new WalletAuthService(() => AccountManager.Instance);
            SettingsService = new WalletSettingsService(() => AccountManager.Instance);
            SettingsViewState = new WalletSettingsViewState();
            SettingsViewBuilder = new WalletSettingsViewBuilder();
            SettingsPresenter = new WalletSettingsPresenter(SettingsViewBuilder, SettingsService, () => AccountManager.Instance, null, SettingsViewState);

            if (AccountManager.Instance == null)
            {
                PhantasmaPhoenix.Unity.Core.Logging.Log.WriteWarning("[Startup] WalletApplicationContext constructed before AccountManager.Instance was ready. Check initialization order.");
            }
        }

        public WalletNavigation Navigation { get; }

        public WalletMessageQueue Messages { get; }

        public WalletModalContext Modals { get; }

        public WalletDataProvider Data { get; }

        public WalletAccountHintsService AccountHintsService { get; }

        public WalletViewState ViewState { get; }

        public WalletQrCodeGenerator QrCodeGenerator { get; }

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

        public WalletFeeService FeeService { get; }

        public WalletTransferService TransferService { get; }

        public WalletStakeService StakeService { get; }

        public WalletBurnService BurnService { get; }

        public WalletNftTransferService NftTransferService { get; }

        public WalletAccountAdminService AccountAdminService { get; }

        public WalletAmountValidator AmountValidator { get; }

        public WalletAuthService AuthService { get; }

        public WalletFeeRequirement FeeRequirement { get; }

        public WalletSettingsService SettingsService { get; }

        public WalletSettingsViewState SettingsViewState { get; }

        public WalletSettingsViewBuilder SettingsViewBuilder { get; }

        public WalletSettingsPresenter SettingsPresenter { get; }
    }
}
