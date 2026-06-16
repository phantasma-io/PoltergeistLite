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
    public partial class AccountManager : MonoBehaviour
    {
        public static readonly int MinPasswordLength = 6;
        public static readonly int MaxPasswordLength = 32;
        public static readonly int MinAccountNameLength = 3;
        public static readonly int MaxAccountNameLength = 16;
        public string WalletIdentifier => "PGL" + UnityEngine.Application.version;
        public const string HiddenWalletsTag = "wallet.hidden.phantasma";

        public Settings Settings { get; private set; }

        public List<Account> Accounts { get; private set; }
        public bool AccountsAreReadyToBeUsed = false;
        private readonly HashSet<string> hiddenPhantasmaAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyCollection<string> HiddenPhantasmaAddresses => hiddenPhantasmaAddresses;

        private Dictionary<string, decimal> _tokenPrices = new Dictionary<string, decimal>();
        public string CurrentTokenCurrency { get; private set; }

        // Keep selection unset until the user explicitly opens a wallet to avoid auto-opening arbitrary accounts on startup.
        private int _selectedAccountIndex = -1;
        public int CurrentIndex => _selectedAccountIndex;
        public Account CurrentAccount => HasSelection ? Accounts[_selectedAccountIndex] : new Account() { };
        public string CurrentPasswordHash;
        public string CurrentWif => Accounts[_selectedAccountIndex].GetWif(CurrentPasswordHash);

        public bool HasSelection => Accounts != null && _selectedAccountIndex >= 0 && _selectedAccountIndex < Accounts.Count();

        private Dictionary<PlatformKind, AccountState> _states = new Dictionary<PlatformKind, AccountState>();
        private readonly Dictionary<(PlatformKind platform, string symbol), List<TokenDataResult>> _nfts = new();
        private readonly Dictionary<(PlatformKind platform, string symbol), Dictionary<string, IRom>> _roms = new();
        private readonly Dictionary<(PlatformKind platform, string symbol), bool> _nftDescriptionsLoaded = new();
        private readonly object _nftCacheLock = new object();
        private readonly object _nftRefreshLock = new object();
        private readonly Dictionary<(PlatformKind platform, string symbol), int> _nftRefreshCounts = new();
        private Dictionary<PlatformKind, HistoryEntry[]> _history = new Dictionary<PlatformKind, HistoryEntry[]>();
        public Dictionary<PlatformKind, RefreshStatus> _refreshStatus = new Dictionary<PlatformKind, RefreshStatus>();

        // Bumps whenever the selected account changes; guards async responses against stale sessions.
        private readonly object _accountSessionLock = new object();
        private long _accountSessionId;
        private CancellationTokenSource _accountSessionCts = new CancellationTokenSource();

        // Bumps whenever NFT refresh requests change per platform+symbol; guards against stale symbol updates.
        private readonly object _nftSessionLock = new object();
        private readonly Dictionary<(PlatformKind platform, string symbol), long> _nftSessionIds = new();

        private readonly struct AccountSession
        {
            public AccountSession(long id, int accountIndex, string phaAddress, string neoAddress, string ethAddress, CancellationToken token)
            {
                Id = id;
                AccountIndex = accountIndex;
                PhaAddress = phaAddress;
                NeoAddress = neoAddress;
                EthAddress = ethAddress;
                Token = token;
            }

            public long Id { get; }
            public int AccountIndex { get; }
            public string PhaAddress { get; }
            public string NeoAddress { get; }
            public string EthAddress { get; }
            public CancellationToken Token { get; }

            public string GetAddress(PlatformKind platform)
            {
                switch (platform)
                {
                    case PlatformKind.Phantasma:
                        return PhaAddress;
                    case PlatformKind.Neo:
                        return NeoAddress;
                    case PlatformKind.Ethereum:
                    case PlatformKind.BSC:
                        return EthAddress;
                    default:
                        return null;
                }
            }
        }

        public event Action<PlatformKind> BalancesRefreshStarted;
        public event Action<PlatformKind> BalancesUpdated;
        public event Action<PlatformKind, string> NftsUpdated;
        public event Action<PlatformKind, string> NftsRefreshStarted;
        public event Action<PlatformKind> HistoryUpdated;
        public event Action<PlatformKind> HistoryRefreshStarted;

        public PlatformKind CurrentPlatform { get; set; }
        public AccountState CurrentState => _states.ContainsKey(CurrentPlatform) ? _states[CurrentPlatform] : null;
        public List<TokenDataResult> CurrentNfts
        {
            get
            {
                List<TokenDataResult> current = null;

                lock (_nftCacheLock)
                {
                    foreach (var entry in _nfts)
                    {
                        if (entry.Key.platform != CurrentPlatform)
                        {
                            continue;
                        }

                        if (current != null)
                        {
                            return null;
                        }

                        current = entry.Value;
                    }
                }

                return current;
            }
        }
        public HistoryEntry[] CurrentHistory => _history.ContainsKey(CurrentPlatform) ? _history[CurrentPlatform] : null;

        public AccountState MainState => _states.ContainsKey(PlatformKind.Phantasma) ? _states[PlatformKind.Phantasma] : null;

        private TtrsNftSortMode currentTtrsNftsSortMode = TtrsNftSortMode.None;
        private NftSortMode currentNftsSortMode = NftSortMode.None;
        private SortDirection currentNftsSortDirection = SortDirection.None;

        public static AccountManager Instance { get; private set; }

        public string Status { get; private set; }
        public bool Ready => Status == "ok";
        public bool BalanceRefreshing => _refreshStatus.ContainsKey(CurrentPlatform) ? _refreshStatus[CurrentPlatform].BalanceRefreshing : false;
        public bool NftsRefreshing => AnyNftRefreshing(CurrentPlatform);
        public bool HistoryRefreshing
        {
            get
            {
                lock (_refreshStatus)
                {
                    // History calls are currently Phantasma-only; using the aggregate state prevents
                    // UI refresh loops when CurrentPlatform points to another chain (caused stack overflows).
                    foreach (var status in _refreshStatus.Values)
                    {
                        if (status.HistoryRefreshing)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }
        public PhantasmaAPI phantasmaApi { get; private set; }

        public static PlatformKind[] AvailablePlatforms { get; private set; }
        public static PlatformKind MergeAvailablePlatforms()
        {
            var platforms = PlatformKind.None;
            foreach (var platform in AccountManager.AvailablePlatforms)
            {
                platforms |= platform;
            }
            return platforms;
        }

        private Dictionary<string, string> _currencyMap = new Dictionary<string, string>();
        public IEnumerable<string> Currencies => _currencyMap.Keys;

        public static readonly int SoulMasterStakeAmount = 50000;

        private DateTime _lastPriceUpdate = DateTime.MinValue;
        private bool tokensReinitInProgress;
        private bool refreshBalancesAfterTokenReload;
        private bool refreshBalancesAfterTokenReloadForce;
        private PlatformKind refreshBalancesAfterTokenReloadPlatforms;
        private Action refreshBalancesAfterTokenReloadCallback;

        private void Awake()
        {
            Instance = this;
            Settings = WalletRuntime.GetSettings();

            // Let Carbon description parsing lazily pull in a single token by carbon id when it
            // references a token created after the last full token-list load, instead of
            // hard-failing the WalletLink signing prompt.
            DescriptionUtils.MissingCarbonTokenLoader = TryLoadMissingTokenByCarbonIdAsync;

            Status = "Initializing wallet...";

            _currencyMap["AUD"] = "A$";
            _currencyMap["CAD"] = "C$";
            _currencyMap["EUR"] = "€";
            _currencyMap["GBP"] = "\u00A3";
            _currencyMap["RUB"] = "\u20BD";
            _currencyMap["USD"] = "$";
            _currencyMap["JPY"] = "¥";

            var platforms = new List<PlatformKind>();
            platforms.Add(PlatformKind.Phantasma);
            platforms.Add(PlatformKind.Neo);
            platforms.Add(PlatformKind.Ethereum);
            platforms.Add(PlatformKind.BSC);

            AvailablePlatforms = platforms.ToArray();

            // Ensure UI signal hub is hooked to our events as soon as AccountManager is ready.
            Poltergeist.Wallet.WalletApplicationContext.Instance?.UiSignals?.EnsureSubscribed();
        }

        public const string WalletVersionTag = "wallet.list.version";
        public const string WalletTag = "wallet.list";

        public bool ReportGetPeersFailure = false;
        public bool ReportAllRpcsUnavailabe = false;
        private int rpcNumberPhantasma; // Total number of Phantasma RPCs, received from getpeers.json.
        private int rpcBenchmarkedPhantasma; // Number of Phantasma RPCs which speed already measured.
        public int rpcAvailablePhantasma = 0;
        private class RpcBenchmarkData
        {
            public string Url;
            public bool ConnectionError;
            public TimeSpan ResponseTime;

            public RpcBenchmarkData(string url, bool connectionError, TimeSpan responseTime)
            {
                Url = url;
                ConnectionError = connectionError;
                ResponseTime = responseTime;
            }
        }
        private List<RpcBenchmarkData> rpcResponseTimesPhantasma = new List<RpcBenchmarkData>();

        public static readonly int PasswordIterations = 100000;
        private static readonly int PasswordSaltByteSize = 64;
        private static readonly int PasswordHashByteSize = 32;
        public bool accountBalanceNotLoaded = true;
        public bool accountHistoryNotLoaded = true;

        public void SelectAccount(int index)
        {
            if (_selectedAccountIndex != index)
            {
                AdvanceAccountSession();
            }

            _selectedAccountIndex = index;
            CurrentPasswordHash = "";

            var platforms = CurrentAccount.platforms.Split();

            // We should add Ethereum platform to old accounts.
            if (!platforms.Contains(PlatformKind.Ethereum))
            {
                var account = Accounts[_selectedAccountIndex];
                account.platforms |= PlatformKind.Ethereum;
                Accounts[_selectedAccountIndex] = account;

                _states[PlatformKind.Ethereum] = new AccountState()
                {
                    platform = PlatformKind.Ethereum,
                    address = GetAddress(CurrentIndex, PlatformKind.Ethereum),
                    balances = new Balance[0],
                    flags = AccountFlags.None,
                    name = ValidationUtils.ANONYMOUS_NAME,
                };

                SaveAccounts();

                platforms.Add(PlatformKind.Ethereum);
            }

            // We should add BinanceSmartChain platform to old accounts.
            if (!platforms.Contains(PlatformKind.BSC))
            {
                var account = Accounts[_selectedAccountIndex];
                account.platforms |= PlatformKind.BSC;
                Accounts[_selectedAccountIndex] = account;

                _states[PlatformKind.BSC] = new AccountState()
                {
                    platform = PlatformKind.BSC,
                    address = GetAddress(CurrentIndex, PlatformKind.BSC),
                    balances = new Balance[0],
                    flags = AccountFlags.None,
                    name = ValidationUtils.ANONYMOUS_NAME,
                };

                SaveAccounts();

                platforms.Add(PlatformKind.BSC);
            }

            if (!platforms.Contains(PlatformKind.Neo))
            {
                var account = Accounts[_selectedAccountIndex];
                account.platforms |= PlatformKind.Neo;
                Accounts[_selectedAccountIndex] = account;

                _states[PlatformKind.Neo] = new AccountState()
                {
                    platform = PlatformKind.Neo,
                    address = GetAddress(CurrentIndex, PlatformKind.Neo),
                    balances = new Balance[0],
                    flags = AccountFlags.None,
                    name = ValidationUtils.ANONYMOUS_NAME,
                };

                SaveAccounts();

                platforms.Add(PlatformKind.Neo);
            }

            CurrentPlatform = platforms.FirstOrDefault();
            _states.Clear();
            lock (_nftCacheLock)
            {
                _nfts.Clear();
                _roms.Clear();
            }
            ResetNftSession();
            _history.Clear(); // Drop cached history so a newly selected wallet never shows transactions from a previous session.
            _refreshStatus.Clear(); // Reset refresh flags/errors to avoid carrying over stale state between wallets.

            accountBalanceNotLoaded = true;
            accountHistoryNotLoaded = true;
        }

        public void UnselectAcount()
        {
            if (_selectedAccountIndex != -1)
            {
                AdvanceAccountSession();
            }

            _selectedAccountIndex = -1;

            // revoke all dapps connected to this account via Phantasma Link
            if (_states.ContainsKey(PlatformKind.Phantasma))
            {
                var link = LinkConnectorHost.Instance.PhantasmaLink;

                var state = _states[PlatformKind.Phantasma];
                foreach (var entry in state.dappTokens)
                {
                    link.Revoke(entry.Key, entry.Value);
                }

                // Revoke v5 Link sessions bound to this account so a logged-out account's
                // dApp sessions cannot resume.
                LinkConnectorHost.Instance?.RevokeAccountSessions(state.address);
            }

            _states.Clear();
            lock (_nftCacheLock)
            {
                _nfts.Clear();
                _roms.Clear();
            }
            ResetNftSession();
            _history.Clear(); // Ensure no history entries leak into the next wallet session.
            TtrsStore.Clear();
            GameStore.Clear();
            NftImages.Clear();
            _refreshStatus.Clear();
        }

        private const int maxChecks = 12; // Timeout after 36 seconds

        public void BlankState()
        {
            // Drop any cached per-platform state and recreate an empty, anonymous one (no balances,
            // no flags) for every platform the current account supports, ahead of a fresh load.
            _states.Clear();
            foreach (var platform in CurrentAccount.platforms.Split())
            {
                _states[platform] = new AccountState()
                {
                    platform = platform,
                    address = GetAddress(CurrentIndex, platform),
                    balances = Array.Empty<Balance>(),
                    flags = AccountFlags.None,
                    name = ValidationUtils.ANONYMOUS_NAME,
                };
            }
        }

        internal void InitDemoAccounts(NexusKind nexusKind)
        {
            var accounts = new List<Account>();

            this.Accounts = accounts;
            SaveAccounts();
        }

        internal void DeleteAll()
        {
            this.Accounts = new List<Account>();
        }

        public void UpdateOpenAccount()
        {
            var neoKeys = PhantasmaPhoenix.InteropChains.Legacy.Neo2.NeoKeys.FromWIF(CurrentWif);
            var SelectedAccount = CurrentAccount;
            SelectedAccount.neoAddressN3 = neoKeys.AddressN3;
            SelectedAccount.neoAddress = neoKeys.Address;
            SelectedAccount.version = 3;
            Accounts[CurrentIndex] = SelectedAccount;
            SaveAccounts();
        }
    }
}
