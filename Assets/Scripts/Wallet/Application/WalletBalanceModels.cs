using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PhantasmaPhoenix.Protocol;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Snapshot of balances for UI binding (UI agnostic).
    /// </summary>
    public sealed class WalletBalancesModel
    {
        public WalletBalancesModel(string accountName, PlatformKind platform, bool isRefreshing, IReadOnlyList<WalletBalanceEntry> balances, string errorMessage)
        {
            AccountName = accountName ?? string.Empty;
            Platform = platform;
            IsRefreshing = isRefreshing;
            Balances = balances ?? Array.Empty<WalletBalanceEntry>();
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public string AccountName { get; }
        public PlatformKind Platform { get; }
        public bool IsRefreshing { get; }
        public IReadOnlyList<WalletBalanceEntry> Balances { get; }
        public string ErrorMessage { get; }
        public bool HasError => !IsRefreshing && !string.IsNullOrEmpty(ErrorMessage);
    }

    /// <summary>
    /// Read-only representation of a single balance entry.
    /// </summary>
    public sealed class WalletBalanceEntry
    {
        public WalletBalanceEntry(string symbol, decimal available, decimal staked, decimal claimable, string chain, uint decimals, bool burnable, bool fungible, IReadOnlyList<string> ids, string fiatWorth)
        {
            Symbol = symbol ?? string.Empty;
            Available = available;
            Staked = staked;
            Claimable = claimable;
            Chain = chain ?? string.Empty;
            Decimals = decimals;
            Burnable = burnable;
            Fungible = fungible;
            Ids = ids ?? Array.Empty<string>();
            FiatWorth = fiatWorth;
        }

        public string Symbol { get; }
        public decimal Available { get; }
        public decimal Staked { get; }
        public decimal Claimable { get; }
        public decimal Total => Available + Staked + Claimable;
        public string Chain { get; }
        public uint Decimals { get; }
        public bool Burnable { get; }
        public bool Fungible { get; }
        public IReadOnlyList<string> Ids { get; }
        public string FiatWorth { get; }
    }
}

