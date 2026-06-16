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
        public string GetPhantasmaTransactionURL(string hash)
        {
            var url = Settings.phantasmaExplorer;
            if (!url.EndsWith("/"))
            {
                url += "/";
            }

            return $"{url}tx/{hash}";
        }

        public string GetPhantasmaAddressURL(string address)
        {
            var url = Settings.phantasmaExplorer;
            if (!url.EndsWith("/"))
            {
                url += "/";
            }

            return $"{url}address/{address}";
        }

        public string GetPhantasmaContractURL(string symbol)
        {
            var url = Settings.phantasmaExplorer;
            if (!url.EndsWith("/"))
            {
                url += "/";
            }

            return $"{url}contract/{symbol}";
        }

        public string GetPhantasmaNftURL(string symbol, string tokenId)
        {
            var url = Settings.phantasmaNftExplorer;
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            if (!url.EndsWith("/"))
            {
                url += "/";
            }

            return $"{url}{symbol.ToLower()}/{tokenId}";
        }

        public string GetEthExplorerURL(string address)
        {
            return $"https://etherscan.io/address/{address}";
        }
        public string GetBscExplorerURL(string address)
        {
            return $"https://bscscan.com/address/{address}";
        }
        public string GetN2ExplorerURL(string address)
        {
            return $"https://neo2.neotube.io/address/{address}";
        }

        // Single source of the case-insensitive duplicate-name rule shared by add and rename.
    }
}
