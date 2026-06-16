using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.VM;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Core.Extensions;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Unity.Core;
using PhantasmaPhoenix.NFT;
using PhantasmaPhoenix.NFT.Extensions;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Unity.Core.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Numerics;

namespace Poltergeist
{
    public partial class AccountManager
    {
        public bool IsWalletHidden(string phaAddress)
        {
            if (string.IsNullOrWhiteSpace(phaAddress))
            {
                return false;
            }

            return hiddenPhantasmaAddresses.Contains(phaAddress.Trim());
        }

        public void ApplyHiddenWallets(IEnumerable<string> addresses, bool persistImmediately = true)
        {
            // Hidden wallets live in a separate list so UI can filter them out without mutating account data.
            // Changes can be staged (persistImmediately=false) while the user is inside Wallet Management.
            hiddenPhantasmaAddresses.Clear();
            if (addresses != null)
            {
                foreach (var address in addresses)
                {
                    var normalized = address?.Trim();
                    if (string.IsNullOrWhiteSpace(normalized))
                    {
                        continue;
                    }

                    hiddenPhantasmaAddresses.Add(normalized);
                }
            }

            PruneHiddenWallets();
            if (persistImmediately)
            {
                SaveHiddenWalletsInternal(true);
            }
        }

        private void LoadHiddenWallets()
        {
            hiddenPhantasmaAddresses.Clear();
            var serialized = PlayerPrefs.GetString(HiddenWalletsTag, string.Empty);
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

                    hiddenPhantasmaAddresses.Add(normalized);
                }

                PruneHiddenWallets();
            }
            catch (Exception e)
            {
                Log.WriteWarning($"Failed to load hidden wallets: {e}");
                hiddenPhantasmaAddresses.Clear();
            }
        }

        private void SaveHiddenWalletsInternal(bool flush)
        {
            try
            {
                var bytes = Serialization.Serialize(hiddenPhantasmaAddresses.ToArray());
                PlayerPrefs.SetString(HiddenWalletsTag, Base16.Encode(bytes));
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

        private void PruneHiddenWallets()
        {
            // Keep hidden flags in sync with the currently available wallets and avoid persisting stale entries
            // for accounts that were removed during this session.
            if (Accounts == null || Accounts.Count == 0)
            {
                hiddenPhantasmaAddresses.Clear();
                return;
            }

            var known = new HashSet<string>(
                Accounts.Where(x => !string.IsNullOrWhiteSpace(x.phaAddress)).Select(x => x.phaAddress),
                StringComparer.OrdinalIgnoreCase);
            hiddenPhantasmaAddresses.RemoveWhere(address => !known.Contains(address));
        }

    }
}
