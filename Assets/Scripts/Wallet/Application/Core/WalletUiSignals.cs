using System;
using PhantasmaPhoenix.Protocol;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Event hub for UI layers to react to wallet data updates.
    /// </summary>
    public sealed class WalletUiSignals
    {
        private readonly Func<AccountManager> _accountProvider;
        private bool _subscribed;

        public WalletUiSignals(Func<AccountManager> accountProvider)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public void EnsureSubscribed()
        {
            if (_subscribed)
            {
                UnityEngine.Debug.Log("[Signals] Already subscribed"); //TODO Check if still needed once refactoring is over
                return;
            }

            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                UnityEngine.Debug.LogWarning("[Signals] AccountManager not ready, skipping subscription"); //TODO Check if still needed once refactoring is over
                return;
            }

            accountManager.BalancesUpdated += platform => BalancesUpdated?.Invoke(platform);
            accountManager.NftsUpdated += (platform, symbol) => NftsUpdated?.Invoke(platform, symbol);
            accountManager.HistoryUpdated += platform => HistoryUpdated?.Invoke(platform);
            accountManager.BalancesRefreshStarted += platform => BalancesRefreshStarted?.Invoke(platform);
            accountManager.NftsRefreshStarted += (platform, symbol) => NftsRefreshStarted?.Invoke(platform, symbol);
            accountManager.HistoryRefreshStarted += platform => HistoryRefreshStarted?.Invoke(platform);

            UnityEngine.Debug.Log("[Signals] Subscribed to AccountManager events"); //TODO Check if still needed once refactoring is over
            _subscribed = true;
        }

        public bool IsSubscribed => _subscribed;

        public event Action<PlatformKind> BalancesRefreshStarted;
        public event Action<PlatformKind> BalancesUpdated;
        public event Action<PlatformKind, string> NftsUpdated;
        public event Action<PlatformKind> HistoryUpdated;
        public event Action<PlatformKind, string> NftsRefreshStarted;
        public event Action<PlatformKind> HistoryRefreshStarted;
        public event Action SettingsChanged;

        public void RaiseSettingsChanged()
        {
            SettingsChanged?.Invoke();
        }
    }
}
