using System.Collections.Generic;
using PhantasmaIntegration;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.RPC.Models;

namespace Poltergeist
{
    public class AccountState
    {
        public PlatformKind platform;
        public string name;
        public string address;
        public Balance[] balances;
        public AccountFlags flags;
        public Timestamp stakeTime;

        public ArchiveResult[] archives;
        public string avatarData;
        public uint availableStorage;
        public uint usedStorage;
        public uint totalStorage => availableStorage + usedStorage;

        public Dictionary<string, string> dappTokens = new Dictionary<string, string>();

        public decimal GetAvailableAmount(string symbol)
        {
            if (balances == null)
            {
                return 0;
            }
            
            for (int i = 0; i < balances.Length; i++)
            {
                var entry = balances[i];
                if (entry.Symbol == symbol)
                {
                    return entry.Available;
                }
            }

            return 0;
        }

        public void RegisterDappToken(string dapp, string token)
        {
            dappTokens[dapp] = token;
        }
    }
}
