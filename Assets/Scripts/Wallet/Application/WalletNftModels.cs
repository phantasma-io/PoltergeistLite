using System;
using System.Collections.Generic;
using PhantasmaPhoenix.Protocol;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Snapshot of NFT list for UI binding (metadata kept minimal for now).
    /// </summary>
    public sealed class WalletNftModel
    {
        public WalletNftModel(string accountName, PlatformKind platform, string symbol, bool isRefreshing, IReadOnlyList<WalletNftItem> items, string errorMessage)
        {
            AccountName = accountName ?? string.Empty;
            Platform = platform;
            Symbol = symbol ?? string.Empty;
            IsRefreshing = isRefreshing;
            Items = items ?? Array.Empty<WalletNftItem>();
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public string AccountName { get; }
        public PlatformKind Platform { get; }
        public string Symbol { get; }
        public bool IsRefreshing { get; }
        public IReadOnlyList<WalletNftItem> Items { get; }
        public string ErrorMessage { get; }
        public bool HasError => !IsRefreshing && !string.IsNullOrEmpty(ErrorMessage);
    }

    /// <summary>
    /// Read-only representation of a single NFT entry. Metadata will be expanded incrementally.
    /// </summary>
    public sealed class WalletNftItem
    {
        public WalletNftItem(string id, string name, string description, string imageUrl)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            ImageUrl = imageUrl ?? string.Empty;
        }

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string ImageUrl { get; }
    }
}

