using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Unity.Core.Logging;

namespace Poltergeist
{
    // Tracks which wallets the user has hidden from the account list. Owns its own state and
    // PlayerPrefs persistence and keeps itself pruned to the accounts that still exist. Pulled out
    // of AccountManager because it is a self-contained concern; AccountManager keeps thin
    // forwarders so its public surface is unchanged.
    public class HiddenWalletStore
    {
        private const string StorageTag = "wallet.hidden.phantasma";

        private readonly HashSet<string> _hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<IEnumerable<Account>> _accounts;

        // accountsProvider yields the current accounts (read lazily, since the wallet list loads
        // after this store is created) and is used to prune hidden entries for deleted wallets.
        public HiddenWalletStore(Func<IEnumerable<Account>> accountsProvider)
        {
            _accounts = accountsProvider;
        }

        public IReadOnlyCollection<string> Hidden => _hidden;

        public bool IsHidden(string phaAddress)
        {
            if (string.IsNullOrWhiteSpace(phaAddress))
            {
                return false;
            }

            return _hidden.Contains(phaAddress.Trim());
        }

        public void Apply(IEnumerable<string> addresses, bool persistImmediately = true)
        {
            // Hidden wallets live in a separate list so UI can filter them out without mutating account data.
            // Changes can be staged (persistImmediately=false) while the user is inside Wallet Management.
            _hidden.Clear();
            if (addresses != null)
            {
                foreach (var address in addresses)
                {
                    var normalized = address?.Trim();
                    if (string.IsNullOrWhiteSpace(normalized))
                    {
                        continue;
                    }

                    _hidden.Add(normalized);
                }
            }

            Prune();
            if (persistImmediately)
            {
                Save(true);
            }
        }

        public void Load()
        {
            _hidden.Clear();
            var serialized = PlayerPrefs.GetString(StorageTag, string.Empty);
            if (string.IsNullOrWhiteSpace(serialized))
            {
                return;
            }

            try
            {
                var bytes = Base16.Decode(serialized);
                var addresses = Serialization.Unserialize<string[]>(bytes) ?? Array.Empty<string>();
                foreach (var address in addresses)
                {
                    var normalized = address?.Trim();
                    if (string.IsNullOrWhiteSpace(normalized))
                    {
                        continue;
                    }

                    _hidden.Add(normalized);
                }

                Prune();
            }
            catch (Exception e)
            {
                Log.WriteWarning($"Failed to load hidden wallets: {e}");
                _hidden.Clear();
            }
        }

        public void Save(bool flush)
        {
            try
            {
                var bytes = Serialization.Serialize(_hidden.ToArray());
                PlayerPrefs.SetString(StorageTag, Base16.Encode(bytes));
                if (flush)
                {
                    PlayerPrefs.Save();
                }
            }
            catch (Exception e)
            {
                Log.WriteWarning($"Failed to save hidden wallets: {e}");
            }
        }

        public void Prune()
        {
            // Keep hidden flags in sync with the currently available wallets and avoid persisting stale entries
            // for accounts that were removed during this session.
            var accounts = _accounts?.Invoke();
            var accountList = accounts == null ? null : (accounts as IReadOnlyCollection<Account> ?? accounts.ToList());
            if (accountList == null || accountList.Count == 0)
            {
                _hidden.Clear();
                return;
            }

            var known = new HashSet<string>(
                accountList.Where(x => !string.IsNullOrWhiteSpace(x.phaAddress)).Select(x => x.phaAddress),
                StringComparer.OrdinalIgnoreCase);
            _hidden.RemoveWhere(address => !known.Contains(address));
        }
    }
}
