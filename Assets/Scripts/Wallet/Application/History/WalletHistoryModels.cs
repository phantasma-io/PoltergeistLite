using System;
using System.Collections.Generic;
using PhantasmaPhoenix.Protocol;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Snapshot of transaction history for UI binding.
    /// </summary>
    public sealed class WalletHistoryModel
    {
        public WalletHistoryModel(string accountName, PlatformKind platform, bool isRefreshing, IReadOnlyList<WalletHistoryItem> entries, string errorMessage)
        {
            AccountName = accountName ?? string.Empty;
            Platform = platform;
            IsRefreshing = isRefreshing;
            Entries = entries ?? Array.Empty<WalletHistoryItem>();
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public string AccountName { get; }
        public PlatformKind Platform { get; }
        public bool IsRefreshing { get; }
        public IReadOnlyList<WalletHistoryItem> Entries { get; }
        public string ErrorMessage { get; }
        public bool HasError => !IsRefreshing && !string.IsNullOrEmpty(ErrorMessage);
    }

    /// <summary>
    /// Read-only representation of a history entry.
    /// </summary>
    public sealed class WalletHistoryItem
    {
        public WalletHistoryItem(string hash, DateTime date, string url)
        {
            Hash = hash ?? string.Empty;
            Date = date;
            Url = url ?? string.Empty;
        }

        public string Hash { get; }
        public DateTime Date { get; }
        public string Url { get; }
    }
}

