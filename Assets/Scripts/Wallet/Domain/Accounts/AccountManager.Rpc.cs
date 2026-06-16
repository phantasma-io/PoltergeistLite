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
        // RPC endpoint selection, benchmarking and failover live in RpcHealth; these forward so all
        // existing call sites and the UI status flags are unchanged.
        private RpcHealth _rpcHealth;

        public int rpcAvailablePhantasma => _rpcHealth.rpcAvailablePhantasma;
        public bool ReportGetPeersFailure { get => _rpcHealth.ReportGetPeersFailure; set => _rpcHealth.ReportGetPeersFailure = value; }
        public bool ReportAllRpcsUnavailabe { get => _rpcHealth.ReportAllRpcsUnavailabe; set => _rpcHealth.ReportAllRpcsUnavailabe = value; }

        public void UpdateRPCURL() => _rpcHealth.UpdateRPCURL();
        public void ChangeFaultyRPCURL(PlatformKind platformKind) => _rpcHealth.ChangeFaultyRPCURL(platformKind);
        private void RotateRpcOnWebError(PhantasmaRequestException ex) => _rpcHealth.RotateRpcOnWebError(ex);
    }
}
