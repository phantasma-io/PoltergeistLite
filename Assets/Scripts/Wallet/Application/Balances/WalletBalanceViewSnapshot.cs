using System.Collections.Generic;
using PhantasmaPhoenix.Protocol;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// UI-ready snapshot of balances.
    /// </summary>
    public sealed class WalletBalanceViewSnapshot
    {
        public WalletBalanceViewSnapshot(string accountName, PlatformKind platform, bool isRefreshing, IReadOnlyList<WalletBalanceEntry> balances, string errorMessage)
        {
            AccountName = accountName;
            Platform = platform;
            IsRefreshing = isRefreshing;
            Balances = balances;
            ErrorMessage = errorMessage;
        }

        public string AccountName { get; }
        public PlatformKind Platform { get; }
        public bool IsRefreshing { get; }
        public IReadOnlyList<WalletBalanceEntry> Balances { get; }
        public string ErrorMessage { get; }
        public bool HasError => !IsRefreshing && !string.IsNullOrEmpty(ErrorMessage);
    }
}
