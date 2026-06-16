using System.Collections.Generic;

namespace Poltergeist
{
    public partial class AccountManager
    {
        // The hidden-wallet logic and state live in HiddenWalletStore; these are thin forwarders so
        // AccountManager's public surface and all existing call sites stay unchanged.
        private HiddenWalletStore _hiddenWallets;

        public IReadOnlyCollection<string> HiddenPhantasmaAddresses => _hiddenWallets.Hidden;

        public bool IsWalletHidden(string phaAddress) => _hiddenWallets.IsHidden(phaAddress);

        public void ApplyHiddenWallets(IEnumerable<string> addresses, bool persistImmediately = true)
            => _hiddenWallets.Apply(addresses, persistImmediately);

        private void LoadHiddenWallets() => _hiddenWallets.Load();
        private void SaveHiddenWalletsInternal(bool flush) => _hiddenWallets.Save(flush);
        private void PruneHiddenWallets() => _hiddenWallets.Prune();
    }
}
