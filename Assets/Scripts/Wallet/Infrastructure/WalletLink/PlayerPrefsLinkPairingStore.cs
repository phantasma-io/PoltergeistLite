using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using PhantasmaPhoenix.Protocol;
using UnityEngine;

namespace Poltergeist
{
    /// <summary>
    /// Durable Phantasma Link v5 pairing storage (spec §17/§19) over PlayerPrefs, so paired
    /// dApps keep their deeplink channel across wallet restarts. Same model as
    /// <see cref="PlayerPrefsLinkSessionStore"/>: all calls arrive on the Unity main thread.
    /// </summary>
    public sealed class PlayerPrefsLinkPairingStore : ILinkPairingStore
    {
        private const string PrefsKey = "phantasma.link.v5.pairings";
        private const int MaxPairings = 50;

        private Dictionary<string, LinkPairingRecord> _records;

        private Dictionary<string, LinkPairingRecord> Records
        {
            get
            {
                if (_records == null)
                {
                    _records = Load();
                }
                return _records;
            }
        }

        public LinkPairingRecord Get(string topic)
        {
            return Records.TryGetValue(topic, out var record) ? record : null;
        }

        public void Save(LinkPairingRecord record)
        {
            Records[record.Topic] = record;

            if (Records.Count > MaxPairings)
            {
                foreach (var stale in Records.Values
                    .OrderBy(r => r.LastSeenUtc)
                    .Take(Records.Count - MaxPairings)
                    .ToArray())
                {
                    Records.Remove(stale.Topic);
                }
            }

            Persist();
        }

        public void Remove(string topic)
        {
            if (Records.Remove(topic))
            {
                Persist();
            }
        }

        public LinkPairingRecord[] List()
        {
            return Records.Values.ToArray();
        }

        private static Dictionary<string, LinkPairingRecord> Load()
        {
            try
            {
                var json = PlayerPrefs.GetString(PrefsKey, "");
                if (!string.IsNullOrEmpty(json))
                {
                    var records = JsonConvert.DeserializeObject<List<LinkPairingRecord>>(json);
                    if (records != null)
                    {
                        return records
                            .Where(r => !string.IsNullOrEmpty(r.Topic) && r.Key != null && r.Key.Length == 32)
                            .ToDictionary(r => r.Topic);
                    }
                }
            }
            catch (Exception)
            {
                // A corrupt blob must not break the wallet; dApps simply re-pair.
            }
            return new Dictionary<string, LinkPairingRecord>();
        }

        private void Persist()
        {
            PlayerPrefs.SetString(PrefsKey, JsonConvert.SerializeObject(Records.Values.ToArray()));
            PlayerPrefs.Save();
        }
    }
}
