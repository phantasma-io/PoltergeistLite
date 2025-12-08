using System;
using System.Collections.Generic;
using System.Numerics;

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
        public WalletBalanceEntry(string symbol, BigInteger available, BigInteger staked, BigInteger claimable, string chain, uint decimals, bool burnable, bool fungible, IReadOnlyList<string> ids, string fiatWorth, string stakedFiatWorth, int displayPrecision)
        {
            Symbol = symbol ?? string.Empty;
            Decimals = decimals;
            Available = available;
            Staked = staked;
            Claimable = claimable;

            AvailableText = WalletAmountFormatter.Format(Available, decimals, displayPrecision);
            StakedText = WalletAmountFormatter.Format(Staked, decimals, displayPrecision);
            ClaimableText = WalletAmountFormatter.Format(Claimable, decimals, displayPrecision);

            Chain = chain ?? string.Empty;
            Burnable = burnable;
            Fungible = fungible;
            Ids = ids ?? Array.Empty<string>();
            FiatWorth = fiatWorth;
            StakedFiatWorth = stakedFiatWorth;
            Total = Available + Staked + Claimable;
        }

        public string Symbol { get; }
        public BigInteger Available { get; }
        public BigInteger Staked { get; }
        public BigInteger Claimable { get; }
        public string AvailableText { get; }
        public string StakedText { get; }
        public string ClaimableText { get; }
        public BigInteger Total { get; }
        public string Chain { get; }
        public uint Decimals { get; }
        public bool Burnable { get; }
        public bool Fungible { get; }
        public IReadOnlyList<string> Ids { get; }
        public string FiatWorth { get; }
        public string StakedFiatWorth { get; }
    }
}
