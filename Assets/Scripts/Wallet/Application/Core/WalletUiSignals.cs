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
                return;
            }

            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return;
            }

            accountManager.BalancesUpdated += platform => BalancesUpdated?.Invoke(platform);
            accountManager.NftsUpdated += (platform, symbol) => NftsUpdated?.Invoke(platform, symbol);
            accountManager.HistoryUpdated += platform => HistoryUpdated?.Invoke(platform);
            accountManager.BalancesRefreshStarted += platform => BalancesRefreshStarted?.Invoke(platform);
            accountManager.NftsRefreshStarted += (platform, symbol) => NftsRefreshStarted?.Invoke(platform, symbol);
            accountManager.HistoryRefreshStarted += platform => HistoryRefreshStarted?.Invoke(platform);

            _subscribed = true;
        }

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
