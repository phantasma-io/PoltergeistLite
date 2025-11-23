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
using Poltergeist.Wallet;
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

        public Settings Settings { get; private set; }

        public List<Account> Accounts { get; private set; }
        public bool AccountsAreReadyToBeUsed = false;

        private Dictionary<string, decimal> _tokenPrices = new Dictionary<string, decimal>();
        public string CurrentTokenCurrency { get; private set; }

        private int _selectedAccountIndex;
        public int CurrentIndex => _selectedAccountIndex;
        public Account CurrentAccount => HasSelection ? Accounts[_selectedAccountIndex] : new Account() { };
        public string CurrentPasswordHash;
        public string CurrentWif => Accounts[_selectedAccountIndex].GetWif(CurrentPasswordHash);

        public bool HasSelection => _selectedAccountIndex >= 0 && _selectedAccountIndex < Accounts.Count();

        private Dictionary<PlatformKind, AccountState> _states = new Dictionary<PlatformKind, AccountState>();
        private Dictionary<PlatformKind, List<TokenDataResult>> _nfts = new Dictionary<PlatformKind, List<TokenDataResult>>();
        private Dictionary<PlatformKind, Dictionary<string, IRom>> _roms = new();
        private Dictionary<PlatformKind, HistoryEntry[]> _history = new Dictionary<PlatformKind, HistoryEntry[]>();
        public Dictionary<PlatformKind, RefreshStatus> _refreshStatus = new Dictionary<PlatformKind, RefreshStatus>();

        public event Action<PlatformKind> BalancesRefreshStarted;
        public event Action<PlatformKind> BalancesUpdated;
        public event Action<PlatformKind, string> NftsUpdated;
        public event Action<PlatformKind, string> NftsRefreshStarted;
        public event Action<PlatformKind> HistoryUpdated;
        public event Action<PlatformKind> HistoryRefreshStarted;

        public PlatformKind CurrentPlatform { get; set; }
        public AccountState CurrentState => _states.ContainsKey(CurrentPlatform) ? _states[CurrentPlatform] : null;
        public List<TokenDataResult> CurrentNfts => _nfts.ContainsKey(CurrentPlatform) ? _nfts[CurrentPlatform] : null;
        public HistoryEntry[] CurrentHistory => _history.ContainsKey(CurrentPlatform) ? _history[CurrentPlatform] : null;

        public AccountState MainState => _states.ContainsKey(PlatformKind.Phantasma) ? _states[PlatformKind.Phantasma] : null;

        private bool nftDescriptionsAreFullyLoaded;
        private TtrsNftSortMode currentTtrsNftsSortMode = TtrsNftSortMode.None;
        private NftSortMode currentNftsSortMode = NftSortMode.None;
        private SortDirection currentNftsSortDirection = SortDirection.None;

        public static AccountManager Instance { get; private set; }

        public string Status { get; private set; }
        public bool Ready => Status == "ok";
        public bool BalanceRefreshing => _refreshStatus.ContainsKey(CurrentPlatform) ? _refreshStatus[CurrentPlatform].BalanceRefreshing : false;
        public bool NftsRefreshing => _refreshStatus.ContainsKey(CurrentPlatform) ? _refreshStatus[CurrentPlatform].NftsRefreshing : false;
        public bool HistoryRefreshing => _refreshStatus.ContainsKey(CurrentPlatform) ? _refreshStatus[CurrentPlatform].HistoryRefreshing : false;
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

        private void Awake()
        {
            Instance = this;
            Settings = WalletRuntime.GetSettings();

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
            if (!WalletAmountFormatter.TryToDecimal(amount, decimals, out var decimalAmount))
            {
                return null;
            }

            bool hasLocalCurrency = !string.IsNullOrEmpty(CurrentTokenCurrency) && _currencyMap.ContainsKey(CurrentTokenCurrency);
            if (_tokenPrices.ContainsKey(symbol) && hasLocalCurrency)
            {
                var price = _tokenPrices[symbol] * decimalAmount;
                var ch = _currencyMap[CurrentTokenCurrency];
                return $"{WalletAmountFormatter.Format(price, MoneyFormatType.Short)} {ch}";
            }
            else
            {
                return null;
            }
        }

        private async Task FetchTokenPricesAsync(IEnumerable<TokenResult> tokens, string currency, CancellationToken cancellationToken)
        {
            var separator = "%2C";
            var url = "https://api.coingecko.com/api/v3/simple/price?ids=" + string.Join(separator, tokens.Where(x => Tokens.HasCGSymbol(x)).Select(x => Tokens.GetCGSymbol(x)).Distinct().ToList()) + "&vs_currencies=" + currency;
            try
            {
                var response = await WebClientAsync.GetAsync<Dictionary<string, Dictionary<string, decimal>>>(url, WebClient.DefaultTimeout, cancellationToken);
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
                    var response = await WebClientAsync.GetAsync<JToken>(url, WebClient.DefaultTimeout, CancellationToken.None);
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

                ExecuteAsync().Forget(LogTaskException);
            }

        }

        private async Task BenchmarkRpcAsync(string rpcUrl)
        {
            try
            {
                var responseTime = await AsyncPhantasma.FromApi<TimeSpan>(
                    (onSuccess, onError) => WebClient.Ping(rpcUrl, onError, onSuccess),
                    CancellationToken.None);

                lock (rpcResponseTimesPhantasma)
                {
                    rpcResponseTimesPhantasma.Add(new RpcBenchmarkData(rpcUrl, false, responseTime));
                }

                Interlocked.Increment(ref rpcAvailablePhantasma);
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
            PlayerPrefs.Save();
        }

        private async Task<TokenResult[]> GetTokensAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                try
                {
                    return await AsyncPhantasma.FromApi<TokenResult[]>(
                        (onSuccess, onError) => phantasmaApi.GetTokens(onSuccess, onError, 10, 5),
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

                if (ResourceManager.Instance != null)
                {
                    ResourceManager.Instance.UnloadTokens();
                }

            }

            CurrentTokenCurrency = "";

            Status = "ok";
            tokensReinitInProgress = false;

            if (refreshBalancesAfterTokenReload)
            {
                refreshBalancesAfterTokenReload = false;
                if (HasSelection)
                {
                    RefreshBalances(false);
                }
            }
        }

        public void RequestTokensReload()
        {
            ScheduleBalanceRefreshAfterTokens();
            TokensReinit();
        }

        private void ScheduleBalanceRefreshAfterTokens()
        {
            refreshBalancesAfterTokenReload = true;
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
                TokensReinit();
            }
        }

        private void LoadNexus()
        {
            UpdateAPIs(true);

            /*var tokenList = PlayerPrefs.GetString(TokenInfoTag, "");

            if (!string.IsNullOrEmpty(tokenList))
            {
                var tokenBytes = Base16.Decode(tokenList);

                var tokens = Serialization.Unserialize<Token[]>(tokenBytes);

                return;
            }

            StartCoroutine(phantasmaApi.GetTokens((tokens) =>
            {
                PrepareTokens(tokens);
                var tokenBytes = Serialization.Serialize(tokens);
                PlayerPrefs.SetString(TokenInfoTag, Base16.Encode(tokenBytes));
                return;
            },
            (error, msg) =>
            {
                Status = "Failed to fetch token list...";
            }));*/
        }

        // Update is called once per frame
        void Update()
        {

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
                                        phantasmaApi.SignAndSendTransaction(PhantasmaKeys.FromWIF(CurrentWif), Settings.nexusName, script, chain, payload, onSuccess, onError, customSignFunction),
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
                                if (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                                {
                                    ChangeFaultyRPCURL(PlatformKind.Phantasma);
                                }

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
                                        phantasmaApi.SignAndSendCarbonTransaction(PhantasmaKeys.FromWIF(CurrentWif), tx, onSuccess, onError),
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
                                if (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                                {
                                    ChangeFaultyRPCURL(PlatformKind.Phantasma);
                                }

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
                                    (onSuccess, onError) => phantasmaApi.InvokeRawScript(chain, Base16.Encode(script), onSuccess, onError),
                                    CancellationToken.None);

                                Log.Write("InvokeScript result: " + result.Result, Log.Level.Debug1);
                                callback(result.Results, null);
                            }
                            catch (PhantasmaRequestException ex)
                            {
                                if (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                                {
                                    ChangeFaultyRPCURL(PlatformKind.Phantasma);
                                }
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
                        (onSuccess, onError) => phantasmaApi.InvokeRawScript(chain, Base16.Encode(script), onSuccess, onError),
                        CancellationToken.None);
                    Log.Write("InvokeScriptPhantasma result: " + result.Result, Log.Level.Debug1);
                    callback(Base16.Decode(result.Result), null);
                }
                catch (PhantasmaRequestException ex)
                {
                    if (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                    {
                        ChangeFaultyRPCURL(PlatformKind.Phantasma);
                    }
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
                                if (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                                {
                                    ChangeFaultyRPCURL(PlatformKind.Phantasma);
                                }
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

            accountBalanceNotLoaded = true;
            accountHistoryNotLoaded = true;
        }

        public void UnselectAcount()
        {
            _selectedAccountIndex = -1;

            // revoke all dapps connected to this account via Phantasma Link
            if (_states.ContainsKey(PlatformKind.Phantasma))
            {
                var link = ConnectorManager.Instance.PhantasmaLink;

                var state = _states[PlatformKind.Phantasma];
                foreach (var entry in state.dappTokens)
                {
                    link.Revoke(entry.Key, entry.Value);
                }
            }

            _states.Clear();
            _nfts.Clear();
            _roms.Clear();
            TtrsStore.Clear();
            GameStore.Clear();
            NftImages.Clear();
            _refreshStatus.Clear();
        }

        private void ReportWalletBalance(PlatformKind platform, AccountState state)
        {
            try
            {
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

        private void ReportWalletNft(PlatformKind platform, string symbol)
        {
            lock (_refreshStatus)
            {
                if (_refreshStatus.ContainsKey(platform))
                {
                    var refreshStatus = _refreshStatus[platform];
                    refreshStatus.NftsRefreshing = false;
                    _refreshStatus[platform] = refreshStatus;
                }
            }

            if (_nfts.ContainsKey(platform) && _nfts[platform] != null)
            {
                Log.Write($"Received {_nfts[platform].Count()} new {symbol} NFTs for {platform}");

                if (CurrentPlatform == PlatformKind.None)
                {
                    CurrentPlatform = platform;
                }

                Log.Write($"[NFT] Invoking NftsUpdated for {platform} {symbol} (subscribers: {NftsUpdated?.GetInvocationList()?.Length ?? 0})"); //TODO Check if still needed once refactoring is over
                NftsUpdated?.Invoke(platform, symbol);
            }
        }

        private void ReportWalletHistory(PlatformKind platform, List<HistoryEntry> history)
        {
            try
            {
                lock (_refreshStatus)
                {
                    var refreshStatus = _refreshStatus[platform];
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
                                (onSuccess, onError) => phantasmaApi.GetTransaction(transactionHash, onSuccess, onError),
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
                            if (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                            {
                                ChangeFaultyRPCURL(PlatformKind.Phantasma);
                            }

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
                        (onSuccess, onError) => phantasmaApi.GetAccount(keys.Address.Text, onSuccess, onError),
                        CancellationToken.None);

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

                    ReportWalletBalance(PlatformKind.Phantasma, state);
                    SetBalanceError(PlatformKind.Phantasma, null);

                    if (missingTokens != null && missingTokens.Count > 0)
                    {
                        Log.WriteWarning($"RefreshBalances: detected unknown tokens ({string.Join(", ", missingTokens)}) - reloading token list.");
                        ScheduleBalanceRefreshAfterTokens();
                        TokensReinit();
                    }
                }
                catch (PhantasmaRequestException ex)
                {
                    Log.WriteWarning($"RefreshBalances[PHA] {ex.ErrorType}: {ex.Message}");

                    if (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                    {
                        ChangeFaultyRPCURL(PlatformKind.Phantasma);
                    }

                    SetBalanceError(PlatformKind.Phantasma, $"Phantasma request failed: {ex.Message}");
                    ReportWalletBalance(PlatformKind.Phantasma, null);
                }
                catch (FormatException ex)
                {
                    Log.WriteWarning($"RefreshBalances[PHA] parse error: {ex.Message}");
                    SetBalanceError(PlatformKind.Phantasma, $"Error while parsing balances: {ex.Message}");
                    ReportWalletBalance(PlatformKind.Phantasma, null);
                }
                catch (Exception ex)
                {
                    Log.WriteWarning($"RefreshBalances[PHA] unexpected error: {ex}");
                    SetBalanceError(PlatformKind.Phantasma, $"Error while fetching balances: {ex.Message}");
                    ReportWalletBalance(PlatformKind.Phantasma, null);
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

                lock (_refreshStatus)
                {
                    if (_refreshStatus.ContainsKey(PlatformKind.Phantasma))
                    {
                        var refreshStatus = _refreshStatus[PlatformKind.Phantasma];
                        refreshStatus.NftsRefreshing = true;
                        _refreshStatus[PlatformKind.Phantasma] = refreshStatus;
                    }
                    else
                    {
                        _refreshStatus.Add(PlatformKind.Phantasma,
                            new RefreshStatus
                            {
                                NftsRefreshing = true
                            });
                    }
                }

                Log.Write($"[NFT] RefreshNft start force={force} symbol={symbol} currentPlatform={CurrentPlatform}"); //TODO Check if still needed once refactoring is over
                foreach (var platform in CurrentAccount.platforms.Split())
                {
                    NftsRefreshStarted?.Invoke(platform, symbol);
                }

                if (force)
                {
                    // On force refresh we clear NFT symbol's cache.
                    if (symbol.ToUpper() == "TTRS")
                        TtrsStore.Clear();
                    else if (symbol.ToUpper() == "GAME")
                        GameStore.Clear();
                    else
                        Cache.ClearDataNode("tokens-" + symbol.ToLower(), Cache.FileType.JSON, CurrentState.address);

                    NftImages.Clear(symbol);
                }

                var platforms = CurrentAccount.platforms.Split();

                var wif = this.CurrentWif;

                foreach (var platform in platforms)
                {
                    // Reinitializing NFT dictionary if needed.
                    if (_nfts.ContainsKey(platform))
                        _nfts[platform].Clear();

                    if (Tokens.GetToken(symbol, platform, out var tokenInfo))
                    {
                        switch (platform)
                        {
                            case PlatformKind.Phantasma:
                                {
                                    var keys = PhantasmaKeys.FromWIF(wif);

                                    Log.Write("Getting NFTs...");
                                    foreach (var balanceEntry in CurrentState.balances)
                                    {
                                        if (balanceEntry.Symbol == symbol && !tokenInfo.IsFungible())
                                        {
                                            nftDescriptionsAreFullyLoaded = false;

                                            // Initializing NFT dictionary if needed.
                                            if (!_nfts.ContainsKey(platform))
                                            {
                                                _nfts.Add(platform, new List<TokenDataResult>());
                                                _roms.Add(platform, new());
                                            }

                                            var cache = Cache.GetTokenCache("tokens-" + symbol.ToLower(), Cache.FileType.JSON, 0, CurrentState.address);
                                            if (cache == null)
                                            {
                                                cache = new TokenDataResult[] { };
                                            }

                                            int loadedTokenCounter = 0;

                                            foreach (var id in balanceEntry.Ids)
                                            {
                                                TokenDataResult? tokenData = Cache.FindTokenData(cache, id);

                                                if (tokenData != null)
                                                {
                                                    var tokenId = tokenData.Id;

                                                    loadedTokenCounter++;

                                                    if (!_nfts[platform].Exists(x => x.Id == tokenId))
                                                    {
                                                        var rom = tokenData.ParseRom(symbol);
                                                        _roms[platform][tokenId] = rom;
                                                        var (hasError, error) = rom.HasParsingError();
                                                        if (rom.IsEmpty())
                                                        {
                                                            Log.Write($"ROM is null or empty");
                                                        }
                                                        else if (hasError)
                                                        {
                                                            Log.Write(error);
                                                        }

                                                        _nfts[platform].Add(tokenData);

                                                        NftImages.DownloadImageAsync(symbol, tokenData.GetPropertyValue("ImageURL"), id, CancellationToken.None).Forget(LogTaskException);
                                                    }

                                                    if (loadedTokenCounter == balanceEntry.Ids.Length)
                                                    {
                                                        Cache.SaveTokenDatas("tokens-" + symbol.ToLower(), Cache.FileType.JSON, cache, CurrentState.address);

                                                        if (symbol != "TTRS")
                                                        {
                                                            nftDescriptionsAreFullyLoaded = true;
                                                        }
                                                    }

                                                    if (loadedTokenCounter > 0)
                                                    {
                                                        ReportWalletNft(platform, symbol);
                                                    }
                                                }
                                                else
                                                {
                                                    if (symbol == "TTRS")
                                                    {
                                                        var tokenData2 = new TokenDataResult();
                                                        tokenData2.Id = id;
                                                        _nfts[platform].Add(tokenData2);

                                                        loadedTokenCounter++;

                                                        if (loadedTokenCounter > 0)
                                                        {
                                                            ReportWalletNft(platform, symbol);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        try
                                                        {
                                                            var tokenData2 = await AsyncPhantasma.FromApi<TokenDataResult>(
                                                                (onSuccess, onError) => phantasmaApi.GetNFT(symbol, id, true, onSuccess, onError),
                                                                CancellationToken.None);
                                                            var rom = tokenData2.ParseRom(symbol);
                                                            _roms[platform][id] = rom;
                                                            var (hasError, error) = rom.HasParsingError();
                                                            if (rom.IsEmpty())
                                                            {
                                                                Log.Write($"ROM is null or empty");
                                                            }
                                                            else if (hasError)
                                                            {
                                                                Log.Write(error);
                                                            }

                                                            NftImages.DownloadImageAsync(symbol, tokenData2.GetPropertyValue("ImageURL"), id, CancellationToken.None).Forget(LogTaskException);

                                                            loadedTokenCounter++;

                                                            _nfts[platform].Add(tokenData2);
                                                            cache = cache.Append(tokenData2).ToArray();

                                                            if (loadedTokenCounter == balanceEntry.Ids.Length)
                                                            {
                                                                Cache.SaveTokenDatas("tokens-" + symbol.ToLower(), Cache.FileType.JSON, cache, CurrentState.address);
                                                            }

                                                            if (loadedTokenCounter > 0)
                                                            {
                                                                ReportWalletNft(platform, symbol);
                                                            }
                                                        }
                                                        catch (PhantasmaRequestException ex)
                                                        {
                                                            loadedTokenCounter++;
                                                            Log.Write($"NFT loading error for {symbol}/{id}: {ex.Message}");
                                                        }
                                                    }
                                                }
                                            }

                                            if (balanceEntry.Ids.Length > 0)
                                            {
                                                if (symbol == "TTRS")
                                                {
                                                    await TtrsStore.LoadStoreNftAsync(balanceEntry.Ids, (item) =>
                                                        {
                                                            NftImages.DownloadImageAsync(symbol, item.item_info.image_url, item.id, CancellationToken.None).Forget(LogTaskException);
                                                        }, CancellationToken.None);

                                                    nftDescriptionsAreFullyLoaded = true;
                                                }
                                                else if (symbol == "GAME")
                                                {
                                                    await GameStore.LoadStoreNftAsync(balanceEntry.Ids, (item) =>
                                                        {
                                                            NftImages.DownloadImageAsync(symbol, item.parsed_rom.img_url, item.ID, CancellationToken.None).Forget(LogTaskException);
                                                        }, CancellationToken.None);

                                                    nftDescriptionsAreFullyLoaded = true;
                                                }
                                            }
                                        }
                                    }
                                }
                                break;

                            default:
                                ReportWalletNft(platform, symbol);
                                break;
                        }
                    }
                    else
                    {
                        ReportWalletNft(platform, symbol);
                    }
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

        public void RefreshHistory(bool force, PlatformKind platforms = PlatformKind.None)
        {
            async Task ExecuteAsync()
            {
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

                var keys = PhantasmaKeys.FromWIF(wif);
                try
                {
                    var result = await AsyncPhantasma.FromApi<AccountTransactionsResult, uint, uint>(
                        (onSuccess, onError) => phantasmaApi.GetAddressTransactions(keys.Address.Text, 1, 20, onSuccess, onError),
                        CancellationToken.None);
                    var (transactions, _, _) = result;

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

                    ReportWalletHistory(PlatformKind.Phantasma, history);
                }
                catch (PhantasmaRequestException ex)
                {
                    if (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                    {
                        ChangeFaultyRPCURL(PlatformKind.Phantasma);
                    }
                    ReportWalletHistory(PlatformKind.Phantasma, null);
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
                        (onSuccess, onError) => phantasmaApi.LookUpName(name, onSuccess, onError),
                        CancellationToken.None);
                    callback(address);
                }
                catch (PhantasmaRequestException ex)
                {
                    if (ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                    {
                        ChangeFaultyRPCURL(PlatformKind.Phantasma);
                    }
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
            if (_nfts[CurrentPlatform] == null)
                return;

            if (!nftDescriptionsAreFullyLoaded) // We should not sort NFTs if there are no attributes available.
                return;

            if (symbol == "TTRS")
            {
                if (currentTtrsNftsSortMode == (TtrsNftSortMode)Settings.ttrsNftSortMode && (int)currentNftsSortDirection == Settings.nftSortDirection)
                    return; // Nothing changed, no need to sort again.

                switch ((TtrsNftSortMode)Settings.ttrsNftSortMode)
                {
                    case TtrsNftSortMode.Number_Date:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => TtrsStore.GetNft(x.Id).mint).ThenBy(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => TtrsStore.GetNft(x.Id).mint).ThenByDescending(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        break;
                    case TtrsNftSortMode.Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => TtrsStore.GetNft(x.Id).timestamp).ThenBy(x => TtrsStore.GetNft(x.Id).mint).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => TtrsStore.GetNft(x.Id).timestamp).ThenByDescending(x => TtrsStore.GetNft(x.Id).mint).ToList();
                        break;
                    case TtrsNftSortMode.Type_Number_Date:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => TtrsStore.GetNft(x.Id).item_info.type).ThenBy(x => TtrsStore.GetNft(x.Id).mint).ThenBy(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => TtrsStore.GetNft(x.Id).item_info.type).ThenByDescending(x => TtrsStore.GetNft(x.Id).mint).ThenByDescending(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        break;
                    case TtrsNftSortMode.Type_Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => TtrsStore.GetNft(x.Id).item_info.type).ThenBy(x => TtrsStore.GetNft(x.Id).timestamp).ThenBy(x => TtrsStore.GetNft(x.Id).mint).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => TtrsStore.GetNft(x.Id).item_info.type).ThenByDescending(x => TtrsStore.GetNft(x.Id).timestamp).ThenByDescending(x => TtrsStore.GetNft(x.Id).mint).ToList();
                        break;
                    case TtrsNftSortMode.Type_Rarity: // And also Number and Date as last sorting parameters.
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => TtrsStore.GetNft(x.Id).item_info.type).ThenByDescending(x => TtrsStore.GetNft(x.Id).item_info.rarity).ThenBy(x => TtrsStore.GetNft(x.Id).mint).ThenBy(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => TtrsStore.GetNft(x.Id).item_info.type).ThenBy(x => TtrsStore.GetNft(x.Id).item_info.rarity).ThenByDescending(x => TtrsStore.GetNft(x.Id).mint).ThenByDescending(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        break;
                }

                currentTtrsNftsSortMode = (TtrsNftSortMode)Settings.ttrsNftSortMode;
            }
            else if (symbol == "GAME")
            {
                if (currentNftsSortMode == (NftSortMode)Settings.nftSortMode && (int)currentNftsSortDirection == Settings.nftSortDirection)
                    return; // Nothing changed, no need to sort again.

                switch ((NftSortMode)Settings.nftSortMode)
                {
                    case NftSortMode.Name:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => GameStore.GetNft(x.Id).meta?.name_english).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => GameStore.GetNft(x.Id).meta?.name_english).ToList();
                        break;
                    case NftSortMode.Number_Date:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => GameStore.GetNft(x.Id).mint).ThenBy(x => GameStore.GetNft(x.Id).parsed_rom.timestampDT()).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => GameStore.GetNft(x.Id).mint).ThenByDescending(x => GameStore.GetNft(x.Id).parsed_rom.timestampDT()).ToList();
                        break;
                    case NftSortMode.Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => GameStore.GetNft(x.Id).parsed_rom.timestampDT()).ThenBy(x => GameStore.GetNft(x.Id).mint).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => GameStore.GetNft(x.Id).parsed_rom.timestampDT()).ThenByDescending(x => GameStore.GetNft(x.Id).mint).ToList();
                        break;
                }

                currentNftsSortMode = (NftSortMode)Settings.nftSortMode;
            }
            else
            {
                if (currentNftsSortMode == (NftSortMode)Settings.nftSortMode && (int)currentNftsSortDirection == Settings.nftSortDirection)
                    return; // Nothing changed, no need to sort again.

                switch ((NftSortMode)Settings.nftSortMode)
                {
                    case NftSortMode.Name:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => GetNftRom(x.Id).GetName()).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => GetNftRom(x.Id).GetName()).ToList();
                        break;
                    case NftSortMode.Number_Date:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => GetNft(x.Id).Mint).ThenBy(x => GetNftRom(x.Id).GetDate()).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => GetNft(x.Id).Mint).ThenByDescending(x => GetNftRom(x.Id).GetDate()).ToList();
                        break;
                    case NftSortMode.Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => GetNftRom(x.Id).GetDate()).ThenBy(x => GetNft(x.Id).Mint).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => GetNftRom(x.Id).GetDate()).ThenByDescending(x => GetNft(x.Id).Mint).ToList();
                        break;
                }

                currentNftsSortMode = (NftSortMode)Settings.nftSortMode;
            }

            currentNftsSortDirection = (SortDirection)Settings.nftSortDirection;
        }

        public TokenDataResult GetNft(string id)
        {
            return _nfts[CurrentPlatform].Where(x => x.Id == id).FirstOrDefault();
        }

        public IRom GetNftRom(string id)
        {
            return _roms[CurrentPlatform][id];
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
