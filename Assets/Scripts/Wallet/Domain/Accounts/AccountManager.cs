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
    public class AccountManager : MonoBehaviour
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

        public string GetTokenWorth(string symbol, BigInteger amount, uint decimals)
        {
            bool hasLocalCurrency = !string.IsNullOrEmpty(CurrentTokenCurrency) && _currencyMap.ContainsKey(CurrentTokenCurrency);
            if (!_tokenPrices.ContainsKey(symbol) || !hasLocalCurrency)
            {
                return null;
            }

            // First try the exact conversion. If the token uses an extreme decimals value (for example 64), the
            // exact decimal path can reject it because decimal cannot represent the intermediate 10^decimals scale.
            // In that case we degrade to a bounded approximation that is sufficient for a short fiat estimate.
            if (!WalletAmountFormatter.TryToDecimal(amount, decimals, out var decimalAmount) &&
                !WalletAmountFormatter.TryToApproxDecimal(amount, decimals, 18, out decimalAmount))
            {
                return null;
            }

            var price = _tokenPrices[symbol] * decimalAmount;
            var ch = _currencyMap[CurrentTokenCurrency];
            return $"{WalletAmountFormatter.Format(price, MoneyFormatType.Short)} {ch}";
        }

        private async Task FetchTokenPricesAsync(IEnumerable<TokenResult> tokens, string currency, CancellationToken cancellationToken)
        {
            var separator = "%2C";
            var url = "https://api.coingecko.com/api/v3/simple/price?ids=" + string.Join(separator, tokens.Where(x => Tokens.HasCGSymbol(x)).Select(x => Tokens.GetCGSymbol(x)).Distinct().ToList()) + "&vs_currencies=" + currency;
            try
            {
                var response = await WebClientAsync.GetAsync<Dictionary<string, Dictionary<string, decimal>>>(
                    url,
                    WebClient.DefaultTimeout,
                    NetworkRetryPolicy.Retries,
                    NetworkRetryPolicy.RetryDelay,
                    cancellationToken);
                foreach (var token in tokens)
                {
                    var cgSymbol = Tokens.GetCGSymbol(token);
                    var node = response.Where(x => x.Key.ToUpperInvariant() == cgSymbol.ToUpperInvariant()).Select(x => x.Value).FirstOrDefault();
                    if (node != default)
                    {
                        var price = node.Where(x => x.Key.ToUpperInvariant() == currency.ToUpperInvariant()).Select(x => x.Value).FirstOrDefault();

                        SetTokenPrice(token.Symbol, price);
                    }
                    else
                    {
                        Log.Write($"Cannot get price for '{cgSymbol}'.");
                    }
                }

                // GOATI token price is pegged to 0.1$.
                SetTokenPrice("GOATI", Convert.ToDecimal(0.1));

                // Prices updated: refresh balances so fiat values appear without manual actions.
                if (HasSelection)
                {
                    RefreshBalances(false);
                }
            }
            catch (Exception e)
            {
                Log.WriteWarning(e.ToString());
            }
        }

        private void SetTokenPrice(string symbol, decimal price)
        {
            Log.Write($"Got price for {symbol} => {price}");
            _tokenPrices[symbol] = price;
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

        private string GetFastestWorkingRPCURL(out TimeSpan responseTime)
        {
            string fastestRpcUrl = null;

            responseTime = TimeSpan.Zero;

            foreach (var rpcResponseTime in rpcResponseTimesPhantasma)
            {
                if (!rpcResponseTime.ConnectionError && String.IsNullOrEmpty(fastestRpcUrl))
                {
                    // At first just initializing with first working RPC.
                    fastestRpcUrl = rpcResponseTime.Url;
                    responseTime = rpcResponseTime.ResponseTime;
                }
                else if (!rpcResponseTime.ConnectionError && rpcResponseTime.ResponseTime < responseTime)
                {
                    // Faster RPC found, switching.
                    fastestRpcUrl = rpcResponseTime.Url;
                    responseTime = rpcResponseTime.ResponseTime;
                }
            }
            return fastestRpcUrl;
        }

        public void UpdateRPCURL()
        {
            async Task ExecuteAsync()
            {
                if (Settings.nexusKind == NexusKind.Dev_Net)
                {
                    rpcAvailablePhantasma = 1;
                    return;
                }

                if (Settings.nexusKind != NexusKind.Main_Net && Settings.nexusKind != NexusKind.Test_Net)
                {
                    rpcAvailablePhantasma = 1;
                    return; // No need to change RPC, it is set by custom settings.
                }

                string url = Settings.nexusKind == NexusKind.Main_Net
                    ? "https://peers.phantasma.info/mainnet-getpeers.json"
                    : "https://peers.phantasma.info/testnet-getpeers.json";

                rpcBenchmarkedPhantasma = 0;
                rpcResponseTimesPhantasma = new List<RpcBenchmarkData>();

                try
                {
                    var response = await WebClientAsync.GetAsync<JToken>(
                        url,
                        WebClient.DefaultTimeout,
                        NetworkRetryPolicy.Retries,
                        NetworkRetryPolicy.RetryDelay,
                        CancellationToken.None);
                    if (response != null)
                    {
                        rpcNumberPhantasma = response.Count();

                        if (String.IsNullOrEmpty(Settings.phantasmaRPCURL))
                        {
                            var index = ((int)(Time.realtimeSinceStartup * 1000)) % rpcNumberPhantasma;
                            var node = response[index];
                            var result = node.Value<string>("url") + "/rpc";
                            Settings.phantasmaRPCURL = result;
                            Log.Write($"Changed Phantasma RPC url {index} => {result}");
                        }

                        UpdateAPIs();

                        var benchmarkTasks = new List<Task>();
                        foreach (var node in response.Children())
                        {
                            var rpcUrl = node.Value<string>("url") + "/rpc";
                            benchmarkTasks.Add(BenchmarkRpcAsync(rpcUrl));
                        }

                        await Task.WhenAll(benchmarkTasks);

                        if (rpcBenchmarkedPhantasma == rpcNumberPhantasma)
                        {
                            TimeSpan bestTime;
                            string bestRpcUrl = GetFastestWorkingRPCURL(out bestTime);

                            if (String.IsNullOrEmpty(bestRpcUrl))
                            {
                                ReportAllRpcsUnavailabe = true;
                                Log.WriteWarning("All Phantasma RPC servers are unavailable. Please check your network connection.");
                            }
                            else
                            {
                                Log.Write($"Fastest Phantasma RPC is {bestRpcUrl}: {new DateTime(bestTime.Ticks).ToString("ss.fff")} sec.");
                                Settings.phantasmaRPCURL = bestRpcUrl;
                                UpdateAPIs();
                                Settings.SaveOnExit();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ReportGetPeersFailure = true;
                    Log.Write($"Couldn't retrieve RPCs list using url '{url}', error: " + ex.Message);
                }
            }

            // Kick off the async refresh without recursive calls to avoid starving the thread pool.
            ExecuteAsync().Forget(LogTaskException);
        }

        private async Task BenchmarkRpcAsync(string rpcUrl)
        {
            // Benchmark should include retry delays so slow/flaky endpoints score worse.
            var startedAt = DateTime.UtcNow;
            var remainingRetries = NetworkRetryPolicy.Retries;

            try
            {
                while (true)
                {
                    try
                    {
                        await AsyncPhantasma.FromApi<TimeSpan>(
                            (onSuccess, onError) => WebClient.Ping(rpcUrl, onError, onSuccess),
                            CancellationToken.None);

                        var totalTime = DateTime.UtcNow - startedAt;
                        lock (rpcResponseTimesPhantasma)
                        {
                            rpcResponseTimesPhantasma.Add(new RpcBenchmarkData(rpcUrl, false, totalTime));
                        }

                        Interlocked.Increment(ref rpcAvailablePhantasma);
                        break;
                    }
                    catch (PhantasmaRequestException ex) when (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR && remainingRetries > 0)
                    {
                        remainingRetries--;
                        if (NetworkRetryPolicy.RetryDelay > TimeSpan.Zero)
                        {
                            await Task.Delay(NetworkRetryPolicy.RetryDelay);
                        }
                    }
                }
            }
            catch (PhantasmaRequestException ex)
            {
                Log.Write("Ping error: " + ex.Message);

                lock (rpcResponseTimesPhantasma)
                {
                    rpcResponseTimesPhantasma.Add(new RpcBenchmarkData(rpcUrl, true, new TimeSpan()));
                }
            }
            finally
            {
                Interlocked.Increment(ref rpcBenchmarkedPhantasma);
            }
        }
        private void RotateRpcOnWebError(PhantasmaRequestException ex)
        {
            if (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
            {
                ChangeFaultyRPCURL(PlatformKind.Phantasma);
            }
        }

        public void ChangeFaultyRPCURL(PlatformKind platformKind)
        {
            if (Settings.nexusKind != NexusKind.Main_Net ||
                (platformKind == PlatformKind.BSC && Settings.nexusKind != NexusKind.Main_Net && Settings.nexusKind != NexusKind.Test_Net))
            {
                return; // Fallback works only for mainnet or BSC testnet.
            }

            if (platformKind == PlatformKind.Phantasma)
            {
                Log.Write($"Changing faulty Phantasma RPC {Settings.phantasmaRPCURL}.");

                // Now we have one less working RPC.
                if (rpcAvailablePhantasma > 0)
                    rpcAvailablePhantasma--;

                // Marking faulty RPC.
                var currentRpc = rpcResponseTimesPhantasma.Find(x => x.Url == Settings.phantasmaRPCURL);
                if (currentRpc != null)
                    currentRpc.ConnectionError = true;

                // Switching to working RPC.
                TimeSpan bestTime;
                string bestRpcUrl = GetFastestWorkingRPCURL(out bestTime);

                if (String.IsNullOrEmpty(bestRpcUrl))
                {
                    ReportAllRpcsUnavailabe = true;
                    Log.WriteWarning("All Phantasma RPC servers are unavailable. Please check your network connection.");
                }
                else
                {
                    Log.Write($"Next fastest Phantasma RPC is {bestRpcUrl}: {new DateTime(bestTime.Ticks).ToString("ss.fff")} sec.");
                    Settings.phantasmaRPCURL = bestRpcUrl;
                    UpdateAPIs();
                }
            }
        }
        public static readonly int PasswordIterations = 100000;
        private static readonly int PasswordSaltByteSize = 64;
        private static readonly int PasswordHashByteSize = 32;
        public static void GetPasswordHash(string password, int passwordIterations, out string salt, out string passwordHash)
        {
            BouncyCastleHashing hashing = new BouncyCastleHashing();
            salt = Convert.ToBase64String(hashing.CreateSalt(PasswordSaltByteSize));
            passwordHash = hashing.PBKDF2_SHA256_GetHash(password, salt, passwordIterations, PasswordHashByteSize);
        }
        public static void GetPasswordHashBySalt(string password, int passwordIterations, string salt, out string passwordHash)
        {
            BouncyCastleHashing hashing = new BouncyCastleHashing();
            passwordHash = hashing.PBKDF2_SHA256_GetHash(password, salt, passwordIterations, PasswordHashByteSize);
        }
        public static string EncryptString(string stringToEncrypt, string key, out string iv)
        {
            var ivBytes = new byte[16];

            //Set up
            var keyParam = new Org.BouncyCastle.Crypto.Parameters.KeyParameter(Convert.FromBase64String(key));

            var secRandom = new Org.BouncyCastle.Security.SecureRandom();
            secRandom.NextBytes(ivBytes);

            var keyParamWithIV = new Org.BouncyCastle.Crypto.Parameters.ParametersWithIV(keyParam, ivBytes, 0, 16);

            var engine = new Org.BouncyCastle.Crypto.Engines.AesEngine();
            var blockCipher = new Org.BouncyCastle.Crypto.Modes.CbcBlockCipher(engine); //CBC
            var cipher = new Org.BouncyCastle.Crypto.Paddings.PaddedBufferedBlockCipher(blockCipher); //Default scheme is PKCS5/PKCS7

            // Encrypt
            cipher.Init(true, keyParamWithIV);
            var inputBytes = System.Text.Encoding.UTF8.GetBytes(stringToEncrypt);
            var outputBytes = new byte[cipher.GetOutputSize(inputBytes.Length)];
            var length = cipher.ProcessBytes(inputBytes, outputBytes, 0);
            cipher.DoFinal(outputBytes, length); //Do the final block

            iv = Convert.ToBase64String(ivBytes);
            return Convert.ToBase64String(outputBytes);
        }
        public static string DecryptString(string stringToDecrypt, string key, string iv)
        {
            //Set up
            var keyParam = new Org.BouncyCastle.Crypto.Parameters.KeyParameter(Convert.FromBase64String(key));
            var ivBytes = Convert.FromBase64String(iv);
            var keyParamWithIV = new Org.BouncyCastle.Crypto.Parameters.ParametersWithIV(keyParam, ivBytes, 0, 16);

            var engine = new Org.BouncyCastle.Crypto.Engines.AesEngine();
            var blockCipher = new Org.BouncyCastle.Crypto.Modes.CbcBlockCipher(engine); //CBC
            var cipher = new Org.BouncyCastle.Crypto.Paddings.PaddedBufferedBlockCipher(blockCipher);

            cipher.Init(false, keyParamWithIV);
            var inputBytes = Convert.FromBase64String(stringToDecrypt);
            var resultExtraSize = new byte[cipher.GetOutputSize(inputBytes.Length)];
            var length = cipher.ProcessBytes(inputBytes, resultExtraSize, 0);
            length += cipher.DoFinal(resultExtraSize, length); //Do the final block

            var result = new byte[length];
            Array.Copy(resultExtraSize, result, length);

            return System.Text.Encoding.UTF8.GetString(result);
        }

        // Start is called before the first frame update
        void Start()
        {
            Settings.Load();

            if (AccountManager.Instance.Settings.initialWindowWidth > 0 && AccountManager.Instance.Settings.initialWindowHeight > 0)
            {
                Screen.SetResolution(AccountManager.Instance.Settings.initialWindowWidth, AccountManager.Instance.Settings.initialWindowHeight, false);
            }

            UpdateRPCURL();

            LoadNexus();

            // Version 1 - original account version used in PG up to version 1.9.
            // Version 2 - new account version.
            // var walletVersion = PlayerPrefs.GetInt(WalletVersionTag, 1);

            var wallets = PlayerPrefs.GetString(WalletTag, "");
            Accounts = new List<Account>();

            if (!string.IsNullOrEmpty(wallets))
            {
                var bytes = Base16.Decode(wallets);
                try
                {
                    List<Account> accountsTemp = new List<Account>();
                    var reader = new BinaryReader(new MemoryStream(bytes));
                    var size = reader.ReadVarInt();
                    for (int i = 0; i < (int)size; i++)
                    {
                        var account = new Account();
                        account.UnserializeData(reader);
                        accountsTemp.Add(account);
                    }

                    Accounts = accountsTemp; //  = Serialization.Unserialize<Account[]>(bytes).ToList();
                }
                catch (Exception e)
                {
                    Log.WriteFatalError("Error deserializing accounts: " + e);
                }
            }

            AccountsAreReadyToBeUsed = true;
            LoadHiddenWallets();

            if (Settings.lastShownInformationScreen == 0)
            {
                Settings.lastShownInformationScreen = 1;

                WalletApplicationContext.Instance.Messages.Push(@"A note for existing Poltergeist wallet users!

If you already have a previous (older, not 'Light') version of Poltergeist installed on your device, then you will need to:

1. Open your previous version of Poltergeist
2. Export your wallets onto your clipboard:
  * Press 'Manage' button available on main screen
  * Press 'Export' button and enter password to encrypt exported accounts, press 'Confirm'
3. Close old app
4. Open the new version of Poltergeist Lite
5. Import wallets data:
  * Press 'Manage' on main screen and then press 'Import'. Paste exported accounts from clipboard and press 'Confirm'. You will need to enter password which you used in the previous step. You will be presented with a list of accounts being imported, press 'Confirm'

Happy Poltergeisting!

Regards,
The Phoenix team", "Notice");
            }
        }

        public void SaveAccounts()
        {
            PlayerPrefs.SetInt(WalletVersionTag, 3);
            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);
            Accounts.ForEach(acc => acc.version = 3);

            writer.WriteVarInt(Accounts.Count);
            foreach (var account in Accounts)
            {
                account.SerializeData(writer);
            }

            var bytes = stream.ToArray();//Serialization.Serialize(Accounts.ToArray());
            PlayerPrefs.SetString(WalletTag, Base16.Encode(bytes));
            PruneHiddenWallets();
            SaveHiddenWalletsInternal(false);
            PlayerPrefs.Save();
        }

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

        private async Task<TokenResult[]> GetTokensAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                try
                {
                    return await AsyncPhantasma.FromApi<TokenResult[]>(
                        (onSuccess, onError) => phantasmaApi.GetTokens(true, onSuccess, onError, 10, NetworkRetryPolicy.Retries),
                        cancellationToken);
                }
                catch (PhantasmaRequestException ex)
                {
                    if (rpcAvailablePhantasma > 0 && Settings.nexusKind == NexusKind.Main_Net && ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                    {
                        ChangeFaultyRPCURL(PlatformKind.Phantasma);
                        continue;
                    }

                    CurrentTokenCurrency = "";
                    Settings.settingRequireReconfiguration = true;
                    Status = "ok"; // We are launching with uninitialized tokens,
                                   // to allow user to edit settings.

                    Log.WriteWarning("Error: Launching with uninitialized tokens.");
                    Log.WriteWarning("Tokens initialization error: " + ex.Message);
                    return Array.Empty<TokenResult>();
                }
            }
        }

        // Lazily fetches a single token by its Carbon id and adds it to the in-memory token list.
        // Used when Carbon description parsing references a token created after the wallet last
        // loaded its full token list (e.g. a brand-new marketplace listing currency), so a single
        // new token does not hard-block the WalletLink signing prompt. Returns true once the token
        // is available; false leaves the original token mapping error to surface.
        public async Task<bool> TryLoadMissingTokenByCarbonIdAsync(ulong carbonId, CancellationToken cancellationToken)
        {
            // Another path may have already loaded it (e.g. a concurrent token reinit).
            lock (Tokens.__lockObj)
            {
                if (Tokens.TryGetTokenByCarbonId(carbonId, out _))
                {
                    return true;
                }
            }

            if (phantasmaApi == null)
            {
                return false;
            }

            TokenResult token;
            try
            {
                // An empty symbol selects the token by its Carbon id (see PhantasmaAPI.GetToken).
                token = await AsyncPhantasma.FromApi<TokenResult>(
                    (onSuccess, onError) => phantasmaApi.GetToken("", true, carbonId, onSuccess, onError, WebClient.DefaultTimeout, NetworkRetryPolicy.Retries),
                    cancellationToken);
            }
            catch (PhantasmaRequestException ex)
            {
                Log.WriteWarning($"Lazy token fetch by carbon id {carbonId} failed: {ex.Message}");
                return false;
            }

            // Guard against a node returning an unexpected or mismatched token.
            if (token == null || string.IsNullOrWhiteSpace(token.CarbonId) ||
                !ulong.TryParse(token.CarbonId, out var parsed) || parsed != carbonId)
            {
                return false;
            }

            lock (Tokens.__lockObj)
            {
                // Re-check after the await in case the token was added meanwhile, to avoid a duplicate.
                if (Tokens.TryGetTokenByCarbonId(carbonId, out _))
                {
                    return true;
                }

                Tokens.AddToken(token);
            }

            return true;
        }

        private void TokensReinit()
        {
            if (tokensReinitInProgress)
                return;

            TokensReinitRoutineAsync(CancellationToken.None).Forget(LogTaskException);
        }

        private async Task TokensReinitRoutineAsync(CancellationToken cancellationToken)
        {
            tokensReinitInProgress = true;
            var tokens = await GetTokensAsync(cancellationToken);
            if (tokens.Length > 0)
            {
                lock (Tokens.__lockObj)
                {
                    Tokens.Init(tokens);
                }

                if (TextureProvider.Instance != null)
                {
                    TextureProvider.Instance.UnloadTokens();
                }

            }

            CurrentTokenCurrency = "";

            Status = "ok";
            tokensReinitInProgress = false;

            // Token metadata is ready again; refresh prices so fiat values can be shown promptly.
            RefreshTokenPrices();

            if (refreshBalancesAfterTokenReload)
            {
                var refreshForce = refreshBalancesAfterTokenReloadForce;
                var refreshPlatforms = refreshBalancesAfterTokenReloadPlatforms;
                var refreshCallback = refreshBalancesAfterTokenReloadCallback;
                refreshBalancesAfterTokenReload = false;
                refreshBalancesAfterTokenReloadForce = false;
                refreshBalancesAfterTokenReloadPlatforms = PlatformKind.None;
                refreshBalancesAfterTokenReloadCallback = null;

                if (HasSelection)
                {
                    RefreshBalances(refreshForce, refreshPlatforms, refreshCallback);
                }
            }
        }

        public void RequestTokensReload()
        {
            ScheduleBalanceRefreshAfterTokens();
            TokensReinit();
        }

        private void ScheduleBalanceRefreshAfterTokens(bool force = true, PlatformKind platforms = PlatformKind.None, Action callback = null)
        {
            refreshBalancesAfterTokenReload = true;
            refreshBalancesAfterTokenReloadForce = force || refreshBalancesAfterTokenReloadForce;
            // If caller specifies a platform set, keep the most recent explicit one; otherwise leave previous value.
            if (platforms != PlatformKind.None)
            {
                refreshBalancesAfterTokenReloadPlatforms = platforms;
            }

            if (callback != null)
            {
                refreshBalancesAfterTokenReloadCallback += callback;
            }
        }

        public void RefreshTokenPrices()
        {
            bool needRefresh = false;

            if (CurrentTokenCurrency != Settings.currency)
            {
                needRefresh = true;
            }
            else
            {
                var diff = DateTime.UtcNow - _lastPriceUpdate;
                if (diff.TotalMinutes >= 5)
                {
                    needRefresh = true;
                }
            }


            if (needRefresh)
            {
                CurrentTokenCurrency = Settings.currency;
                _lastPriceUpdate = DateTime.UtcNow;

                FetchTokenPricesAsync(Tokens.GetTokensForCoingecko(), CurrentTokenCurrency, CancellationToken.None).Forget(LogTaskException);
            }
        }

        public void UpdateAPIs(bool possibleNexusChange = false)
        {
            Log.Write("reinit APIs => " + Settings.phantasmaRPCURL);
            phantasmaApi = new PhantasmaAPI(Settings.phantasmaRPCURL);

            if (possibleNexusChange)
            {
                // Network switch: reload token list and ensure balances retry once tokens are back.
                ScheduleBalanceRefreshAfterTokens();
                TokensReinit();
            }
        }

        private void LoadNexus()
        {
            UpdateAPIs(true);
        }

        private static BigInteger ParseTokenAmount(string amount, uint decimals)
        {
            if (string.IsNullOrWhiteSpace(amount))
            {
                return BigInteger.Zero;
            }

            if (amount.Contains(".") || amount.Contains(","))
            {
                throw new FormatException($"Unexpected fractional amount '{amount}' for token with {decimals} decimals.");
            }

            if (!BigInteger.TryParse(amount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
            {
                throw new FormatException($"Cannot parse amount '{amount}'");
            }

            return raw;
        }

        public void SignAndSendTransaction(string chain, byte[] script, byte[] payload, Action<Hash, string> callback, Func<byte[], byte[], byte[], byte[]> customSignFunction = null)
        {
            async Task ExecuteAsync()
            {
                if (payload == null)
                {
                    payload = System.Text.Encoding.UTF8.GetBytes(WalletIdentifier);
                }

                switch (CurrentPlatform)
                {
                    case PlatformKind.Phantasma:
                        {
                            try
                            {
                                var result = await AsyncPhantasma.FromApi(
                                    (Action<string, string> onSuccess, Action<EPHANTASMA_SDK_ERROR_TYPE, string> onError) =>
                                        phantasmaApi.SignAndSendTransaction(
                                            PhantasmaKeys.FromWIF(CurrentWif),
                                            Settings.nexusName,
                                            script,
                                            chain,
                                            payload,
                                            onSuccess,
                                            onError,
                                            customSignFunction,
                                            timeout: WebClient.DefaultTimeout,
                                            retries: NetworkRetryPolicy.Retries),
                                    CancellationToken.None);

                                var hashText = result.Item1;
                                var encodedTx = result.Item2;

                                if (Settings.devMode)
                                {
                                    Log.Write($"SignAndSendTransactionWithPayload(): Encoded tx: {encodedTx}");
                                }

                                if (!string.IsNullOrEmpty(hashText))
                                {
                                    try
                                    {
                                        callback(Hash.Parse(hashText), null);
                                    }
                                    catch (Exception e)
                                    {
                                        Log.WriteWarning("Error parsing hash: " + e.Message);
                                        callback(Hash.Null, $"Error: hashText={hashText}");
                                    }
                                }
                                else
                                {
                                    callback(Hash.Null, "Failed to send transaction");
                                }
                            }
                            catch (PhantasmaRequestException ex)
                            {
                                RotateRpcOnWebError(ex);

                                callback(Hash.Null, ex.Message);
                            }

                            break;
                        }

                    default:
                        {
                            callback(Hash.Null, "not implemented for " + CurrentPlatform);
                            break;
                        }
                }

            }

            ExecuteAsync().Forget(LogTaskException);
        }

        public void SignAndSendCarbonTransaction(TxMsg tx, Action<Hash, string> callback)
        {
            async Task ExecuteAsync()
            {
                switch (CurrentPlatform)
                {
                    case PlatformKind.Phantasma:
                        {
                            try
                            {
                                var result = await AsyncPhantasma.FromApi(
                                    (Action<string, string> onSuccess, Action<EPHANTASMA_SDK_ERROR_TYPE, string> onError) =>
                                        phantasmaApi.SignAndSendCarbonTransaction(
                                            PhantasmaKeys.FromWIF(CurrentWif),
                                            tx,
                                            onSuccess,
                                            onError,
                                            timeout: WebClient.DefaultTimeout,
                                            retries: NetworkRetryPolicy.Retries),
                                    CancellationToken.None);

                                var hashText = result.Item1;
                                var encodedTx = result.Item2;

                                if (Settings.devMode)
                                {
                                    Log.Write($"SignAndSendCarbonTransaction(): Encoded tx: {encodedTx}");
                                }

                                if (!string.IsNullOrEmpty(hashText))
                                {
                                    try
                                    {
                                        callback(Hash.Parse(hashText), null);
                                    }
                                    catch (Exception e)
                                    {
                                        Log.WriteWarning("Error parsing hash: " + e.Message);
                                        callback(Hash.Null, $"Error: hashText={hashText}");
                                    }
                                }
                                else
                                {
                                    callback(Hash.Null, "Failed to send transaction");
                                }
                            }
                            catch (PhantasmaRequestException ex)
                            {
                                RotateRpcOnWebError(ex);

                                callback(Hash.Null, ex.Message);
                            }

                            break;
                        }

                    default:
                        {
                            callback(Hash.Null, "not implemented for " + CurrentPlatform);
                            break;
                        }
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

        public void InvokeScript(string chain, byte[] script, Action<string[], string> callback)
        {
            async Task ExecuteAsync()
            {
                switch (CurrentPlatform)
                {
                    case PlatformKind.Phantasma:
                        {
                            Log.Write("InvokeScript: " + System.Text.Encoding.UTF8.GetString(script), Log.Level.Debug1);
                            try
                            {
                                var result = await AsyncPhantasma.FromApi<PhantasmaPhoenix.RPC.Models.ScriptResult>(
                                    (onSuccess, onError) => phantasmaApi.InvokeRawScript(
                                        chain,
                                        Base16.Encode(script),
                                        onSuccess,
                                        onError,
                                        timeout: WebClient.DefaultTimeout,
                                        retries: NetworkRetryPolicy.Retries),
                                    CancellationToken.None);

                                Log.Write("InvokeScript result: " + result.Result, Log.Level.Debug1);
                                callback(result.Results, null);
                            }
                            catch (PhantasmaRequestException ex)
                            {
                                RotateRpcOnWebError(ex);
                                callback(null, ex.Message);
                            }

                            break;
                        }
                    default:
                        {
                            callback(null, "not implemented for " + CurrentPlatform);
                            break;
                        }
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

        public void InvokeScriptPhantasma(string chain, byte[] script, Action<byte[], string> callback)
        {
            async Task ExecuteAsync()
            {
                Log.Write("InvokeScriptPhantasma: " + System.Text.Encoding.UTF8.GetString(script), Log.Level.Debug1);
                try
                {
                    var result = await AsyncPhantasma.FromApi<PhantasmaPhoenix.RPC.Models.ScriptResult>(
                        (onSuccess, onError) => phantasmaApi.InvokeRawScript(
                            chain,
                            Base16.Encode(script),
                            onSuccess,
                            onError,
                            timeout: WebClient.DefaultTimeout,
                            retries: NetworkRetryPolicy.Retries),
                        CancellationToken.None);
                    Log.Write("InvokeScriptPhantasma result: " + result.Result, Log.Level.Debug1);
                    callback(Base16.Decode(result.Result), null);
                }
                catch (PhantasmaRequestException ex)
                {
                    RotateRpcOnWebError(ex);
                    callback(null, ex.Message);
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

        public void WriteArchive(Hash hash, int blockIndex, byte[] data, Action<bool, string> callback)
        {
            async Task ExecuteAsync()
            {
                switch (CurrentPlatform)
                {
                    case PlatformKind.Phantasma:
                        {
                            Log.Write("WriteArchive: " + hash, Log.Level.Debug1);
                            try
                            {
                                var result = await AsyncPhantasma.FromApi<bool>(
                                    (onSuccess, onError) => phantasmaApi.WriteArchive(hash.ToString(), blockIndex, data, onSuccess, onError),
                                    CancellationToken.None);
                                Log.Write("WriteArchive result: " + result, Log.Level.Debug1);
                                callback(result, null);
                            }
                            catch (PhantasmaRequestException ex)
                            {
                                RotateRpcOnWebError(ex);
                                callback(false, ex.Message);
                            }
                            break;
                        }
                    default:
                        {
                            callback(false, "not implemented for " + CurrentPlatform);
                            break;
                        }
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

        // We use this to detect when account was just loaded
        // and needs balances/histories to be loaded.
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

        private void ReportWalletBalance(PlatformKind platform, AccountState state, AccountSession session)
        {
            try
            {
                if (!IsSessionCurrent(session, platform, state?.address))
                {
                    return;
                }

                RefreshStatus refreshStatus;
                lock (_refreshStatus)
                {
                    refreshStatus = _refreshStatus.ContainsKey(platform)
                        ? _refreshStatus[platform]
                        : new RefreshStatus();
                    refreshStatus.BalanceRefreshing = false;
                    _refreshStatus[platform] = refreshStatus;
                }

                Log.Write($"[Balances] ReportWalletBalance platform={platform} stateNull={state == null}"); //TODO Check if still needed once refactoring is over

                if (state != null)
                {
                    Log.Write("Received new state for " + platform);

                    if (_states.TryGetValue(platform, out var previousState) && previousState?.dappTokens != null)
                    {
                        foreach (var entry in previousState.dappTokens)
                        {
                            if (!state.dappTokens.ContainsKey(entry.Key))
                            {
                                state.dappTokens[entry.Key] = entry.Value;
                            }
                        }
                    }

                    _states[platform] = state;
                }

                var temp = refreshStatus.BalanceRefreshCallback;
                lock (_refreshStatus)
                {
                    refreshStatus.BalanceRefreshCallback = null;
                    _refreshStatus[platform] = refreshStatus;
                }
                temp?.Invoke();

                Log.Write($"[Balances] Invoking BalancesUpdated for {platform} (subscribers: {BalancesUpdated?.GetInvocationList()?.Length ?? 0})"); //TODO Check if still needed once refactoring is over
                BalancesUpdated?.Invoke(platform);
            }
            catch (Exception) { } // This fixes crash when user leaves account fast without waiting for balances to load
        }

        // log=false is used for progressive updates to avoid flooding the log.
        private void ReportWalletNft(PlatformKind platform, string symbol, AccountSession session, bool log = true)
        {
            if (!IsSessionCurrent(session))
            {
                return;
            }

            var normalizedSymbol = NormalizeNftSymbol(symbol);
            var key = (platform, normalizedSymbol);
            int? nftCount = null;
            if (!string.IsNullOrEmpty(normalizedSymbol))
            {
                lock (_nftCacheLock)
                {
                    if (_nfts.TryGetValue(key, out var nfts) && nfts != null)
                    {
                        nftCount = nfts.Count;
                    }
                }
            }

            if (nftCount.HasValue)
            {
                if (log)
                {
                    Log.Write($"Received {nftCount.Value} new {symbol} NFTs for {platform}");
                }

                if (CurrentPlatform == PlatformKind.None)
                {
                    CurrentPlatform = platform;
                }

                if (log)
                {
                    Log.Write($"[NFT] Invoking NftsUpdated for {platform} {symbol} (subscribers: {NftsUpdated?.GetInvocationList()?.Length ?? 0})"); //TODO Check if still needed once refactoring is over
                }
                NftsUpdated?.Invoke(platform, symbol);
            }
        }

        private void ReportWalletHistory(PlatformKind platform, List<HistoryEntry> history, AccountSession session)
        {
            try
            {
                if (!IsSessionCurrent(session))
                {
                    return;
                }

                lock (_refreshStatus)
                {
                    if (!_refreshStatus.TryGetValue(platform, out var refreshStatus))
                    {
                        refreshStatus = new RefreshStatus();
                        Log.Write($"[History] ReportWalletHistory: created missing RefreshStatus for {platform}");
                    }
                    refreshStatus.HistoryRefreshing = false;
                    _refreshStatus[platform] = refreshStatus;
                }

                Log.Write($"[History] ReportWalletHistory platform={platform} historyNull={history == null}"); //TODO Check if still needed once refactoring is over

                if (history != null)
                {
                    Log.Write("Received new history for " + platform);
                    _history[platform] = history.ToArray();

                    if (CurrentPlatform == PlatformKind.None)
                    {
                        CurrentPlatform = platform;
                    }
                }

                Log.Write($"[History] Invoking HistoryUpdated for {platform} (subscribers: {HistoryUpdated?.GetInvocationList()?.Length ?? 0})"); //TODO Check if still needed once refactoring is over
                HistoryUpdated?.Invoke(platform);
            }
            catch (Exception) { } // This fixes crash when user leaves account fast without waiting for balances to load
        }


        private const int maxChecks = 12; // Timeout after 36 seconds

        public void RequestConfirmation(string transactionHash, int checkCount, Action<TransactionResult, string> callback)
        {
            async Task ExecuteAsync()
            {
                switch (CurrentPlatform)
                {
                    case PlatformKind.Phantasma:
                        try
                        {
                            var txResult = await AsyncPhantasma.FromApi<TransactionResult>(
                                (onSuccess, onError) => phantasmaApi.GetTransaction(
                                    transactionHash,
                                    onSuccess,
                                    onError,
                                    timeout: WebClient.DefaultTimeout,
                                    retries: NetworkRetryPolicy.Retries),
                                CancellationToken.None);

                            if (txResult.State == ExecutionState.Running)
                            {
                                callback(txResult, "pending");
                            }
                            else if (txResult.State == ExecutionState.Break || txResult.State == ExecutionState.Fault)
                            {
                                if (string.IsNullOrEmpty(txResult.DebugComment) && checkCount <= 6)
                                {
                                    // We wait a bit for additional information about failure to become available
                                    callback(txResult, "pending");
                                }
                                else
                                {
                                    callback(txResult, "Transaction failed");
                                }
                            }
                            else
                            {
                                callback(txResult, null);
                            }
                        }
                        catch (PhantasmaRequestException ex)
                        {
                            RotateRpcOnWebError(ex);

                            var msg = ex.Message;
                            if (checkCount <= maxChecks)
                            {
                                if (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.FAILED_PARSING_JSON)
                                {
                                    msg = "Cannot determine if transaction was successful or not due to incorrect RPC response. " + msg;
                                }
                                else if (msg.ToUpperInvariant().Contains("PENDING") || msg.ToUpperInvariant().Contains("TRANSACTION NOT FOUND"))
                                {
                                    // If tx is PENDING or NOT FOUND, we want to wait till timeout
                                    // to ensure that no new information about tx will appear.
                                    msg = "pending";
                                }
                                callback(null, msg);
                            }
                            else
                            {
                                callback(null, "timeout");
                            }
                        }

                        break;

                    default:
                        callback(null, "not implemented: " + CurrentPlatform);
                        break;
                }
            }

            ExecuteAsync().Forget(LogTaskException);

        }

        public void RefreshBalances(bool force, PlatformKind platforms = PlatformKind.None, Action callback = null)
        {
            async Task ExecuteAsync()
            {
                if (!HasSelection)
                {
                    Log.WriteWarning("RefreshBalances: skipped because no account is selected.");
                    return;
                }

                var currentAccount = CurrentAccount;
                if (currentAccount.passwordProtected && string.IsNullOrEmpty(CurrentPasswordHash))
                {
                    Log.WriteWarning("RefreshBalances: skipped because current account is locked.");
                    return;
                }

                // Avoid refreshing while token list is being rebuilt (e.g., after network change); schedule a retry once tokens are ready.
                // We need token metadata (decimals, flags) to parse balances safely; when tokens are empty or reinit is running, bail out.
                var tokensReady = Tokens.GetTokens().Length > 0;
                if (!tokensReady || tokensReinitInProgress)
                {
                    ScheduleBalanceRefreshAfterTokens(force, platforms, callback);
                    if (!tokensReinitInProgress)
                    {
                        TokensReinit();
                    }

                    Log.Write("[Balances] Refresh skipped: tokens not ready, will retry after token reload.");
                    return;
                }

                var session = CaptureAccountSession();
                List<PlatformKind> platformsList;
                if (platforms == PlatformKind.None)
                    platformsList = currentAccount.platforms.Split();
                else
                    platformsList = platforms.Split();

                lock (_refreshStatus)
                {
                    RefreshStatus refreshStatus;
                    var now = DateTime.UtcNow;
                    if (_refreshStatus.ContainsKey(PlatformKind.Phantasma))
                    {
                        refreshStatus = _refreshStatus[PlatformKind.Phantasma];

                        refreshStatus.BalanceRefreshing = true;
                        refreshStatus.LastBalanceRefresh = now;
                        refreshStatus.BalanceRefreshCallback = callback;
                        refreshStatus.BalanceError = null;

                        _refreshStatus[PlatformKind.Phantasma] = refreshStatus;
                    }
                    else
                    {
                        _refreshStatus.Add(PlatformKind.Phantasma,
                            new RefreshStatus
                            {
                                BalanceRefreshing = true,
                                LastBalanceRefresh = now,
                                BalanceRefreshCallback = callback,
                                BalanceError = null,
                                HistoryRefreshing = false,
                                LastHistoryRefresh = DateTime.MinValue
                            });
                    }
                }

                Log.Write($"[Balances] RefreshBalances start force={force} currentPlatform={CurrentPlatform} targets={string.Join(',', platformsList)}"); //TODO Check if still needed once refactoring is over
                foreach (var platform in platformsList)
                {
                    BalancesRefreshStarted?.Invoke(platform);
                }

                var wif = CurrentWif;
                var keys = PhantasmaKeys.FromWIF(wif);
                var ethKeys = PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.FromWIF(wif);
                UpdateOpenAccount();
                try
                {
                    var acc = await AsyncPhantasma.FromApi<PhantasmaPhoenix.RPC.Models.AccountResult>(
                        (onSuccess, onError) => phantasmaApi.GetAccount(
                            keys.Address.Text,
                            onSuccess,
                            onError,
                            timeout: WebClient.DefaultTimeout,
                            retries: NetworkRetryPolicy.Retries),
                        session.Token);

                    if (!IsSessionCurrent(session, PlatformKind.Phantasma, acc?.Address))
                    {
                        return;
                    }

                    var balanceMap = new Dictionary<string, Balance>();
                    HashSet<string> missingTokens = null;

                    foreach (var entry in acc.Balances)
                    {
                        var token = Tokens.GetToken(entry.Symbol, PlatformKind.Phantasma);
                        var decimals = token?.Decimals ?? 8;
                        var availableAmount = ParseTokenAmount(entry.Amount, decimals);

                        if (token == null)
                        {
                            missingTokens ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            missingTokens.Add(entry.Symbol);
                        }

                        balanceMap[entry.Symbol] = new Balance()
                        {
                            Symbol = entry.Symbol,
                            Available = availableAmount,
                            Staked = BigInteger.Zero,
                            Claimable = BigInteger.Zero,
                            Chain = entry.Chain,
                            Decimals = decimals,
                            Burnable = token?.IsBurnable() ?? true,
                            Fungible = token?.IsFungible() ?? true,
                            Ids = entry.Ids
                        };

                    }

                    var soulDecimals = Tokens.GetTokenDecimals("SOUL", PlatformKind.Phantasma);
                    var kcalDecimals = Tokens.GetTokenDecimals("KCAL", PlatformKind.Phantasma);

                    var stakedAmount = ParseTokenAmount(acc.Stakes.Amount, soulDecimals);
                    var claimableAmount = ParseTokenAmount(acc.Stakes.Unclaimed, kcalDecimals);

                    var stakeTimestamp = new Timestamp(acc.Stakes.Time);

                    if (stakedAmount > 0)
                    {
                        var symbol = "SOUL";
                        if (balanceMap.ContainsKey(symbol))
                        {
                            var entry = balanceMap[symbol];
                            entry.Staked = stakedAmount;
                        }
                        else
                        {
                            var token = Tokens.GetToken(symbol, PlatformKind.Phantasma);
                            var entry = new Balance()
                            {
                                Symbol = symbol,
                                Chain = "main",
                                Staked = stakedAmount,
                                Claimable = BigInteger.Zero,
                                Decimals = token.Decimals,
                                Burnable = token.IsBurnable(),
                                Fungible = token.IsFungible()
                            };
                            balanceMap[symbol] = entry;
                        }
                    }

                    if (claimableAmount > 0)
                    {
                        var symbol = "KCAL";
                        if (balanceMap.ContainsKey(symbol))
                        {
                            var entry = balanceMap[symbol];
                            entry.Claimable = claimableAmount;
                        }
                        else
                        {
                            var token = Tokens.GetToken(symbol, PlatformKind.Phantasma);
                            var entry = new Balance()
                            {
                                Symbol = symbol,
                                Chain = "main",
                                Staked = BigInteger.Zero,
                                Claimable = claimableAmount,
                                Decimals = token.Decimals,
                                Burnable = token.IsBurnable(),
                                Fungible = token.IsFungible()
                            };
                            balanceMap[symbol] = entry;
                        }
                    }

                    balanceMap = balanceMap
                        .OrderBy(b => b.Key != "SOUL")
                        .ThenBy(b => b.Key != "KCAL")
                        .ThenBy(b => b.Key)
                        .ToDictionary(kv => kv.Key, kv => kv.Value);

                    var state = new AccountState()
                    {
                        platform = PlatformKind.Phantasma,
                        address = acc.Address,
                        name = acc.Name,
                        balances = balanceMap.Values.ToArray(),
                        flags = AccountFlags.None
                    };

                    var soulMasterThreshold = WalletAmountParser.FromDecimal(SoulMasterStakeAmount, soulDecimals);
                    if (stakedAmount >= soulMasterThreshold)
                    {
                        state.flags |= AccountFlags.Master;
                    }

                    if (acc.Validator.Equals("Primary") || acc.Validator.Equals("Secondary"))
                    {
                        state.flags |= AccountFlags.Validator;
                    }

                    state.stakeTime = stakeTimestamp;

                    state.usedStorage = acc.Storage.Used;
                    state.availableStorage = acc.Storage.Available;
                    state.archives = acc.Storage.Archives;
                    state.avatarData = acc.Storage.Avatar;

                    ReportWalletBalance(PlatformKind.Phantasma, state, session);
                    SetBalanceError(PlatformKind.Phantasma, null, session);

                    if (missingTokens != null && missingTokens.Count > 0)
                    {
                        Log.WriteWarning($"RefreshBalances: detected unknown tokens ({string.Join(", ", missingTokens)}) - reloading token list.");
                        ScheduleBalanceRefreshAfterTokens();
                        TokensReinit();
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (PhantasmaRequestException ex)
                {
                    Log.WriteWarning($"RefreshBalances[PHA] {ex.ErrorType}: {ex.Message}");

                    RotateRpcOnWebError(ex);

                    SetBalanceError(PlatformKind.Phantasma, $"Phantasma request failed: {ex.Message}", session);
                    ReportWalletBalance(PlatformKind.Phantasma, null, session);
                }
                catch (FormatException ex)
                {
                    Log.WriteWarning($"RefreshBalances[PHA] parse error: {ex.Message}");
                    SetBalanceError(PlatformKind.Phantasma, $"Error while parsing balances: {ex.Message}", session);
                    ReportWalletBalance(PlatformKind.Phantasma, null, session);
                }
                catch (Exception ex)
                {
                    Log.WriteWarning($"RefreshBalances[PHA] unexpected error: {ex}");
                    SetBalanceError(PlatformKind.Phantasma, $"Error while fetching balances: {ex.Message}", session);
                    ReportWalletBalance(PlatformKind.Phantasma, null, session);
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

        public void BlankState()
        {
            var platforms = CurrentAccount.platforms.Split();

            _states.Clear();
            foreach (var platform in platforms)
            {
                _states[platform] = new AccountState()
                {
                    platform = platform,
                    address = GetAddress(CurrentIndex, platform),
                    balances = new Balance[0],
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

        public void RefreshNft(bool force, string symbol)
        {
            async Task ExecuteAsync()
            {
                var now = DateTime.UtcNow;
                var session = CaptureAccountSession();
                var normalizedSymbol = NormalizeNftSymbol(symbol);
                var cacheAddress = CurrentState?.address;
                if (string.IsNullOrEmpty(normalizedSymbol))
                {
                    return;
                }

                Log.Write($"[NFT] RefreshNft start force={force} symbol={symbol} currentPlatform={CurrentPlatform}"); //TODO Check if still needed once refactoring is over

                if (force)
                {
                    // On force refresh we clear NFT symbol's cache.
                    if (normalizedSymbol == "TTRS")
                        TtrsStore.Clear();
                    else if (normalizedSymbol == "GAME")
                        GameStore.Clear();
                    else
                        Cache.ClearDataNode("tokens-" + symbol.ToLower(), Cache.FileType.JSON, cacheAddress);

                    NftImages.Clear(symbol);
                }

                var platforms = CurrentAccount.platforms.Split();

                foreach (var platform in platforms)
                {
                    var nftSessionId = AdvanceNftSession(platform, normalizedSymbol);
                    if (!IsNftRequestCurrent(session, platform, normalizedSymbol, nftSessionId))
                    {
                        return;
                    }

                    var refreshStarted = false;
                    var currentState = CurrentState;
                    if (currentState == null)
                    {
                        if (IsNftRequestCurrent(session, platform, normalizedSymbol, nftSessionId))
                        {
                            ReportWalletNft(platform, symbol, session);
                        }
                        continue;
                    }

                    BeginNftRefresh(platform, normalizedSymbol);
                    refreshStarted = true;
                    NftsRefreshStarted?.Invoke(platform, symbol);

                    var nftKey = (platform, normalizedSymbol);
                    List<TokenDataResult> cachedNfts = null;
                    Dictionary<string, IRom> cachedRoms = null;
                    lock (_nftCacheLock)
                    {
                        _nfts.TryGetValue(nftKey, out cachedNfts);
                        _roms.TryGetValue(nftKey, out cachedRoms);
                    }

                    var workingNfts = cachedNfts != null
                        ? new List<TokenDataResult>(cachedNfts)
                        : new List<TokenDataResult>();

                    var workingRoms = cachedRoms != null
                        ? new Dictionary<string, IRom>(cachedRoms)
                        : new Dictionary<string, IRom>();

                    var hasBalanceData = currentState.balances != null;
                    var balanceEntries = hasBalanceData
                        ? currentState.balances.Where(x => string.Equals(x.Symbol, normalizedSymbol, StringComparison.OrdinalIgnoreCase)).ToList()
                        : new List<Balance>();
                    var targetIds = new HashSet<string>(balanceEntries.SelectMany(x => x.Ids ?? Array.Empty<string>()), StringComparer.OrdinalIgnoreCase);

                    // Progressive updates only when there is no cached list for this (platform, symbol).
                    // This avoids flicker when cached data exists, but keeps the UI responsive when the list would be empty for a long time.
                    var progressEligible = (cachedNfts == null || cachedNfts.Count == 0) && targetIds.Count > 0;
                    var progressBatchSize = 5;
                    var progressInterval = TimeSpan.FromMilliseconds(250);
                    var lastProgressAt = DateTime.UtcNow;
                    var pendingProgress = 0;

                    void PublishProgress(bool force = false)
                    {
                        if (!progressEligible)
                        {
                            return;
                        }

                        if (!IsNftRequestCurrent(session, platform, normalizedSymbol, nftSessionId))
                        {
                            return;
                        }

                        pendingProgress++;
                        var now = DateTime.UtcNow;
                        if (!force && pendingProgress < progressBatchSize && now - lastProgressAt < progressInterval)
                        {
                            return;
                        }

                        pendingProgress = 0;
                        lastProgressAt = now;

                        lock (_nftCacheLock)
                        {
                            _nfts[nftKey] = new List<TokenDataResult>(workingNfts);
                            _roms[nftKey] = new Dictionary<string, IRom>(workingRoms);
                        }

                        ReportWalletNft(platform, symbol, session, log: false);
                    }

                    void UpsertNft(TokenDataResult tokenData)
                    {
                        if (tokenData == null || string.IsNullOrEmpty(tokenData.Id))
                        {
                            return;
                        }

                        var idx = workingNfts.FindIndex(x => string.Equals(x.Id, tokenData.Id, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0)
                        {
                            workingNfts[idx] = tokenData;
                        }
                        else
                        {
                            workingNfts.Add(tokenData);
                        }

                        PublishProgress();
                    }

                    void UpsertRom(string tokenId, IRom rom)
                    {
                        if (string.IsNullOrEmpty(tokenId))
                        {
                            return;
                        }

                        workingRoms[tokenId] = rom;
                    }

                    try
                    {
                        if (Tokens.GetToken(symbol, platform, out var tokenInfo))
                        {
                            switch (platform)
                            {
                                case PlatformKind.Phantasma:
                                    {
                                        if (tokenInfo.IsFungible())
                                        {
                                            break;
                                        }

                                        var cache = Cache.GetTokenCache("tokens-" + symbol.ToLower(), Cache.FileType.JSON, 0, currentState.address);
                                        if (cache == null)
                                        {
                                            cache = Array.Empty<TokenDataResult>();
                                        }

                                        Log.Write("Getting NFTs...");
                                        foreach (var balanceEntry in balanceEntries)
                                        {
                                            SetNftDescriptionsLoaded(platform, normalizedSymbol, false);
                                            var loadedTokenCounter = 0;
                                            var ids = balanceEntry.Ids ?? Array.Empty<string>();

                                            foreach (var id in ids)
                                            {
                                                var tokenData = Cache.FindTokenData(cache, id);

                                                if (tokenData != null)
                                                {
                                                    loadedTokenCounter++;

                                                    var rom = tokenData.ParseRom(symbol);
                                                    UpsertRom(tokenData.Id, rom);
                                                    var (hasError, error) = rom.HasParsingError();
                                                    if (rom.IsEmpty())
                                                    {
                                                        Log.Write($"ROM is null or empty");
                                                    }
                                                    else if (hasError)
                                                    {
                                                        Log.Write(error);
                                                    }

                                                    UpsertNft(tokenData);

                                                    NftImages.DownloadImageAsync(symbol, tokenData.GetPropertyValue("ImageURL"), id, session.Token).Forget(LogTaskException);
                                                }
                                                else if (normalizedSymbol == "TTRS")
                                                {
                                                    loadedTokenCounter++;

                                                    var tokenData2 = workingNfts.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) ?? new TokenDataResult { Id = id };
                                                    UpsertNft(tokenData2);
                                                }
                                                else
                                                {
                                                    try
                                                    {
                                                        if (progressEligible)
                                                        {
                                                            // Insert a placeholder immediately so the list can start showing progress.
                                                            UpsertNft(new TokenDataResult { Id = id });
                                                        }

                                                        var tokenData2 = await AsyncPhantasma.FromApi<TokenDataResult>(
                                                            (onSuccess, onError) => phantasmaApi.GetNFT(
                                                                symbol,
                                                                id,
                                                                true,
                                                                onSuccess,
                                                                onError,
                                                                timeout: WebClient.DefaultTimeout,
                                                                retries: NetworkRetryPolicy.Retries),
                                                            session.Token);
                                                        if (!IsNftRequestCurrent(session, platform, normalizedSymbol, nftSessionId))
                                                        {
                                                            return;
                                                        }
                                                        var rom = tokenData2.ParseRom(symbol);
                                                        UpsertRom(id, rom);
                                                        var (hasError, error) = rom.HasParsingError();
                                                        if (rom.IsEmpty())
                                                        {
                                                            Log.Write($"ROM is null or empty");
                                                        }
                                                        else if (hasError)
                                                        {
                                                            Log.Write(error);
                                                        }

                                                        NftImages.DownloadImageAsync(symbol, tokenData2.GetPropertyValue("ImageURL"), id, session.Token).Forget(LogTaskException);

                                                        loadedTokenCounter++;

                                                        UpsertNft(tokenData2);
                                                        cache = cache.Where(x => !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)).Append(tokenData2).ToArray();
                                                    }
                                                    catch (PhantasmaRequestException ex)
                                                    {
                                                        loadedTokenCounter++;
                                                        Log.Write($"NFT loading error for {symbol}/{id}: {ex.Message}");
                                                    }
                                                }
                                            }

                                            PublishProgress(force: true);

                                            if (ids.Length > 0 && loadedTokenCounter == ids.Length)
                                            {
                                                Cache.SaveTokenDatas("tokens-" + symbol.ToLower(), Cache.FileType.JSON, cache, currentState.address);

                                                if (symbol != "TTRS")
                                                {
                                                    SetNftDescriptionsLoaded(platform, normalizedSymbol, true);
                                                }
                                            }

                                            if (ids.Length > 0)
                                            {
                                                if (normalizedSymbol == "TTRS")
                                                {
                                                    await TtrsStore.LoadStoreNftAsync(ids, (item) =>
                                                        {
                                                            NftImages.DownloadImageAsync(symbol, item.item_info.image_url, item.id, session.Token).Forget(LogTaskException);
                                                        }, session.Token);
                                                    if (!IsNftRequestCurrent(session, platform, normalizedSymbol, nftSessionId))
                                                    {
                                                        return;
                                                    }

                                                    SetNftDescriptionsLoaded(platform, normalizedSymbol, true);
                                                    PublishProgress(force: true);
                                                }
                                                else if (normalizedSymbol == "GAME")
                                                {
                                                    await GameStore.LoadStoreNftAsync(ids, (item) =>
                                                        {
                                                            NftImages.DownloadImageAsync(symbol, item.parsed_rom.img_url, item.ID, session.Token).Forget(LogTaskException);
                                                        }, session.Token);
                                                    if (!IsNftRequestCurrent(session, platform, normalizedSymbol, nftSessionId))
                                                    {
                                                        return;
                                                    }

                                                    SetNftDescriptionsLoaded(platform, normalizedSymbol, true);
                                                    PublishProgress(force: true);
                                                }
                                            }
                                        }
                                    }
                                    break;

                                default:
                                    break;
                            }
                        }
                    }
                    finally
                    {
                        if (refreshStarted)
                        {
                            EndNftRefresh(platform, normalizedSymbol);
                        }

                        if (IsNftRequestCurrent(session, platform, normalizedSymbol, nftSessionId))
                        {
                            if (hasBalanceData)
                            {
                                workingNfts.RemoveAll(x => x == null || string.IsNullOrEmpty(x.Id) || !targetIds.Contains(x.Id));
                                var staleRomIds = workingRoms.Keys.Where(id => !targetIds.Contains(id)).ToList();
                                foreach (var stale in staleRomIds)
                                {
                                    workingRoms.Remove(stale);
                                }
                            }

                            lock (_nftCacheLock)
                            {
                                _nfts[nftKey] = workingNfts;
                                _roms[nftKey] = workingRoms;
                            }
                            ReportWalletNft(platform, symbol, session);
                        }
                    }
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

        public void RefreshHistory(bool force, PlatformKind platforms = PlatformKind.None)
        {
            async Task ExecuteAsync()
            {
                if (!HasSelection)
                {
                    Log.WriteWarning("RefreshHistory: skipped because no account is selected.");
                    return;
                }

                var currentAccount = CurrentAccount;
                if (currentAccount.passwordProtected && string.IsNullOrEmpty(CurrentPasswordHash))
                {
                    Log.WriteWarning("RefreshHistory: skipped because current account is locked.");
                    return;
                }

                var session = CaptureAccountSession();
                var accountName = string.IsNullOrEmpty(currentAccount.name) ? "(unknown)" : currentAccount.name;
                Log.Write($"[History] RefreshHistory start force={force} currentAccount={accountName} currentPlatform={CurrentPlatform} platformsArg={platforms}");
                List<PlatformKind> platformsList;
                if (platforms == PlatformKind.None)
                    platformsList = CurrentAccount.platforms.Split();
                else
                    platformsList = platforms.Split();

                lock (_refreshStatus)
                {
                    RefreshStatus refreshStatus;
                    var now = DateTime.UtcNow;
                    if (_refreshStatus.ContainsKey(PlatformKind.Phantasma))
                    {
                        refreshStatus = _refreshStatus[PlatformKind.Phantasma];

                        refreshStatus.HistoryRefreshing = true;
                        refreshStatus.LastHistoryRefresh = now;

                        _refreshStatus[PlatformKind.Phantasma] = refreshStatus;
                    }
                    else
                    {
                        _refreshStatus.Add(PlatformKind.Phantasma,
                            new RefreshStatus
                            {
                                BalanceRefreshing = false,
                                LastBalanceRefresh = DateTime.MinValue,
                                BalanceRefreshCallback = null,
                                HistoryRefreshing = true,
                                LastHistoryRefresh = now
                            });
                    }
                }

                foreach (var platform in platformsList)
                {
                    HistoryRefreshStarted?.Invoke(platform);
                }

                var wif = this.CurrentWif;
                accountHistoryNotLoaded = false; // First refresh attempt for this wallet has been triggered.

                var keys = PhantasmaKeys.FromWIF(wif);
                try
                {
                    var result = await AsyncPhantasma.FromApi<AccountTransactionsResult, uint, uint>(
                        (onSuccess, onError) => phantasmaApi.GetAddressTransactions(
                            keys.Address.Text,
                            1,
                            20,
                            onSuccess,
                            onError,
                            timeout: WebClient.DefaultTimeout,
                            retries: NetworkRetryPolicy.Retries),
                        session.Token);
                    var (transactions, _, _) = result;

                    if (!IsSessionCurrent(session))
                    {
                        return;
                    }

                    var history = new List<HistoryEntry>();

                    foreach (var tx in transactions.Txs)
                    {
                        history.Add(new HistoryEntry()
                        {
                            hash = tx.Hash,
                            date = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(tx.Timestamp).ToLocalTime(),
                            url = GetPhantasmaTransactionURL(tx.Hash)
                        });
                    }

                    var platformsLabel = string.Join(",", platformsList);
                    Log.Write($"[History] RefreshHistory success txCount={history.Count} platforms={platformsLabel}");
                    ReportWalletHistory(PlatformKind.Phantasma, history, session);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (PhantasmaRequestException ex)
                {
                    RotateRpcOnWebError(ex);
                    Log.WriteWarning($"[History] RefreshHistory failed (SDK) {ex.ErrorType}: {ex.Message}");
                    ReportWalletHistory(PlatformKind.Phantasma, null, session);
                }
                catch (Exception ex)
                {
                    // Ensure UI does not stay stuck in a perpetual refresh state on unexpected failures.
                    Log.WriteWarning($"RefreshHistory[PHA] unexpected error: {ex}");
                    ReportWalletHistory(PlatformKind.Phantasma, null, session);
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

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

        public int AddWallet(string name, string wif, string password, bool legacySeed)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 3)
            {
                throw new Exception("Name is too short.");
            }

            if (name.Length > 16)
            {
                throw new Exception("Name is too long.");
            }

            for (int i = 0; i < Accounts.Count(); i++)
            {
                if (Accounts[i].name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception("An account with this name already exists.");
                }
            }

            var account = new Account() { name = name, platforms = AccountManager.MergeAvailablePlatforms(), misc = "" };

            // Initializing public addresses.
            var phaKeys = PhantasmaKeys.FromWIF(wif);
            account.phaAddress = phaKeys.Address.ToString();

            var neoKeys = PhantasmaPhoenix.InteropChains.Legacy.Neo2.NeoKeys.FromWIF(wif);
            account.neoAddress = neoKeys.Address.ToString();
            account.neoAddress = neoKeys.AddressN3.ToString();

            var ethereumAddressUtil = new PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Util.AddressUtil();
            account.ethAddress = ethereumAddressUtil.ConvertToChecksumAddress(PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.FromWIF(wif).Address);

            if (!String.IsNullOrEmpty(password))
            {
                account.passwordProtected = true;
                account.passwordIterations = PasswordIterations;

                // Encrypting WIF.
                GetPasswordHash(password, account.passwordIterations, out string salt, out string passwordHash);
                account.password = "";
                account.salt = salt;

                account.WIF = EncryptString(wif, passwordHash, out string iv);
                account.iv = iv;

                // Decrypting to ensure there are no exceptions.
                DecryptString(account.WIF, passwordHash, account.iv);
            }
            else
            {
                account.passwordProtected = false;
                account.WIF = wif;
            }

            account.misc = legacySeed ? "legacy-seed" : "";

            Accounts.Add(account);

            return Accounts.Count() - 1;
        }

        internal void DeleteAccount(int currentIndex)
        {
            if (currentIndex < 0 || currentIndex >= Accounts.Count())
            {
                return;
            }

            Accounts.RemoveAt(currentIndex);
            SaveAccounts();
        }

        internal void ReplaceAccountWIF(int currentIndex, string wif, string passwordHash, out string deletedDuplicateWallet)
        {
            deletedDuplicateWallet = null;

            if (currentIndex < 0 || currentIndex >= Accounts.Count())
            {
                return;
            }

            var account = Accounts[currentIndex];
            if (string.IsNullOrEmpty(passwordHash))
            {
                account.WIF = wif;
            }
            else
            {
                account.WIF = EncryptString(wif, passwordHash, out string iv);
                account.iv = iv;
            }
            account.misc = ""; // Migration does not guarantee that new account have current seed, but that's all that we can do with it.

            // Initializing new public addresses.
            wif = account.GetWif(passwordHash); // Recreating to be sure all is good.
            var phaKeys = PhantasmaKeys.FromWIF(wif);
            account.phaAddress = phaKeys.Address.ToString();

            var neoKeys = PhantasmaPhoenix.InteropChains.Legacy.Neo2.NeoKeys.FromWIF(wif);
            account.neoAddress = neoKeys.Address.ToString();

            var ethereumAddressUtil = new PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Util.AddressUtil();
            account.ethAddress = ethereumAddressUtil.ConvertToChecksumAddress(PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.FromWIF(wif).Address);

            Accounts[currentIndex] = account;

            for (var i = 0; i < Accounts.Count; i++)
            {
                if (i != currentIndex && Accounts[i].phaAddress == account.phaAddress)
                {
                    deletedDuplicateWallet = Accounts[i].name;
                    Accounts.RemoveAt(i);
                    break;
                }
            }

            SaveAccounts();
        }

        public bool RenameAccount(string newName)
        {
            foreach (var account in Accounts)
            {
                if (account.name.Equals(newName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var account2 = Accounts[CurrentIndex];
            account2.name = newName;
            Accounts[CurrentIndex] = account2;
            SaveAccounts();
            return true;
        }

        private void LogTaskException(Exception ex)
        {
            if (ex == null)
            {
                return;
            }

            Log.WriteWarning(ex.ToString());
        }

        internal void ValidateAccountName(string name, Action<string> callback)
        {
            async Task ExecuteAsync()
            {
                try
                {
                    var address = await AsyncPhantasma.FromApi<string>(
                        (onSuccess, onError) => phantasmaApi.LookUpName(
                            name,
                            onSuccess,
                            onError,
                            timeout: WebClient.DefaultTimeout,
                            retries: NetworkRetryPolicy.Retries),
                        CancellationToken.None);
                    callback(address);
                }
                catch (PhantasmaRequestException ex)
                {
                    RotateRpcOnWebError(ex);
                    callback(null);
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

        public string GetAddress(int index, PlatformKind platform)
        {
            if (index < 0 || index >= Accounts.Count())
            {
                return null;
            }

            if (index == _selectedAccountIndex)
            {
                if (_states.ContainsKey(platform))
                {
                    return _states[platform].address;
                }
            }

            switch (platform)
            {
                case PlatformKind.Phantasma:
                    return Accounts[index].phaAddress;

                case PlatformKind.Neo:
                    return Accounts[index].neoAddress;

                case PlatformKind.Ethereum:
                    return Accounts[index].ethAddress;

                case PlatformKind.BSC:
                    return Accounts[index].ethAddress;
            }

            return null;
        }

        public void ResetNftsSorting()
        {
            currentTtrsNftsSortMode = TtrsNftSortMode.None;
            currentNftsSortMode = NftSortMode.None;
            currentNftsSortDirection = SortDirection.None;
        }

        public void SortTtrsNfts(string symbol)
        {
            var normalizedSymbol = NormalizeNftSymbol(symbol);
            if (string.IsNullOrEmpty(normalizedSymbol))
            {
                return;
            }

            var key = (CurrentPlatform, normalizedSymbol);
            List<TokenDataResult> nfts;
            lock (_nftCacheLock)
            {
                if (!_nfts.TryGetValue(key, out var cachedNfts) || cachedNfts == null)
                {
                    return;
                }
                nfts = new List<TokenDataResult>(cachedNfts);
            }

            if (!AreNftDescriptionsLoaded(CurrentPlatform, normalizedSymbol)) // We should not sort NFTs if there are no attributes available.
            {
                return;
            }

            if (normalizedSymbol == "TTRS")
            {
                if (currentTtrsNftsSortMode == (TtrsNftSortMode)Settings.ttrsNftSortMode && (int)currentNftsSortDirection == Settings.nftSortDirection)
                    return; // Nothing changed, no need to sort again.

                switch ((TtrsNftSortMode)Settings.ttrsNftSortMode)
                {
                    case TtrsNftSortMode.Number_Date:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => TtrsStore.GetNft(x.Id).mint).ThenBy(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => TtrsStore.GetNft(x.Id).mint).ThenByDescending(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        break;
                    case TtrsNftSortMode.Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => TtrsStore.GetNft(x.Id).timestamp).ThenBy(x => TtrsStore.GetNft(x.Id).mint).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => TtrsStore.GetNft(x.Id).timestamp).ThenByDescending(x => TtrsStore.GetNft(x.Id).mint).ToList();
                        break;
                    case TtrsNftSortMode.Type_Number_Date:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderByDescending(x => TtrsStore.GetNft(x.Id).item_info.type).ThenBy(x => TtrsStore.GetNft(x.Id).mint).ThenBy(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        else
                            nfts = nfts.OrderBy(x => TtrsStore.GetNft(x.Id).item_info.type).ThenByDescending(x => TtrsStore.GetNft(x.Id).mint).ThenByDescending(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        break;
                    case TtrsNftSortMode.Type_Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderByDescending(x => TtrsStore.GetNft(x.Id).item_info.type).ThenBy(x => TtrsStore.GetNft(x.Id).timestamp).ThenBy(x => TtrsStore.GetNft(x.Id).mint).ToList();
                        else
                            nfts = nfts.OrderBy(x => TtrsStore.GetNft(x.Id).item_info.type).ThenByDescending(x => TtrsStore.GetNft(x.Id).timestamp).ThenByDescending(x => TtrsStore.GetNft(x.Id).mint).ToList();
                        break;
                    case TtrsNftSortMode.Type_Rarity: // And also Number and Date as last sorting parameters.
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderByDescending(x => TtrsStore.GetNft(x.Id).item_info.type).ThenByDescending(x => TtrsStore.GetNft(x.Id).item_info.rarity).ThenBy(x => TtrsStore.GetNft(x.Id).mint).ThenBy(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        else
                            nfts = nfts.OrderBy(x => TtrsStore.GetNft(x.Id).item_info.type).ThenBy(x => TtrsStore.GetNft(x.Id).item_info.rarity).ThenByDescending(x => TtrsStore.GetNft(x.Id).mint).ThenByDescending(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        break;
                }

                lock (_nftCacheLock)
                {
                    _nfts[key] = nfts;
                }
                currentTtrsNftsSortMode = (TtrsNftSortMode)Settings.ttrsNftSortMode;
            }
            else if (normalizedSymbol == "GAME")
            {
                if (currentNftsSortMode == (NftSortMode)Settings.nftSortMode && (int)currentNftsSortDirection == Settings.nftSortDirection)
                    return; // Nothing changed, no need to sort again.

                switch ((NftSortMode)Settings.nftSortMode)
                {
                    case NftSortMode.Name:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => GameStore.GetNft(x.Id).meta?.name_english).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => GameStore.GetNft(x.Id).meta?.name_english).ToList();
                        break;
                    case NftSortMode.Number_Date:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => GameStore.GetNft(x.Id).mint).ThenBy(x => GameStore.GetNft(x.Id).parsed_rom.timestampDT()).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => GameStore.GetNft(x.Id).mint).ThenByDescending(x => GameStore.GetNft(x.Id).parsed_rom.timestampDT()).ToList();
                        break;
                    case NftSortMode.Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => GameStore.GetNft(x.Id).parsed_rom.timestampDT()).ThenBy(x => GameStore.GetNft(x.Id).mint).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => GameStore.GetNft(x.Id).parsed_rom.timestampDT()).ThenByDescending(x => GameStore.GetNft(x.Id).mint).ToList();
                        break;
                }

                lock (_nftCacheLock)
                {
                    _nfts[key] = nfts;
                }
                currentNftsSortMode = (NftSortMode)Settings.nftSortMode;
            }
            else
            {
                if (currentNftsSortMode == (NftSortMode)Settings.nftSortMode && (int)currentNftsSortDirection == Settings.nftSortDirection)
                    return; // Nothing changed, no need to sort again.

                // Some NFT collections can return missing ROM/metadata (or placeholders during progressive loading).
                // Sorting must stay stable and never throw; use safe fallback keys so items remain visible.
                string GetMintSortKey(string id)
                {
                    var token = GetNft(normalizedSymbol, id);
                    return string.IsNullOrWhiteSpace(token?.Mint) ? "0" : token.Mint;
                }

                string GetNameSortKey(string id)
                {
                    var rom = GetNftRom(normalizedSymbol, id);
                    return rom == null || rom.IsEmpty() ? string.Empty : (rom.GetName() ?? string.Empty);
                }

                DateTime GetDateSortKey(string id)
                {
                    var rom = GetNftRom(normalizedSymbol, id);
                    return rom == null || rom.IsEmpty() ? DateTime.MinValue : rom.GetDate();
                }

                switch ((NftSortMode)Settings.nftSortMode)
                {
                    case NftSortMode.Name:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => GetNameSortKey(x.Id)).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => GetNameSortKey(x.Id)).ToList();
                        break;
                    case NftSortMode.Number_Date:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => GetMintSortKey(x.Id)).ThenBy(x => GetDateSortKey(x.Id)).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => GetMintSortKey(x.Id)).ThenByDescending(x => GetDateSortKey(x.Id)).ToList();
                        break;
                    case NftSortMode.Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => GetDateSortKey(x.Id)).ThenBy(x => GetMintSortKey(x.Id)).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => GetDateSortKey(x.Id)).ThenByDescending(x => GetMintSortKey(x.Id)).ToList();
                        break;
                }

                lock (_nftCacheLock)
                {
                    _nfts[key] = nfts;
                }
                currentNftsSortMode = (NftSortMode)Settings.nftSortMode;
            }

            currentNftsSortDirection = (SortDirection)Settings.nftSortDirection;
        }

        public TokenDataResult GetNft(string id)
        {
            var nfts = CurrentNfts;
            return nfts?.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public List<TokenDataResult> GetNfts(string symbol)
        {
            return GetNfts(CurrentPlatform, symbol);
        }

        public List<TokenDataResult> GetNfts(PlatformKind platform, string symbol)
        {
            var normalizedSymbol = NormalizeNftSymbol(symbol);
            if (string.IsNullOrEmpty(normalizedSymbol))
            {
                return null;
            }

            var key = (platform, normalizedSymbol);
            lock (_nftCacheLock)
            {
                return _nfts.TryGetValue(key, out var nfts) ? nfts : null;
            }
        }

        public TokenDataResult GetNft(string symbol, string id)
        {
            var nfts = GetNfts(symbol);
            return nfts?.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public IRom GetNftRom(string symbol, string id)
        {
            var normalizedSymbol = NormalizeNftSymbol(symbol);
            if (string.IsNullOrEmpty(normalizedSymbol) || string.IsNullOrEmpty(id))
            {
                return null;
            }

            var key = (CurrentPlatform, normalizedSymbol);
            lock (_nftCacheLock)
            {
                if (_roms.TryGetValue(key, out var roms) && roms != null && roms.TryGetValue(id, out var rom))
                {
                    return rom;
                }
            }

            return null;
        }

        public IRom GetNftRom(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            Dictionary<string, IRom> current = null;
            lock (_nftCacheLock)
            {
                foreach (var entry in _roms)
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

            if (current != null && current.TryGetValue(id, out var rom))
            {
                return rom;
            }

            return null;
        }

        public async Task<ValidationResult<TokenDataResult>> LoadDebugNftAsync(string symbol, string tokenId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(tokenId))
            {
                return ValidationResult<TokenDataResult>.Fail("NFT symbol and token id are required.");
            }

            if (phantasmaApi == null)
            {
                return ValidationResult<TokenDataResult>.Fail("RPC client is not ready.");
            }

            tokenId = tokenId.Trim();

            var normalizedSymbol = NormalizeNftSymbol(symbol);
            if (string.IsNullOrEmpty(normalizedSymbol))
            {
                return ValidationResult<TokenDataResult>.Fail("NFT symbol is invalid.");
            }

            TokenDataResult tokenData;
            try
            {
                tokenData = await AsyncPhantasma.FromApi<TokenDataResult>(
                    (onSuccess, onError) => phantasmaApi.GetNFT(
                        normalizedSymbol,
                        tokenId,
                        true,
                        onSuccess,
                        onError,
                        timeout: WebClient.DefaultTimeout,
                        retries: NetworkRetryPolicy.Retries),
                    cancellationToken);
            }
            catch (PhantasmaRequestException ex)
            {
                Log.WriteWarning($"[NFT][Debug] GetNFT failed for {normalizedSymbol}:{tokenId}: {ex.Message}");
                return ValidationResult<TokenDataResult>.Fail($"Failed to load NFT: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"[NFT][Debug] GetNFT failed for {normalizedSymbol}:{tokenId}: {ex}");
                return ValidationResult<TokenDataResult>.Fail("Failed to load NFT.");
            }

            if (tokenData == null || string.IsNullOrWhiteSpace(tokenData.Id))
            {
                return ValidationResult<TokenDataResult>.Fail("NFT not found.");
            }

            IRom rom = null;
            try
            {
                rom = tokenData.ParseRom(normalizedSymbol);
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"[NFT][Debug] Failed to parse ROM for {normalizedSymbol}:{tokenId}: {ex.Message}");
            }

            var key = (CurrentPlatform, normalizedSymbol);
            lock (_nftCacheLock)
            {
                var workingNfts = _nfts.TryGetValue(key, out var cachedNfts) && cachedNfts != null
                    ? new List<TokenDataResult>(cachedNfts)
                    : new List<TokenDataResult>();

                var idx = workingNfts.FindIndex(x => string.Equals(x.Id, tokenData.Id, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    workingNfts[idx] = tokenData;
                }
                else
                {
                    workingNfts.Add(tokenData);
                }

                _nfts[key] = workingNfts;

                if (rom != null && !rom.IsEmpty())
                {
                    var workingRoms = _roms.TryGetValue(key, out var cachedRoms) && cachedRoms != null
                        ? new Dictionary<string, IRom>(cachedRoms)
                        : new Dictionary<string, IRom>();
                    workingRoms[tokenData.Id] = rom;
                    _roms[key] = workingRoms;
                }
            }

            NftsUpdated?.Invoke(CurrentPlatform, normalizedSymbol);
            return ValidationResult<TokenDataResult>.Ok(tokenData);
        }

        public void GetPhantasmaAddressInfo(string addressString, Account? account, Action<string, string> callback)
        {
            byte[] scriptUnclaimed;
            byte[] scriptStake;
            byte[] scriptStorageStake;
            byte[] scriptVotingPower;
            byte[] scriptStakeTimestamp;
            byte[] scriptTimeBeforeUnstake;
            byte[] scriptMasterDate;
            byte[] scriptIsMaster;
            try
            {
                var address = Address.Parse(addressString);

                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetUnclaimed", address);
                    scriptUnclaimed = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetStake", address);
                    scriptStake = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetStorageStake", address);
                    scriptStorageStake = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetAddressVotingPower", address);
                    scriptVotingPower = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetStakeTimestamp", address);
                    scriptStakeTimestamp = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetTimeBeforeUnstake", address);
                    scriptTimeBeforeUnstake = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetMasterDate", address);
                    scriptMasterDate = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "IsMaster", address);
                    scriptIsMaster = sb.EndScript();
                }
            }
            catch (Exception e)
            {
                callback(null, e.ToString());
                return;
            }

            InvokeScriptPhantasma("main", scriptUnclaimed, (unclaimedResult, unclaimedInvokeError) =>
            {
                if (!string.IsNullOrEmpty(unclaimedInvokeError))
                {
                    callback(null, "Script invocation error!\n\n" + unclaimedInvokeError);
                    return;
                }
                else
                {
                    InvokeScriptPhantasma("main", scriptStake, (stakeResult, stakeInvokeError) =>
                    {
                        if (!string.IsNullOrEmpty(stakeInvokeError))
                        {
                            callback(null, "Script invocation error!\n\n" + stakeInvokeError);
                            return;
                        }
                        else
                        {
                            InvokeScriptPhantasma("main", scriptStorageStake, (storageStakeResult, storageStakeInvokeError) =>
                            {
                                if (!string.IsNullOrEmpty(storageStakeInvokeError))
                                {
                                    callback(null, "Script invocation error!\n\n" + storageStakeInvokeError);
                                    return;
                                }
                                else
                                {
                                    InvokeScriptPhantasma("main", scriptVotingPower, (votingPowerResult, votingPowerInvokeError) =>
                                    {
                                        if (!string.IsNullOrEmpty(votingPowerInvokeError))
                                        {
                                            callback(null, "Script invocation error!\n\n" + votingPowerInvokeError);
                                            return;
                                        }
                                        else
                                        {
                                            InvokeScriptPhantasma("main", scriptStakeTimestamp, (stakeTimestampResult, stakeTimestampInvokeError) =>
                                            {
                                                if (!string.IsNullOrEmpty(stakeTimestampInvokeError))
                                                {
                                                    callback(null, "Script invocation error!\n\n" + stakeTimestampInvokeError);
                                                    return;
                                                }
                                                else
                                                {
                                                    InvokeScriptPhantasma("main", scriptTimeBeforeUnstake, (timeBeforeUnstakeResult, timeBeforeUnstakeInvokeError) =>
                                                    {
                                                        if (!string.IsNullOrEmpty(timeBeforeUnstakeInvokeError))
                                                        {
                                                            callback(null, "Script invocation error!\n\n" + timeBeforeUnstakeInvokeError);
                                                            return;
                                                        }
                                                        else
                                                        {
                                                            InvokeScriptPhantasma("main", scriptMasterDate, (masterDateResult, masterDateInvokeError) =>
                                                            {
                                                                if (!string.IsNullOrEmpty(masterDateInvokeError))
                                                                {
                                                                    callback(null, "Script invocation error!\n\n" + masterDateInvokeError);
                                                                    return;
                                                                }
                                                                else
                                                                {
                                                                    InvokeScriptPhantasma("main", scriptIsMaster, (isMasterResult, isMasterInvokeError) =>
                                                                    {
                                                                        if (!string.IsNullOrEmpty(isMasterInvokeError))
                                                                        {
                                                                            callback(null, "Script invocation error!\n\n" + isMasterInvokeError);
                                                                            return;
                                                                        }
                                                                        else
                                                                        {
                                                                            var unclaimedRaw = unclaimedResult != null ? VMObject.FromBytes(unclaimedResult).AsNumber() : -1;
                                                                            var stakeRaw = stakeResult != null ? VMObject.FromBytes(stakeResult).AsNumber() : -1;
                                                                            var storageStakeRaw = storageStakeResult != null ? VMObject.FromBytes(storageStakeResult).AsNumber() : -1;
                                                                            var unclaimed = WalletAmountFormatter.Format(unclaimedRaw, 10);
                                                                            var stake = WalletAmountFormatter.Format(stakeRaw, 8);
                                                                            var storageStake = WalletAmountFormatter.Format(storageStakeRaw, 8);
                                                                            var votingPower = votingPowerResult != null ? VMObject.FromBytes(votingPowerResult).AsNumber() : -1;
                                                                            var stakeTimestamp = stakeTimestampResult != null ? VMObject.FromBytes(stakeTimestampResult).AsTimestamp() : 0;
                                                                            var stakeTimestampLocal = stakeTimestamp != null ? ((DateTime)stakeTimestamp).ToLocalTime() : DateTime.MinValue;
                                                                            var timeBeforeUnstake = timeBeforeUnstakeResult != null ? VMObject.FromBytes(timeBeforeUnstakeResult).AsNumber() : -1;
                                                                            var masterDate = masterDateResult != null ? VMObject.FromBytes(masterDateResult).AsTimestamp() : 0;
                                                                            var isMaster = isMasterResult != null ? VMObject.FromBytes(isMasterResult).AsBool() : false;

                                                                            callback($"{addressString} account information:\n\n" +
                                                                                $"Unclaimed: {unclaimed} KCAL\n" +
                                                                                $"Stake: {stake} SOUL\n" +
                                                                                $"Is SM: {isMaster}\n" +
                                                                                $"SM since: {masterDate}\n" +
                                                                                $"Stake timestamp: {stakeTimestampLocal} ({stakeTimestamp} UTC)\n" +
                                                                                $"Next staking period starts in: {TimeSpan.FromSeconds((double)timeBeforeUnstake):hh\\:mm\\:ss}\n" +
                                                                                $"Storage stake: {storageStake} SOUL\n" +
                                                                                $"Voting power: {votingPower}" +
                                                                                (account != null ? $"\n\nNeo legacy address: {((Account)account).neoAddress}\nN3 address: {((Account)account).neoAddressN3}\nEth/BSC address: {((Account)account).ethAddress}" : ""), null);
                                                                        }
                                                                    });
                                                                }
                                                            });
                                                        }
                                                    });
                                                }
                                            });
                                        }
                                    });
                                }
                            });
                        }
                    });
                }

            });
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
