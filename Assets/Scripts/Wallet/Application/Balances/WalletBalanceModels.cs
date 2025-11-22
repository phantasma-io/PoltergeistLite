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
        public WalletBalanceEntry(string symbol, BigInteger availableRaw, BigInteger stakedRaw, BigInteger claimableRaw, string chain, uint decimals, bool burnable, bool fungible, IReadOnlyList<string> ids, string fiatWorth)
        {
            Symbol = symbol ?? string.Empty;
            Decimals = decimals;
            AvailableRaw = availableRaw;
            StakedRaw = stakedRaw;
            ClaimableRaw = claimableRaw;

            Available = WalletAmountFormatter.ToDecimal(AvailableRaw, decimals, out var availableOverflow);
            Staked = WalletAmountFormatter.ToDecimal(StakedRaw, decimals, out var stakedOverflow);
            Claimable = WalletAmountFormatter.ToDecimal(ClaimableRaw, decimals, out var claimableOverflow);

            AvailableOverflow = availableOverflow;
            StakedOverflow = stakedOverflow;
            ClaimableOverflow = claimableOverflow;

            AvailableText = WalletAmountFormatter.Format(AvailableRaw, decimals);
            StakedText = WalletAmountFormatter.Format(StakedRaw, decimals);
            ClaimableText = WalletAmountFormatter.Format(ClaimableRaw, decimals);

            Chain = chain ?? string.Empty;
            Burnable = burnable;
            Fungible = fungible;
            Ids = ids ?? Array.Empty<string>();
            FiatWorth = fiatWorth;
        }

        public string Symbol { get; }
        public BigInteger AvailableRaw { get; }
        public BigInteger StakedRaw { get; }
        public BigInteger ClaimableRaw { get; }
        public decimal Available { get; }
        public decimal Staked { get; }
        public decimal Claimable { get; }
        public bool AvailableOverflow { get; }
        public bool StakedOverflow { get; }
        public bool ClaimableOverflow { get; }
        public string AvailableText { get; }
        public string StakedText { get; }
        public string ClaimableText { get; }
        public decimal Total
        {
            get
            {
                try
                {
                    return Available + Staked + Claimable;
                }
                catch (OverflowException)
                {
                    return decimal.MaxValue;
                }
            }
        }
        public string Chain { get; }
        public uint Decimals { get; }
        public bool Burnable { get; }
        public bool Fungible { get; }
        public IReadOnlyList<string> Ids { get; }
        public string FiatWorth { get; }
    }
}
