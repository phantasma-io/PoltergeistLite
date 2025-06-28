using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.VM;
using PhantasmaPhoenix.Core.Extensions;
using Newtonsoft.Json.Linq;
using PhantasmaIntegration;

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
        private Dictionary<PlatformKind, List<TokenData>> _nfts = new Dictionary<PlatformKind, List<TokenData>>();
        private Dictionary<PlatformKind, HistoryEntry[]> _history = new Dictionary<PlatformKind, HistoryEntry[]>();
        public Dictionary<PlatformKind, RefreshStatus> _refreshStatus = new Dictionary<PlatformKind, RefreshStatus>();

        public PlatformKind CurrentPlatform { get; set; }
        public AccountState CurrentState => _states.ContainsKey(CurrentPlatform) ? _states[CurrentPlatform] : null;
        public List<TokenData> CurrentNfts => _nfts.ContainsKey(CurrentPlatform) ? _nfts[CurrentPlatform] : null;
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

        private void Awake()
        {
            Instance = this;
            Settings = new Settings();

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
        }

        public string GetTokenWorth(string symbol, decimal amount)
        {
            bool hasLocalCurrency = !string.IsNullOrEmpty(CurrentTokenCurrency) && _currencyMap.ContainsKey(CurrentTokenCurrency);
            if (_tokenPrices.ContainsKey(symbol) && hasLocalCurrency)
            {
                var price = _tokenPrices[symbol] * amount;
                var ch = _currencyMap[CurrentTokenCurrency];
                return $"{WalletGUI.MoneyFormat(price, MoneyFormatType.Short)} {ch}";
            }
            else
            {
                return null;
            }
        }

        private IEnumerator FetchTokenPrices(IEnumerable<Token> symbols, string currency)
        {
            var separator = "%2C";
            var url = "https://api.coingecko.com/api/v3/simple/price?ids=" + string.Join(separator, symbols.Where(x => !String.IsNullOrEmpty(x.apiSymbol)).Select(x => x.apiSymbol).Distinct().ToList()) + "&vs_currencies=" + currency;
            return WebClient.RESTRequestT<Dictionary<string, Dictionary<string, decimal>>>(url, WebClient.DefaultTimeout, (error, msg) =>
            {

            },
            (response) =>
            {
                try
                {
                    foreach (var symbol in symbols)
                    {
                        var node = response.Where(x => x.Key.ToUpperInvariant() == symbol.apiSymbol.ToUpperInvariant()).Select(x => x.Value).FirstOrDefault();
                        if (node != default)
                        {
                            var price = node.Where(x => x.Key.ToUpperInvariant() == currency.ToUpperInvariant()).Select(x => x.Value).FirstOrDefault();

                            SetTokenPrice(symbol.symbol, price);
                        }
                        else
                        {
                            Log.Write($"Cannot get price for '{symbol.apiSymbol}'.");
                        }
                    }

                    // GOATI token price is pegged to 0.1$.
                    SetTokenPrice("GOATI", Convert.ToDecimal(0.1));
                }
                catch (Exception e)
                {
                    Log.WriteWarning(e.ToString());
                }
            });
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
            if (Settings.nexusKind != NexusKind.Main_Net && Settings.nexusKind != NexusKind.Test_Net)
            {
                rpcAvailablePhantasma = 1;
                return; // No need to change RPC, it is set by custom settings.
            }

            string url;
            if(Settings.nexusKind == NexusKind.Main_Net)
            {
                url = $"https://peers.phantasma.info/mainnet-getpeers.json";
            }
            else
            {
                url = $"https://peers.phantasma.info/testnet-getpeers.json";
            }

            rpcBenchmarkedPhantasma = 0;
            rpcResponseTimesPhantasma = new List<RpcBenchmarkData>();

            StartCoroutine(
                WebClient.RESTGet<JToken>(url, WebClient.DefaultTimeout, (error, msg) =>
                {
                    ReportGetPeersFailure = true;
                    Log.Write($"Couldn't retrieve RPCs list using url '{url}', error: " + error);
                },
                (response) =>
                {
                    if (response != null)
                    {
                        rpcNumberPhantasma = response.Count();

                        if (String.IsNullOrEmpty(Settings.phantasmaRPCURL))
                        {
                            // If we have no previously used RPC, we select random one at first.
                            var index = ((int)(Time.realtimeSinceStartup * 1000)) % rpcNumberPhantasma;
                            var node = response[index];
                            var result = node.Value<string>("url") + "/rpc";
                            Settings.phantasmaRPCURL = result;
                            Log.Write($"Changed Phantasma RPC url {index} => {result}");
                        }

                        UpdateAPIs();

                        // Benchmarking RPCs.
                        foreach (var node in response.Children())
                        {
                            var rpcUrl = node.Value<string>("url") + "/rpc";

                            StartCoroutine(
                                WebClient.Ping(rpcUrl, (error, msg) =>
                                {
                                    Log.Write("Ping error: " + error);

                                    rpcBenchmarkedPhantasma++;

                                    lock (rpcResponseTimesPhantasma)
                                    {
                                        rpcResponseTimesPhantasma.Add(new RpcBenchmarkData(rpcUrl, true, new TimeSpan()));
                                    }

                                    if (rpcBenchmarkedPhantasma == rpcNumberPhantasma)
                                    {
                                        // We finished benchmarking, time to select best RPC server.
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
                                },
                                (responseTime) =>
                                {
                                    rpcBenchmarkedPhantasma++;

                                    rpcAvailablePhantasma++;

                                    lock (rpcResponseTimesPhantasma)
                                    {
                                        rpcResponseTimesPhantasma.Add(new RpcBenchmarkData(rpcUrl, false, responseTime));
                                    }

                                    if (rpcBenchmarkedPhantasma == rpcNumberPhantasma)
                                    {
                                        // We finished benchmarking, time to select best RPC server.
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
                                })
                            );
                        }
                    }
                })
            );
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
                if(rpcAvailablePhantasma > 0)
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

                WalletGUI.MessageForUser(@"A note for existing Poltergeist wallet users!
 
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

        private IEnumerator GetTokens(Action<Token[]> callback)
        {
            while (!Ready)
            {
                var coroutine = StartCoroutine(phantasmaApi.GetTokens((tokens) =>
                {
                    callback(tokens);
                }, (error, msg) =>
                {
                    if (rpcAvailablePhantasma > 0 && Settings.nexusKind == NexusKind.Main_Net)
                    {
                        ChangeFaultyRPCURL(PlatformKind.Phantasma);
                    }
                    else
                    {
                        CurrentTokenCurrency = "";

                        AccountManager.Instance.Settings.settingRequireReconfiguration = true;
                        Status = "ok"; // We are launching with uninitialized tokens,
                                       // to allow user to edit settings.
                        
                        Log.WriteWarning("Error: Launching with uninitialized tokens.");
                    }

                    Log.WriteWarning("Tokens initialization error: " + msg);
                }));

                yield return coroutine;
            }
        }

        private void TokensReinit()
        {
            StartCoroutine(GetTokens((tokens) =>
            {
                Tokens.Init(tokens);

                CurrentTokenCurrency = "";

                Status = "ok";
            }));
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

                StartCoroutine(FetchTokenPrices(Tokens.GetTokensForCoingecko(), CurrentTokenCurrency));
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

        public decimal AmountFromString(string str, int decimals)
        {
            if (string.IsNullOrEmpty(str))
            {
                return 0;
            }

            if(decimals < 0)
            {
                throw new ($"Decimals for token are unavailable, cannot convert {str} amount");
            }

            return UnitConversion.ToDecimal(str, decimals);
        }

        public void SignAndSendTransaction(string chain, byte[] script, TransferRequest? transferRequest, BigInteger phaGasPrice, BigInteger phaGasLimit, byte[] payload, ProofOfWork PoW, IKeyPair customKeys, Action<Hash, string> callback, Func<byte[], byte[], byte[], byte[]> customSignFunction = null)
        {
            if (payload == null)
            {
                payload = System.Text.Encoding.UTF8.GetBytes(WalletIdentifier);
            }

            switch (CurrentPlatform)
            {
                case PlatformKind.Phantasma:
                    {
                        StartCoroutine(phantasmaApi.SignAndSendTransactionWithPayload(PhantasmaKeys.FromWIF(CurrentWif), customKeys, Settings.nexusName, script, chain, phaGasPrice, phaGasLimit, payload, PoW, (hashText, encodedTx, txHash) =>
                        {
                            if (Settings.devMode)
                            {
                                Log.Write($"SignAndSendTransactionWithPayload(): Encoded tx: {encodedTx}");
                            }
                            if ( !string.IsNullOrEmpty(hashText) )
                            {
                                try
                                {
                                    var hash = Hash.Parse(hashText);

                                    if(hash != txHash)
                                    {
                                        callback(hash,  $"Error: RPC returned different hash, expected {txHash}");
                                        return;
                                    }

                                    callback(hash, null);

                                }catch (Exception e)
                                {
                                    Log.WriteWarning("Error parsing hash: " + e.Message);
                                    callback(Hash.Null,  $"Error: hashText={hashText}");
                                    return;
                                }
                            }
                            else
                            {
                                callback(Hash.Null, "Failed to send transaction");
                            }
                        }, (error, msg) =>
                        {
                            if(error == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                            {
                                ChangeFaultyRPCURL(PlatformKind.Phantasma);
                            }
                            callback(Hash.Null, msg);
                        }, customSignFunction));
                        break;
                    }

                default:
                    {
                        callback(Hash.Null, "not implemented for " + CurrentPlatform);
                        break;
                    }
            }
        }

        public void InvokeScript(string chain, byte[] script, Action<string[], string> callback)
        {
            var account = this.CurrentAccount;

            switch (CurrentPlatform)
            {
                case PlatformKind.Phantasma:
                    {
                        Log.Write("InvokeScript: " + System.Text.Encoding.UTF8.GetString(script), Log.Level.Debug1);
                        StartCoroutine(phantasmaApi.InvokeRawScript(chain, Base16.Encode(script), (x) =>
                        {
                            Log.Write("InvokeScript result: " + x.result, Log.Level.Debug1);
                            callback(x.results, null);
                        }, (error, log) =>
                        {
                            if (error == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                            {
                                ChangeFaultyRPCURL(PlatformKind.Phantasma);
                            }
                            callback(null, log);
                        }));
                        break;
                    }
                default:
                    {
                        callback(null, "not implemented for " + CurrentPlatform);
                        break;
                    }
            }
        }

        public void InvokeScriptPhantasma(string chain, byte[] script, Action<byte[], string> callback)
        {
            var account = this.CurrentAccount;

            Log.Write("InvokeScriptPhantasma: " + System.Text.Encoding.UTF8.GetString(script), Log.Level.Debug1);
            StartCoroutine(phantasmaApi.InvokeRawScript(chain, Base16.Encode(script), (x) =>
            {
                Log.Write("InvokeScriptPhantasma result: " + x.result, Log.Level.Debug1);
                callback(Base16.Decode(x.result), null);
            }, (error, log) =>
            {
                if (error == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                {
                    ChangeFaultyRPCURL(PlatformKind.Phantasma);
                }
                callback(null, log);
            }));
        }

        public void WriteArchive(Hash hash, int blockIndex, byte[] data, Action<bool, string> callback)
        {
            var account = this.CurrentAccount;

            switch (CurrentPlatform)
            {
                case PlatformKind.Phantasma:
                    {
                        Log.Write("WriteArchive: " + hash, Log.Level.Debug1);
                        StartCoroutine(phantasmaApi.WriteArchive(hash.ToString(), blockIndex, data, (result) =>
                        {
                            Log.Write("WriteArchive result: " + result, Log.Level.Debug1);
                            callback(result, null);
                        }, (error, log) =>
                        {
                            if (error == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                            {
                                ChangeFaultyRPCURL(PlatformKind.Phantasma);
                            }
                            callback(false, log);
                        }));
                        break;
                    }
                default:
                    {
                        callback(false, "not implemented for " + CurrentPlatform);
                        break;
                    }
            }
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
                    refreshStatus = _refreshStatus[platform];
                    refreshStatus.BalanceRefreshing = false;
                    _refreshStatus[platform] = refreshStatus;
                }

                if (state != null)
                {
                    Log.Write("Received new state for " + platform);
                    _states[platform] = state;
                }
            
                var temp = refreshStatus.BalanceRefreshCallback;
                lock (_refreshStatus)
                {
                    refreshStatus.BalanceRefreshCallback = null;
                    _refreshStatus[platform] = refreshStatus;
                }
                temp?.Invoke();
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

                if (history != null)
                {
                    Log.Write("Received new history for " + platform);
                    _history[platform] = history.ToArray();

                    if (CurrentPlatform == PlatformKind.None)
                    {
                        CurrentPlatform = platform;
                    }
                }
            }
            catch (Exception) { } // This fixes crash when user leaves account fast without waiting for balances to load
        }


        private const int maxChecks = 12; // Timeout after 36 seconds

        public void RequestConfirmation(string transactionHash, int checkCount, Action<PhantasmaIntegration.Transaction?, string> callback)
        {
            switch (CurrentPlatform)
            {
                case PlatformKind.Phantasma:
                    StartCoroutine(phantasmaApi.GetTransaction(transactionHash, (txResult) =>
                    {
                        if (txResult.Value.state == ExecutionState.Running)
                        {
                            callback(txResult, "pending");
                        }
                        else if (txResult.Value.state == ExecutionState.Break || txResult.Value.state == ExecutionState.Fault)
                        {
                            if(string.IsNullOrEmpty(txResult.Value.debugComment) && checkCount <= 6)
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
                    }, (error, msg) =>
                    {
                        if (error == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                        {
                            ChangeFaultyRPCURL(PlatformKind.Phantasma);
                        }

                        if (checkCount <= maxChecks)
                        {
                            if (error == EPHANTASMA_SDK_ERROR_TYPE.FAILED_PARSING_JSON)
                            {
                                msg = "Cannot determine if transaction was successful or not due to incorrect RPC response. " + msg;
                            }
                            else if(msg.ToUpperInvariant().Contains("PENDING") || msg.ToUpperInvariant().Contains("TRANSACTION NOT FOUND"))
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
                    }));
                    break;

                default:
                    callback(null, "not implemented: " + CurrentPlatform);
                    break;
            }

        }

        public void RefreshBalances(bool force, PlatformKind platforms = PlatformKind.None, Action callback = null)
        {
            List<PlatformKind> platformsList;
            if(platforms == PlatformKind.None)
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

                    refreshStatus.BalanceRefreshing = true;
                    refreshStatus.LastBalanceRefresh = now;
                    refreshStatus.BalanceRefreshCallback = callback;

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
                            HistoryRefreshing = false,
                            LastHistoryRefresh = DateTime.MinValue
                        });
                }
            }

            var wif = CurrentWif;

            lock (Tokens.__lockObj)
            {
                var keys = PhantasmaKeys.FromWIF(wif);
                var ethKeys = PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.FromWIF(wif);
                UpdateOpenAccount();
                StartCoroutine(phantasmaApi.GetAccount(keys.Address.Text, (acc) =>
                {
                    var balanceMap = new Dictionary<string, Balance>();

                    foreach (var entry in acc.balances)
                    {

                        var token = Tokens.GetToken(entry.symbol, PlatformKind.Phantasma);
                        if (token != null)
                            balanceMap[entry.symbol] = new Balance()
                            {
                                Symbol = entry.symbol,
                                Available = AmountFromString(entry.amount, token.decimals),
                                Staked = 0,
                                Claimable = 0,
                                Chain = entry.chain,
                                Decimals = token.decimals,
                                Burnable = token.IsBurnable(),
                                Fungible = token.IsFungible(),
                                Ids = entry.ids
                            };
                        else
                            balanceMap[entry.symbol] = new Balance()
                            {
                                Symbol = entry.symbol,
                                Available = AmountFromString(entry.amount, 8),
                                Staked = 0,
                                Claimable = 0,
                                Chain = entry.chain,
                                Decimals = 8,
                                Burnable = true,
                                Fungible = true,
                                Ids = entry.ids
                            };


                    }

                    var stakedAmount = AmountFromString(acc.stakes.amount,
                        Tokens.GetTokenDecimals("SOUL", PlatformKind.Phantasma));
                    var claimableAmount = AmountFromString(acc.stakes.unclaimed,
                        Tokens.GetTokenDecimals("KCAL", PlatformKind.Phantasma));

                    var stakeTimestamp = new Timestamp(acc.stakes.time);

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
                                Available = 0,
                                Staked = stakedAmount,
                                Claimable = 0,
                                Decimals = token.decimals,
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
                                Available = 0,
                                Staked = 0,
                                Claimable = claimableAmount,
                                Decimals = token.decimals,
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

                    // State without swaps
                    var state = new AccountState()
                    {
                        platform = PlatformKind.Phantasma,
                        address = acc.address,
                        name = acc.name,
                        balances = balanceMap.Values.ToArray(),
                        flags = AccountFlags.None
                    };

                    if (stakedAmount >= SoulMasterStakeAmount)
                    {
                        state.flags |= AccountFlags.Master;
                    }

                    if (acc.validator.Equals("Primary") || acc.validator.Equals("Secondary"))
                    {
                        state.flags |= AccountFlags.Validator;
                    }

                    state.stakeTime = stakeTimestamp;

                    state.usedStorage = acc.storage.used;
                    state.availableStorage = acc.storage.available;
                    state.archives = acc.storage.archives;
                    state.avatarData = acc.storage.avatar;

                    ReportWalletBalance(PlatformKind.Phantasma, state);
                },
                (error, msg) =>
                {
                    Log.WriteWarning($"RefreshBalances[PHA] {error}: {msg}");

                    if (error == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                    {
                        ChangeFaultyRPCURL(PlatformKind.Phantasma);
                    }

                    ReportWalletBalance(PlatformKind.Phantasma, null);
                }));
            }
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
                                            _nfts.Add(platform, new List<TokenData>());

                                        var cache = Cache.GetTokenCache("tokens-" + symbol.ToLower(), Cache.FileType.JSON, 0, CurrentState.address);
                                        if(cache == null)
                                        {
                                            cache = new TokenData[]{};
                                        }

                                        int loadedTokenCounter = 0;

                                        foreach (var id in balanceEntry.Ids)
                                        {
                                            // Checking if token is cached.
                                            TokenData? tokenData = Cache.FindTokenData(cache, id);

                                            if (tokenData != null)
                                            {
                                                // Loading token from cache.
                                                var tokenId = tokenData.Value.ID;

                                                loadedTokenCounter++;

                                                // Checking if token already loaded to dictionary.
                                                if (!_nfts[platform].Exists(x => x.ID == tokenId))
                                                {
                                                    tokenData.Value.ParseRoms(symbol);
                                                    _nfts[platform].Add(tokenData.Value);

                                                    // Downloading NFT images.
                                                    StartCoroutine(NftImages.DownloadImage(symbol, tokenData.Value.GetPropertyValue("ImageURL"), id));
                                                }

                                                if (loadedTokenCounter == balanceEntry.Ids.Length)
                                                {
                                                    // We finished loading tokens.
                                                    // Saving them in cache.
                                                    Cache.SaveTokenDatas("tokens-" + symbol.ToLower(), Cache.FileType.JSON, cache, CurrentState.address);

                                                    if (symbol != "TTRS")
                                                    {
                                                        // For all NFTs except TTRS all needed information
                                                        // is loaded by this moment.
                                                        nftDescriptionsAreFullyLoaded = true;
                                                    }
                                                }
                                                
                                                if (loadedTokenCounter > 0)
                                                {
                                                    // We mark process as ready after first NFT loaded to make process async
                                                    ReportWalletNft(platform, symbol);
                                                }
                                            }
                                            else
                                            {
                                                if (symbol == "TTRS")
                                                {
                                                    // TODO: Load TokenData for TTRS too (add batch load method for TokenDatas).
                                                    // For now we skip TokenData loading to speed up TTRS NFTs loading,
                                                    // since it's not used for TTRS anyway.
                                                    var tokenData2 = new TokenData();
                                                    tokenData2.ID = id;
                                                    _nfts[platform].Add(tokenData2);

                                                    loadedTokenCounter++;

                                                    if (loadedTokenCounter > 0)
                                                    {
                                                        // We mark process as ready after first NFT loaded to make process async
                                                        ReportWalletNft(platform, symbol);
                                                    }
                                                }
                                                else
                                                {
                                                    StartCoroutine(phantasmaApi.GetNFT(symbol, id, (tokenData2) =>
                                                    {
                                                        tokenData2.ParseRoms(symbol);

                                                        // Downloading NFT images.
                                                        StartCoroutine(NftImages.DownloadImage(symbol, tokenData2.GetPropertyValue("ImageURL"), id));

                                                        loadedTokenCounter++;

                                                        _nfts[platform].Add(tokenData2);
                                                        cache = cache.Append(tokenData2).ToArray();

                                                        if (loadedTokenCounter == balanceEntry.Ids.Length)
                                                        {
                                                            // We finished loading tokens.
                                                            // Saving them in cache.
                                                            Cache.SaveTokenDatas("tokens-" + symbol.ToLower(), Cache.FileType.JSON, cache, CurrentState.address);
                                                        }

                                                        if (loadedTokenCounter > 0)
                                                        {
                                                            // We mark process as ready after first NFT loaded to make process async
                                                            ReportWalletNft(platform, symbol);
                                                        }
                                                    }, (error, msg) =>
                                                    {
                                                        loadedTokenCounter++;
                                                        Log.Write($"NFT loading error for {symbol}/{id}: {msg}");
                                                    }));
                                                }
                                            }
                                        }

                                        if (balanceEntry.Ids.Length > 0)
                                        {
                                            // Getting NFT descriptions.
                                            if (symbol == "TTRS")
                                            {
                                                StartCoroutine(TtrsStore.LoadStoreNft(balanceEntry.Ids, (item) =>
                                                {
                                                    // Downloading NFT images.
                                                    StartCoroutine(NftImages.DownloadImage(symbol, item.item_info.image_url, item.id));
                                                }, () =>
                                                {
                                                    nftDescriptionsAreFullyLoaded = true;
                                                }));
                                            }
                                            else if (symbol == "GAME")
                                            {
                                                StartCoroutine(GameStore.LoadStoreNft(balanceEntry.Ids, (item) =>
                                                {
                                                    // Downloading NFT images.
                                                    StartCoroutine(NftImages.DownloadImage(symbol, item.parsed_rom.img_url, item.ID));
                                                }, () =>
                                                {
                                                    nftDescriptionsAreFullyLoaded = true;
                                                }));
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

        public void RefreshHistory(bool force, PlatformKind platforms = PlatformKind.None)
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

            var wif = this.CurrentWif;

            var keys = PhantasmaKeys.FromWIF(wif);
            StartCoroutine(phantasmaApi.GetAddressTransactions(keys.Address.Text, 1, 20, (x, page, max) =>
            {
                var history = new List<HistoryEntry>();

                foreach (var tx in x.txs)
                {
                    history.Add(new HistoryEntry()
                    {
                        hash = tx.hash,
                        date = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(tx.timestamp).ToLocalTime(),
                        url = GetPhantasmaTransactionURL(tx.hash)
                    });
                }

                ReportWalletHistory(PlatformKind.Phantasma, history);
            },
            (error, msg) =>
            {
                if (error == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                {
                    ChangeFaultyRPCURL(PlatformKind.Phantasma);
                }
                ReportWalletHistory(PlatformKind.Phantasma, null);
            }));
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
            if (currentIndex<0 || currentIndex >= Accounts.Count())
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

            for(var i = 0; i < Accounts.Count; i++)
            {
                if(i != currentIndex && Accounts[i].phaAddress == account.phaAddress)
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

        internal void ValidateAccountName(string name, Action<string> callback)
        {
            StartCoroutine(
                this.phantasmaApi.LookUpName(name, (address) =>
                {
                    callback(address);
                },
                (error, msg) =>
                {
                    if (error == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                    {
                        ChangeFaultyRPCURL(PlatformKind.Phantasma);
                    }
                    callback(null);
                })
            );
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
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => TtrsStore.GetNft(x.ID).mint).ThenBy(x => TtrsStore.GetNft(x.ID).timestamp).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => TtrsStore.GetNft(x.ID).mint).ThenByDescending(x => TtrsStore.GetNft(x.ID).timestamp).ToList();
                        break;
                    case TtrsNftSortMode.Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => TtrsStore.GetNft(x.ID).timestamp).ThenBy(x => TtrsStore.GetNft(x.ID).mint).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => TtrsStore.GetNft(x.ID).timestamp).ThenByDescending(x => TtrsStore.GetNft(x.ID).mint).ToList();
                        break;
                    case TtrsNftSortMode.Type_Number_Date:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => TtrsStore.GetNft(x.ID).item_info.type).ThenBy(x => TtrsStore.GetNft(x.ID).mint).ThenBy(x => TtrsStore.GetNft(x.ID).timestamp).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => TtrsStore.GetNft(x.ID).item_info.type).ThenByDescending(x => TtrsStore.GetNft(x.ID).mint).ThenByDescending(x => TtrsStore.GetNft(x.ID).timestamp).ToList();
                        break;
                    case TtrsNftSortMode.Type_Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => TtrsStore.GetNft(x.ID).item_info.type).ThenBy(x => TtrsStore.GetNft(x.ID).timestamp).ThenBy(x => TtrsStore.GetNft(x.ID).mint).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => TtrsStore.GetNft(x.ID).item_info.type).ThenByDescending(x => TtrsStore.GetNft(x.ID).timestamp).ThenByDescending(x => TtrsStore.GetNft(x.ID).mint).ToList();
                        break;
                    case TtrsNftSortMode.Type_Rarity: // And also Number and Date as last sorting parameters.
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => TtrsStore.GetNft(x.ID).item_info.type).ThenByDescending(x => TtrsStore.GetNft(x.ID).item_info.rarity).ThenBy(x => TtrsStore.GetNft(x.ID).mint).ThenBy(x => TtrsStore.GetNft(x.ID).timestamp).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => TtrsStore.GetNft(x.ID).item_info.type).ThenBy(x => TtrsStore.GetNft(x.ID).item_info.rarity).ThenByDescending(x => TtrsStore.GetNft(x.ID).mint).ThenByDescending(x => TtrsStore.GetNft(x.ID).timestamp).ToList();
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
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => GameStore.GetNft(x.ID).meta?.name_english).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => GameStore.GetNft(x.ID).meta?.name_english).ToList();
                        break;
                    case NftSortMode.Number_Date:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => GameStore.GetNft(x.ID).mint).ThenBy(x => GameStore.GetNft(x.ID).parsed_rom.timestampDT()).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => GameStore.GetNft(x.ID).mint).ThenByDescending(x => GameStore.GetNft(x.ID).parsed_rom.timestampDT()).ToList();
                        break;
                    case NftSortMode.Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => GameStore.GetNft(x.ID).parsed_rom.timestampDT()).ThenBy(x => GameStore.GetNft(x.ID).mint).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => GameStore.GetNft(x.ID).parsed_rom.timestampDT()).ThenByDescending(x => GameStore.GetNft(x.ID).mint).ToList();
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
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => GetNft(x.ID).parsedRom.GetName()).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => GetNft(x.ID).parsedRom.GetName()).ToList();
                        break;
                    case NftSortMode.Number_Date:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => GetNft(x.ID).mint).ThenBy(x => GetNft(x.ID).parsedRom.GetDate()).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => GetNft(x.ID).mint).ThenByDescending(x => GetNft(x.ID).parsedRom.GetDate()).ToList();
                        break;
                    case NftSortMode.Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderBy(x => GetNft(x.ID).parsedRom.GetDate()).ThenBy(x => GetNft(x.ID).mint).ToList();
                        else
                            _nfts[CurrentPlatform] = _nfts[CurrentPlatform].OrderByDescending(x => GetNft(x.ID).parsedRom.GetDate()).ThenByDescending(x => GetNft(x.ID).mint).ToList();
                        break;
                }

                currentNftsSortMode = (NftSortMode)Settings.nftSortMode;
            }
            
            currentNftsSortDirection = (SortDirection)Settings.nftSortDirection;
        }

        public TokenData GetNft(string id)
        {
            return _nfts[CurrentPlatform].Where(x => x.ID == id).FirstOrDefault();
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
                                                                        var unclaimed = unclaimedResult != null ? UnitConversion.ToDecimal(VMObject.FromBytes(unclaimedResult).AsNumber(), 10) : -1;
                                                                        var stake = stakeResult != null ? UnitConversion.ToDecimal(VMObject.FromBytes(stakeResult).AsNumber(), 8) : -1;
                                                                        var storageStake = storageStakeResult != null ? UnitConversion.ToDecimal(VMObject.FromBytes(storageStakeResult).AsNumber(), 8) : -1;
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
