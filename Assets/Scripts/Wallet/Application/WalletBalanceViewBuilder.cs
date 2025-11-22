namespace Poltergeist.Wallet
{
    /// <summary>
    /// Builds balance view snapshots from data provider.
    /// </summary>
    public sealed class WalletBalanceViewBuilder
    {
        public WalletBalanceViewSnapshot Build(WalletDataProvider dataProvider)
        {
            var model = dataProvider.GetBalancesSnapshot();

            return new WalletBalanceViewSnapshot(model.AccountName, model.Platform, model.IsRefreshing, model.Balances, model.ErrorMessage);
        }
    }
}
