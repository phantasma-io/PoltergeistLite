using System;
using System.Collections.Generic;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Caches view-ready snapshots and shared selection state used by UI layers.
    /// </summary>
    public sealed class WalletViewState
    {
        private readonly Dictionary<string, WalletNftViewSnapshot> nftSnapshots = new Dictionary<string, WalletNftViewSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> dirtyNftSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public WalletBalanceViewSnapshot BalancesSnapshot { get; private set; }
        public WalletHistoryViewSnapshot HistorySnapshot { get; private set; }
        public bool BalancesDirty { get; private set; } = true;
        public bool HistoryDirty { get; private set; } = true;
        public string TransferSymbol { get; set; }

        public WalletBalanceViewSnapshot GetBalancesSnapshot(Func<WalletBalanceViewSnapshot> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (BalancesDirty || BalancesSnapshot == null)
            {
                BalancesSnapshot = builder();
                BalancesDirty = false;
            }

            return BalancesSnapshot;
        }

        public WalletHistoryViewSnapshot GetHistorySnapshot(Func<WalletHistoryViewSnapshot> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (HistoryDirty || HistorySnapshot == null)
            {
                HistorySnapshot = builder();
                HistoryDirty = false;
            }

            return HistorySnapshot;
        }

        public WalletNftViewSnapshot GetNftSnapshot(string symbol, Func<string, WalletNftViewSnapshot> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (string.IsNullOrEmpty(symbol))
            {
                return builder(string.Empty);
            }

            if (dirtyNftSymbols.Contains(symbol) || !nftSnapshots.TryGetValue(symbol, out var snapshot))
            {
                snapshot = builder(symbol);
                nftSnapshots[symbol] = snapshot;
                dirtyNftSymbols.Remove(symbol);
            }

            return snapshot;
        }

        public void MarkBalancesDirty()
        {
            BalancesDirty = true;
        }

        public void MarkHistoryDirty()
        {
            HistoryDirty = true;
        }

        public void MarkNftDirty(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
            {
                return;
            }

            dirtyNftSymbols.Add(symbol);
        }

        public bool IsNftDirty(string symbol)
        {
            return !string.IsNullOrEmpty(symbol) && dirtyNftSymbols.Contains(symbol);
        }

        public void ResetSnapshots()
        {
            BalancesSnapshot = null;
            HistorySnapshot = null;
            nftSnapshots.Clear();
            dirtyNftSymbols.Clear();
            MarkBalancesDirty();
            MarkHistoryDirty();
        }
    }
}
