using System;
using System.Collections.Generic;
using PhantasmaPhoenix.Protocol;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Provides address hint lists for account/platform combinations.
    /// </summary>
    public sealed class WalletAccountHintsService
    {
        private readonly Func<AccountManager> _accountProvider;

        public WalletAccountHintsService(Func<AccountManager> accountProvider)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public Dictionary<string, string> BuildAccountHints(PlatformKind targets)
        {
            var hints = new Dictionary<string, string>();

            var accountManager = _accountProvider();
            if (accountManager == null || !accountManager.HasSelection || accountManager.Accounts == null)
            {
                return hints;
            }

            var currentAccount = accountManager.CurrentAccount;
            var currentPlatform = accountManager.CurrentPlatform;

            for (int index = 0; index < accountManager.Accounts.Count; index++)
            {
                var account = accountManager.Accounts[index];
                if (account.name == currentAccount.name)
                {
                    continue;
                }

                var platforms = account.platforms.Split();
                foreach (var platform in platforms)
                {
                    if (!targets.HasFlag(platform))
                    {
                        continue;
                    }

                    // In Poltergeist we support swaps only within same account.
                    if ((currentPlatform == PlatformKind.Ethereum || currentPlatform == PlatformKind.BSC || currentPlatform == PlatformKind.Neo) && platform == PlatformKind.Phantasma)
                    {
                        continue;
                    }

                    if (currentPlatform == PlatformKind.Phantasma && platform != PlatformKind.Phantasma)
                    {
                        continue;
                    }

                    var addr = accountManager.GetAddress(index, platform);
                    if (!string.IsNullOrEmpty(addr))
                    {
                        var shortenedPlatform = platform switch
                        {
                            PlatformKind.Phantasma => "Pha",
                            PlatformKind.Ethereum => "Eth",
                            _ => platform.ToString()
                        };

                        var key = $"{account.name} [{shortenedPlatform}]";
                        hints[key] = addr;
                    }
                }
            }

            return hints;
        }
    }
}
