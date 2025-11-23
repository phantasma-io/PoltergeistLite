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
        private readonly HashSet<string> selectedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public WalletBalanceViewSnapshot BalancesSnapshot { get; private set; }
        public WalletHistoryViewSnapshot HistorySnapshot { get; private set; }
        public bool BalancesDirty { get; private set; } = true;
        public bool HistoryDirty { get; private set; } = true;
        public string TransferSymbol { get; set; }
        public float AccountScrollY { get; set; }
        public float NftScrollY { get; set; }
        public float NftTransferScrollY { get; set; }
        public IReadOnlyCollection<string> SelectedAccounts => selectedAccounts;
        public int SelectedAccountCount => selectedAccounts.Count;

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
            selectedAccounts.Clear();
            TransferSymbol = null;
            AccountScrollY = 0f;
            NftScrollY = 0f;
            NftTransferScrollY = 0f;
            MarkBalancesDirty();
            MarkHistoryDirty();
        }

        public bool IsAccountSelected(string address)
        {
            return !string.IsNullOrEmpty(address) && selectedAccounts.Contains(address);
        }

        public void SelectAccount(string address)
        {
            if (!string.IsNullOrEmpty(address))
            {
                selectedAccounts.Add(address);
            }
        }

        public void UnselectAccount(string address)
        {
            if (!string.IsNullOrEmpty(address))
            {
                selectedAccounts.Remove(address);
            }
        }

        public void ClearAccountSelection()
        {
            selectedAccounts.Clear();
        }
    }
}
