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
            NftViewBuilder = new WalletNftViewBuilder();
            NftViewState = new WalletNftViewState();
            NftViewPresenter = new WalletNftPresenter(NftViewBuilder, NftViewState, () => AccountManager.Instance);
        }

        public WalletNavigation Navigation { get; }

        public WalletMessageQueue Messages { get; }

        public WalletModalContext Modals { get; }

        public WalletDataProvider Data { get; }

        public WalletNftViewBuilder NftViewBuilder { get; }

        public WalletNftViewState NftViewState { get; }

        public WalletNftPresenter NftViewPresenter { get; }
    }
}
