using System.Collections.Generic;
using PhantasmaPhoenix.Protocol;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// UI-ready snapshot of history entries.
    /// </summary>
    public sealed class WalletHistoryViewSnapshot
    {
        public WalletHistoryViewSnapshot(string accountName, PlatformKind platform, bool isRefreshing, IReadOnlyList<WalletHistoryItem> entries, string errorMessage)
        {
            AccountName = accountName;
            Platform = platform;
            IsRefreshing = isRefreshing;
            Entries = entries;
            ErrorMessage = errorMessage;
        }

        public string AccountName { get; }
        public PlatformKind Platform { get; }
        public bool IsRefreshing { get; }
        public IReadOnlyList<WalletHistoryItem> Entries { get; }
        public string ErrorMessage { get; }
        public bool HasError => !IsRefreshing && !string.IsNullOrEmpty(ErrorMessage);
    }
}
