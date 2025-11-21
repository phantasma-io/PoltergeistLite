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
        }

        public WalletNavigation Navigation { get; }

        public WalletMessageQueue Messages { get; }

        public WalletModalContext Modals { get; }
    }
}
