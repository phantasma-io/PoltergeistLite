using System;
using System.Collections.Generic;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Immutable descriptor of an NFT inspection step used to restore the navigation trail.
    /// </summary>
    public readonly struct WalletNftInspectEntry
    {
        public WalletNftInspectEntry(string symbol, string tokenId, bool locked)
        {
            Symbol = symbol ?? string.Empty;
            TokenId = tokenId ?? string.Empty;
            Locked = locked;
        }

        public string Symbol { get; }
        public string TokenId { get; }
        public bool Locked { get; }

        public bool IsValid => !string.IsNullOrEmpty(Symbol) && !string.IsNullOrEmpty(TokenId);
    }

    /// <summary>
    /// Caches view-ready snapshots and shared selection state used by UI layers.
    /// </summary>
    public sealed class WalletViewState
    {
        private readonly Dictionary<string, WalletNftViewSnapshot> nftSnapshots = new Dictionary<string, WalletNftViewSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> dirtyNftSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> selectedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<WalletNftInspectEntry> nftInspectTrail = new List<WalletNftInspectEntry>();

        public WalletBalanceViewSnapshot BalancesSnapshot { get; private set; }
        public WalletHistoryViewSnapshot HistorySnapshot { get; private set; }
        public bool BalancesDirty { get; private set; } = true;
        public bool HistoryDirty { get; private set; } = true;
        public string TransferSymbol { get; set; }
        public float AccountScrollY { get; set; }
        public float NftScrollY { get; set; }
        public float NftTransferScrollY { get; set; }
        public string TokenDashboardSymbol { get; set; }
        public IReadOnlyCollection<string> SelectedAccounts => selectedAccounts;
        public int SelectedAccountCount => selectedAccounts.Count;
        public IReadOnlyList<WalletNftInspectEntry> NftInspectTrail => nftInspectTrail;
        public bool HasNftInspect => nftInspectTrail.Count > 0;

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
            TokenDashboardSymbol = null;
            ClearNftInspectTrail();
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

        public void PushNftInspect(WalletNftInspectEntry entry)
        {
            if (!entry.IsValid)
            {
                return;
            }

            nftInspectTrail.Add(entry);
        }

        public bool TryPopNftInspect(out WalletNftInspectEntry entry)
        {
            if (nftInspectTrail.Count > 0)
            {
                entry = nftInspectTrail[nftInspectTrail.Count - 1];
                nftInspectTrail.RemoveAt(nftInspectTrail.Count - 1);
                return true;
            }

            entry = default;
            return false;
        }

        public WalletNftInspectEntry? PeekNftInspect()
        {
            if (nftInspectTrail.Count == 0)
            {
                return null;
            }

            return nftInspectTrail[nftInspectTrail.Count - 1];
        }

        public void ClearNftInspectTrail()
        {
            nftInspectTrail.Clear();
        }
    }
}
