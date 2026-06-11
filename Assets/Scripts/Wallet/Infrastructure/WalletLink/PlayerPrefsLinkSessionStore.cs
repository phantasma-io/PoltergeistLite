using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using PhantasmaPhoenix.Protocol;
using UnityEngine;

namespace Poltergeist
{
    /// <summary>
    /// Durable Phantasma Link v5 session storage (spec §7) over PlayerPrefs, so authorized dApp
    /// sessions survive wallet restarts and resume without a new consent prompt. All calls arrive
    /// on the Unity main thread (the v5 dispatcher runs inside the link server's OnUI marshal),
    /// which is exactly what PlayerPrefs requires.
    /// </summary>
    public sealed class PlayerPrefsLinkSessionStore : ILinkSessionStore
    {
        private const string PrefsKey = "phantasma.link.v5.sessions";
        // Hard cap so an abusive dApp cannot grow the prefs blob without bound; the oldest
        // (least recently seen) sessions are evicted first.
        private const int MaxSessions = 50;

        private Dictionary<string, LinkSessionRecord> _records;

        private Dictionary<string, LinkSessionRecord> Records
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

        public LinkSessionRecord Get(string id)
        {
            return Records.TryGetValue(id, out var record) ? record : null;
        }

        public void Save(LinkSessionRecord record)
        {
            Records[record.Id] = record;

            if (Records.Count > MaxSessions)
            {
                foreach (var stale in Records.Values
                    .OrderBy(r => r.LastSeenUtc)
                    .Take(Records.Count - MaxSessions)
                    .ToArray())
                {
                    Records.Remove(stale.Id);
                }
            }

            Persist();
        }

        public void Remove(string id)
        {
            if (Records.Remove(id))
            {
                Persist();
            }
        }

        public LinkSessionRecord[] List()
        {
            return Records.Values.ToArray();
        }

        private static Dictionary<string, LinkSessionRecord> Load()
        {
            try
            {
                var json = PlayerPrefs.GetString(PrefsKey, "");
                if (!string.IsNullOrEmpty(json))
                {
                    var records = JsonConvert.DeserializeObject<List<LinkSessionRecord>>(json);
                    if (records != null)
                    {
                        return records.Where(r => !string.IsNullOrEmpty(r.Id)).ToDictionary(r => r.Id);
                    }
                }
            }
            catch (Exception)
            {
                // A corrupt blob must not break the wallet; dApps simply re-prompt.
            }
            return new Dictionary<string, LinkSessionRecord>();
        }

        private void Persist()
        {
            PlayerPrefs.SetString(PrefsKey, JsonConvert.SerializeObject(Records.Values.ToArray()));
            PlayerPrefs.Save();
        }
    }
}
