using System;

namespace Poltergeist
{
    public class RefreshStatus
    {
        // Balance
        public bool BalanceRefreshing;
        public DateTime LastBalanceRefresh;
        public Action BalanceRefreshCallback;
        public string BalanceError;
        // History
        public bool HistoryRefreshing;
        public DateTime LastHistoryRefresh;
        public bool NftsRefreshing;

        public override string ToString()
        {
            return $"BalanceRefreshing: {BalanceRefreshing}, LastBalanceRefresh: {LastBalanceRefresh}, BalanceError: {BalanceError}, HistoryRefreshing: {HistoryRefreshing}, LastHistoryRefresh: {LastHistoryRefresh}";
        }
    }
}
