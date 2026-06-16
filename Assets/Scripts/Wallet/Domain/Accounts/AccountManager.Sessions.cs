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
        internal string GetBalanceError(PlatformKind platform)
        {
            lock (_refreshStatus)
            {
                if (_refreshStatus.TryGetValue(platform, out var status))
                {
                    return status.BalanceError;
                }
            }

            return null;
        }

        private void SetBalanceError(PlatformKind platform, string message)
        {
            lock (_refreshStatus)
            {
                if (_refreshStatus.TryGetValue(platform, out var status))
                {
                    status.BalanceError = message;
                    _refreshStatus[platform] = status;
                }
                else
                {
                    _refreshStatus[platform] = new RefreshStatus
                    {
                        BalanceError = message
                    };
                }
            }
        }

        private void SetBalanceError(PlatformKind platform, string message, AccountSession session)
        {
            if (!IsSessionCurrent(session))
            {
                return;
            }

            SetBalanceError(platform, message);
        }

        private void AdvanceAccountSession()
        {
            lock (_accountSessionLock)
            {
                _accountSessionId++;
                _accountSessionCts.Cancel();
                // Keep old CTS undisposed; in-flight tasks may still register callbacks.
                _accountSessionCts = new CancellationTokenSource();
            }
        }

        private static string NormalizeNftSymbol(string symbol)
        {
            return string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();
        }

        public bool IsNftRefreshing(string symbol)
        {
            return IsNftRefreshing(CurrentPlatform, symbol);
        }

        public bool IsNftRefreshing(PlatformKind platform, string symbol)
        {
            var normalizedSymbol = NormalizeNftSymbol(symbol);
            if (string.IsNullOrEmpty(normalizedSymbol))
            {
                return false;
            }

            var key = (platform, normalizedSymbol);
            lock (_nftRefreshLock)
            {
                return _nftRefreshCounts.TryGetValue(key, out var count) && count > 0;
            }
        }

        private bool AnyNftRefreshing(PlatformKind platform)
        {
            lock (_nftRefreshLock)
            {
                foreach (var entry in _nftRefreshCounts)
                {
                    if (entry.Key.platform == platform && entry.Value > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void BeginNftRefresh(PlatformKind platform, string symbol)
        {
            var normalizedSymbol = NormalizeNftSymbol(symbol);
            if (string.IsNullOrEmpty(normalizedSymbol))
            {
                return;
            }

            var key = (platform, normalizedSymbol);
            lock (_nftRefreshLock)
            {
                _nftRefreshCounts.TryGetValue(key, out var count);
                _nftRefreshCounts[key] = count + 1;
            }

            UpdateNftRefreshingStatus(platform);
        }

        private void EndNftRefresh(PlatformKind platform, string symbol)
        {
            var normalizedSymbol = NormalizeNftSymbol(symbol);
            if (string.IsNullOrEmpty(normalizedSymbol))
            {
                return;
            }

            var key = (platform, normalizedSymbol);
            lock (_nftRefreshLock)
            {
                if (_nftRefreshCounts.TryGetValue(key, out var count))
                {
                    count--;
                    if (count <= 0)
                    {
                        _nftRefreshCounts.Remove(key);
                    }
                    else
                    {
                        _nftRefreshCounts[key] = count;
                    }
                }
            }

            UpdateNftRefreshingStatus(platform);
        }

        private void UpdateNftRefreshingStatus(PlatformKind platform)
        {
            var anyRefreshing = AnyNftRefreshing(platform);
            lock (_refreshStatus)
            {
                var refreshStatus = _refreshStatus.ContainsKey(platform)
                    ? _refreshStatus[platform]
                    : new RefreshStatus();
                refreshStatus.NftsRefreshing = anyRefreshing;
                _refreshStatus[platform] = refreshStatus;
            }
        }

        private long AdvanceNftSession(PlatformKind platform, string symbol)
        {
            var normalizedSymbol = NormalizeNftSymbol(symbol);
            if (string.IsNullOrEmpty(normalizedSymbol))
            {
                return 0;
            }

            var key = (platform, normalizedSymbol);
            lock (_nftSessionLock)
            {
                if (!_nftSessionIds.TryGetValue(key, out var sessionId))
                {
                    sessionId = 0;
                }

                sessionId++;
                _nftSessionIds[key] = sessionId;
                return sessionId;
            }
        }

        private void ResetNftSession()
        {
            lock (_nftSessionLock)
            {
                _nftSessionIds.Clear();
                _nftDescriptionsLoaded.Clear();
            }

            lock (_nftRefreshLock)
            {
                _nftRefreshCounts.Clear();
            }
        }

        private bool IsNftSessionCurrent(PlatformKind platform, string symbol, long nftSessionId)
        {
            var normalizedSymbol = NormalizeNftSymbol(symbol);
            if (string.IsNullOrEmpty(normalizedSymbol))
            {
                return false;
            }

            var key = (platform, normalizedSymbol);
            lock (_nftSessionLock)
            {
                return _nftSessionIds.TryGetValue(key, out var currentId) && currentId == nftSessionId;
            }
        }

        private bool IsNftRequestCurrent(AccountSession session, PlatformKind platform, string symbol, long nftSessionId)
        {
            return IsSessionCurrent(session) && IsNftSessionCurrent(platform, symbol, nftSessionId);
        }

        private bool AreNftDescriptionsLoaded(PlatformKind platform, string symbol)
        {
            var normalizedSymbol = NormalizeNftSymbol(symbol);
            if (string.IsNullOrEmpty(normalizedSymbol))
            {
                return false;
            }

            var key = (platform, normalizedSymbol);
            lock (_nftSessionLock)
            {
                return _nftDescriptionsLoaded.TryGetValue(key, out var loaded) && loaded;
            }
        }

        private void SetNftDescriptionsLoaded(PlatformKind platform, string symbol, bool loaded)
        {
            var normalizedSymbol = NormalizeNftSymbol(symbol);
            if (string.IsNullOrEmpty(normalizedSymbol))
            {
                return;
            }

            var key = (platform, normalizedSymbol);
            lock (_nftSessionLock)
            {
                _nftDescriptionsLoaded[key] = loaded;
            }
        }

        private AccountSession CaptureAccountSession()
        {
            lock (_accountSessionLock)
            {
                var accountIndex = _selectedAccountIndex;
                string phaAddress = null;
                string neoAddress = null;
                string ethAddress = null;
                if (Accounts != null && accountIndex >= 0 && accountIndex < Accounts.Count)
                {
                    var account = Accounts[accountIndex];
                    phaAddress = account.phaAddress;
                    neoAddress = account.neoAddress;
                    ethAddress = account.ethAddress;
                }

                return new AccountSession(_accountSessionId, accountIndex, phaAddress, neoAddress, ethAddress, _accountSessionCts.Token);
            }
        }

        private bool IsSessionCurrent(AccountSession session)
        {
            lock (_accountSessionLock)
            {
                return session.Id == _accountSessionId;
            }
        }

        private bool IsSessionCurrent(AccountSession session, PlatformKind platform, string address)
        {
            lock (_accountSessionLock)
            {
                if (session.Id != _accountSessionId)
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(address))
                {
                    var expected = session.GetAddress(platform);
                    if (string.IsNullOrEmpty(expected) || !string.Equals(expected, address, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

    }
}
