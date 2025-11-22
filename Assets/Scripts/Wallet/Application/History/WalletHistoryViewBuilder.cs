namespace Poltergeist.Wallet
{
    /// <summary>
    /// Builds history view snapshots from data provider.
    /// </summary>
    public sealed class WalletHistoryViewBuilder
    {
        public WalletHistoryViewSnapshot Build(WalletDataProvider dataProvider)
        {
            var model = dataProvider.GetHistorySnapshot();
            return new WalletHistoryViewSnapshot(model.AccountName, model.Platform, model.IsRefreshing, model.Entries, model.ErrorMessage);
        }
    }
}
