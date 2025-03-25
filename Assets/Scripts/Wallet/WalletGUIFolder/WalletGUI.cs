using System;
using System.Linq;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

using Phantasma.SDK;

using ZXing;
using ZXing.QrCode;
using System.Globalization;
using System.Collections;
using System.Threading;
using Poltergeist.PhantasmaLegacy.Ethereum;
using BigInteger = System.Numerics.BigInteger;
using Phantasma.Core.Cryptography;
using Phantasma.Core.Numerics;
using Phantasma.Core.Domain;
using Phantasma.Business.VM.Utils;
using Phantasma.Business.Blockchain;
using Phantasma.Core.Types;
using NBitcoin;
using Phantasma.Core.Cryptography.EdDSA;
using Poltergeist.Neo2.Core;
using Phantasma.Core.Cryptography.ECDsa;

namespace Poltergeist
{
    public partial class WalletGUI : MonoBehaviour
    {
        public RawImage background;
        private Texture2D soulMasterLogo;

        private Dictionary<PlatformKind, Texture2D> QRCodeTextures = new Dictionary<PlatformKind, Texture2D>();

        public const string WalletTitle = "Poltergeist Lite";

        public int Border => Units(1);
        public int HalfBorder => Border / 2;
        public const bool fullScreen = true;
        public bool VerticalLayout => virtualWidth < virtualHeight; //virtualWidth < 420;

        private Rect windowRect = new Rect(0, 0, 600, 400);
        private Rect defaultRect;

        private Rect modalRect;

        private GUIState guiState;
        private Stack<GUIState> stateStack = new Stack<GUIState>();

        private string transferSymbol;
        private Hash transactionHash;
        private bool transactionStillPending;
        private int transactionCheckCount;
        private DateTime transactionLastCheck;
        private bool refreshBalanceAfterConfirmation;

        private AnimationDirection currentAnimation;
        private float animationTime;
        private bool invertAnimation;
        private Action animationCallback;

        private bool HasAnimation => currentAnimation != AnimationDirection.None;

        private string currentTitle;

        private string newWalletSeedPhrase;
        private Action newWalletCallback;

        private ComboBox hintComboBox = new ComboBox();

        // NFT sorting and filtering.
        private ComboBox nftSortModeComboBox = new ComboBox();
        private string nftFilterName;
        private ComboBox nftTypeComboBox = new ComboBox();
        private int nftFilterTypeIndex = 0;
        private string nftFilterType = "All";
        private ComboBox nftRarityComboBox = new ComboBox();
        private int nftFilterRarity = 0;
        private ComboBox nftMintedComboBox = new ComboBox();
        private int nftFilterMinted = 0;

        // NFT pagination.
        private int nftPageSize = 25;
        private int nftPageNumber = 0;
        private int nftCount = 0;
        private int nftPageCount = 0;
        private List<TokenData> nftFilteredList = new List<TokenData>(); // List of displayed NFT items (after applying filters).
        private List<string> nftTransferList = new List<string>(); // List of NFT items, selected by user.

        private List<string> accountManagementSelectedList = new List<string>();

        private bool initialized;

        private int virtualWidth;
        private int virtualHeight;

        private string fatalError;

        public static WalletGUI Instance { get; private set; }

        // Helps to close opened drop-down lists when they are not needed any more.
        private void ResetAllCombos()
        {
            currencyComboBox.ResetState();
            hintComboBox.ResetState();
            nexusComboBox.ResetState();
            mnemonicPhraseLengthComboBox.ResetState();
            mnemonicPhraseVerificationModeComboBox.ResetState();
            passwordModeComboBox.ResetState();
            logLevelComboBox.ResetState();
            uiThemeComboBox.ResetState();
            nftSortModeComboBox.ResetState();
            nftTypeComboBox.ResetState();
            nftRarityComboBox.ResetState();
            nftMintedComboBox.ResetState();
        }

        public static int Units(int n)
        {
            return 16 * n;
        }

        public static string MoneyFormat(decimal amount, MoneyFormatType formatType = MoneyFormatType.Standard)
        {
            switch (formatType)
            {
                case MoneyFormatType.Short:
                    amount -= amount % 0.01M; // Getting rid of deceiving rounding.
                    return amount.ToString("#,0.##");
                case MoneyFormatType.Standard:
                    amount -= amount % 0.0001M;
                    return amount.ToString("#,0.####");
                case MoneyFormatType.Long:
                    amount -= amount % 0.000000000001M;
                    return amount.ToString("#,0.############");
                default:
                    return amount.ToString();
            }
        }

        private void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            // Getting wallet's command line args.
            string[] _args = System.Environment.GetCommandLineArgs();

            // We have to get these settings prior to Settings.Load() call,
            // to initialize log properly.
            AccountManager.Instance.Settings.LoadLogSettings();

            Log.Level _logLevel = AccountManager.Instance.Settings.logLevel;
            var _logOverwriteMode = AccountManager.Instance.Settings.logOverwriteMode;
            bool _logForceWorkingFolderUsage = false;

            // Checking if log options are set in command line.
            // They override settings (for debug purposes).
            for (int i = 0; i < _args.Length; i++)
            {
                switch (_args[i])
                {
                    case "--log-level":
                        {
                            if (i + 1 < _args.Length)
                            {
                                Enum.TryParse<Log.Level>(_args[i + 1], true, out _logLevel);
                            }

                            break;
                        }

                    case "--log-force-working-folder-usage":
                        {
                            _logForceWorkingFolderUsage = true;

                            break;
                        }
                }
            }

            Log.Init("poltergeist.log", _logLevel, _logForceWorkingFolderUsage, _logOverwriteMode);
            Log.Write("********************************************************\n" +
                       "************** Poltergeist Wallet started **************\n" +
                       "********************************************************\n" +
                       "Wallet version: " + UnityEngine.Application.version + $" built on: { Poltergeist.Build.Info.Instance.BuildTime} UTC\n" +
                       "Log level: " + _logLevel.ToString());

            Cache.Init("cache");

            initialized = false;

            guiState = GUIState.Loading;

            Log.Write(Screen.width + " x " + Screen.height);
            currencyOptions = AccountManager.Instance.Currencies.ToArray();

            // We will use this RawImage object to set/change background image.
            background = GameObject.Find("Background").GetComponent<RawImage>();
        }

        void OnEnable()
        {
            Application.logMessageReceived += LogCallback;
        }

        void LogCallback(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
            {
                fatalError = condition + "\nStack trace:\n" + stackTrace;
                Log.Write($"Fatal error: {fatalError}");
                SetState(GUIState.Fatal);
            }
        }

        void OnDisable()
        {
            Application.logMessageReceived -= LogCallback;
        }

        #region UTILS
        private void PushState(GUIState state)
        {
            if (guiState != GUIState.Loading)
            {
                stateStack.Push(guiState);
            }

            SetState(state);
        }

        private void SetState(GUIState state)
        {
            ResetAllCombos();

            switch (guiState)
            {
                case GUIState.Backup:
                    newWalletSeedPhrase = null;
                    newWalletCallback = null;
                    break;

                case GUIState.ScanQR:
                    if (camTexture != null)
                    {
                        camTexture.Stop();
                        camTexture = null;
                    }
                    break;
            }

            if (state == GUIState.Exit)
            {
                CloseCurrentStack();
                return;
            }

            guiState = state;

            var accountManager = AccountManager.Instance;

            currentTitle = null;

            switch (state)
            {
                case GUIState.Fatal:
                    currentTitle = "Fatal Error";
                    break;

                case GUIState.Wallets:
                    currentTitle = "Wallet List";

                    foreach (var tex in QRCodeTextures.Values)
                    {
                        Texture2D.Destroy(tex);
                    }

                    QRCodeTextures.Clear();
                    break;

                case GUIState.Balances:
                    currentTitle = "Balances for " + accountManager.CurrentAccount.name;
                    balanceScroll = Vector2.zero;

                    // We do this only when account was just opened.
                    // We don't do this on every consequent state change.
                    if (accountManager.accountBalanceNotLoaded)
                    {
                        accountManager.RefreshBalances(true);
                        accountManager.accountBalanceNotLoaded = false;
                    }
                    break;

                case GUIState.Nft:
                case GUIState.NftView:
                    currentTitle = transferSymbol + " NFTs for " + accountManager.CurrentAccount.name;
                    accountManager.ResetNftsSorting();
                    break;

                case GUIState.NftTransferList:
                    currentTitle = transferSymbol + " NFTs transfer list for " + accountManager.CurrentAccount.name;
                    nftTransferListScroll = Vector2.zero;
                    break;

                case GUIState.History:
                    currentTitle = "History for " + accountManager.CurrentAccount.name;

                    // We do this only when account was just opened.
                    // We don't do this on every consequent state change.
                    if (accountManager.accountHistoryNotLoaded)
                    {
                        accountManager.RefreshHistory(true);
                        accountManager.accountHistoryNotLoaded = false;
                    }
                    break;

                case GUIState.Account:
                    currentTitle = "Account details for " + accountManager.CurrentAccount.name;

                    if (QRCodeTextures.Count == 0)
                    {
                        var platforms = accountManager.CurrentAccount.platforms.Split();
                        foreach (var platform in platforms)
                        {
                            var address = accountManager.GetAddress(accountManager.CurrentIndex, platform);
                            var tex = GenerateQR($"{platform.ToString().ToLower()}://{address}");
                            QRCodeTextures[platform] = tex;
                        }
                    }
                    break;

                case GUIState.WalletsManagement:
                    currentTitle = "Wallets Management";
                    accountManagementSelectedList.Clear();
                    break;

                case GUIState.Settings:
                    {
                        if(accountManager.Settings.nexusKind == NexusKind.Unknown)
                        {
                            currentTitle = "Wallet Setup";
                        } else if (accountManager.Settings.settingRequireReconfiguration)
                        {
                            currentTitle = "Wallet Setup (Connection failed)";
                        } else
                        {
                            currentTitle = "Settings";
                        }
                        
                        settingsScroll = Vector2.zero;
                        currencyComboBox.SelectedItemIndex = 0;
                        for (int i = 0; i < currencyOptions.Length; i++)
                        {
                            if (currencyOptions[i] == accountManager.Settings.currency)
                            {
                                currencyComboBox.SelectedItemIndex = i;
                                break;
                            }

                        }

                        nexusIndex = 0;
                        for (int i = 0; i < availableNexus.Length; i++)
                        {
                            if (availableNexus[i] == accountManager.Settings.nexusKind)
                            {
                                nexusIndex = i;
                                break;
                            }
                        }
                        nexusComboBox.SelectedItemIndex = nexusIndex;

                        mnemonicPhraseLengthIndex = 0;
                        for (int i = 0; i < availableMnemonicPhraseLengths.Length; i++)
                        {
                            if (availableMnemonicPhraseLengths[i] == accountManager.Settings.mnemonicPhraseLength)
                            {
                                mnemonicPhraseLengthIndex = i;
                                break;
                            }
                        }
                        mnemonicPhraseLengthComboBox.SelectedItemIndex = mnemonicPhraseLengthIndex;

                        mnemonicPhraseVerificationModeIndex = 0;
                        for (int i = 0; i < availableMnemonicPhraseVerificationModes.Length; i++)
                        {
                            if (availableMnemonicPhraseVerificationModes[i] == accountManager.Settings.mnemonicPhraseVerificationMode)
                            {
                                mnemonicPhraseVerificationModeIndex = i;
                                break;
                            }
                        }
                        mnemonicPhraseVerificationModeComboBox.SelectedItemIndex = mnemonicPhraseVerificationModeIndex;

                        passwordModeIndex = 0;
                        for (int i = 0; i < availablePasswordModes.Length; i++)
                        {
                            if (availablePasswordModes[i] == accountManager.Settings.passwordMode)
                            {
                                passwordModeIndex = i;
                                break;
                            }
                        }
                        passwordModeComboBox.SelectedItemIndex = passwordModeIndex;

                        logLevelIndex = 0;
                        for (int i = 0; i < availableLogLevels.Length; i++)
                        {
                            if (availableLogLevels[i] == accountManager.Settings.logLevel)
                            {
                                logLevelIndex = i;
                                break;
                            }
                        }
                        logLevelComboBox.SelectedItemIndex = logLevelIndex;

                        uiThemeIndex = 0;
                        for (int i = 0; i < availableUiThemes.Length; i++)
                        {
                            if (availableUiThemes[i].ToString() == accountManager.Settings.uiThemeName)
                            {
                                uiThemeIndex = i;
                                break;
                            }
                        }
                        uiThemeComboBox.SelectedItemIndex = uiThemeIndex;

                        

                        break;
                    }

                case GUIState.Backup:
                    currentTitle = "Backup your seed phrase!";
                    break;

                case GUIState.ScanQR:
                    currentTitle = "QR scanning";
                    cameraError = false;
                    scanTime = Time.time;
                    break;
            }
        }

        private void PopState()
        {
            if (modalRedirected)
            {
                modalRedirected = false;
            }

            var state = stateStack.Pop();
            SetState(state);
        }

        public void Animate(AnimationDirection direction, bool invert, Action callback = null)
        {
            animationTime = Time.time;
            invertAnimation = invert;
            currentAnimation = direction;
            animationCallback = callback;
        }
        #endregion

        private const int MaxResolution = 1024;

        #region CONNECTOR PROMPT
        
        private string _promptText;
        private Action<bool> _promptCallback;
        private bool _promptVisible;

        public void Prompt(string text, Action<bool> callback)
        {           
            // if theres an active prompt, this new one automatically fails
            if (_promptText != null)
            {
                callback(false);
                return;
            }

            _promptText = text;
            _promptCallback = callback;
            _promptVisible = false;
            AppFocus.Instance.StartFocus();
        }

        private void UpdatePrompt()
        {
            if (_promptText == null || _promptVisible)
            {
                return;
            }

            _promptVisible = true;

            PromptBox(_promptText, ModalYesNo, (result) =>
            {
                var temp = _promptCallback;
                _promptText = null;
                temp(result == PromptResult.Success);
            });
        }
        #endregion

        // This code is needed for Android to quit wallet on 'Back' double press.
        int escClickCounter = 0;
        IEnumerator escClickTime()
        {
            yield return new WaitForSeconds(0.5f);
            escClickCounter = 0;
        }
        private void Update()
        {
            // This allows to touch scroll on mobile devices.
            if (Input.touchCount > 0)
            {
                var touch = Input.touches[0];
                if (touch.phase == TouchPhase.Moved)
                {
                    if(hintComboBox.DropDownIsOpened())
                        hintComboBox.ListScroll.y += touch.deltaPosition.y;
                    else if((guiState == GUIState.Wallets || guiState == GUIState.WalletsManagement) && !(modalState != ModalState.None && !modalRedirected))
                        accountScroll.y += touch.deltaPosition.y;
                    else if ((guiState == GUIState.Balances || guiState == GUIState.History) && !(modalState != ModalState.None && !modalRedirected))
                        balanceScroll.y += touch.deltaPosition.y;
                    else if (guiState == GUIState.NftView && !(modalState != ModalState.None && !modalRedirected))
                        nftScroll.y += touch.deltaPosition.y;
                    else if (guiState == GUIState.NftTransferList && !(modalState != ModalState.None && !modalRedirected))
                        nftTransferListScroll.y += touch.deltaPosition.y;
                    else if (guiState == GUIState.Settings && !(modalState != ModalState.None && !modalRedirected))
                        settingsScroll.y += touch.deltaPosition.y;
                }
            }

            /*if (Input.GetKeyDown(KeyCode.Z))
            {
                AccountState state = null;
                state.address += "";
            }*/

            // This code is needed for Android to quit wallet on 'Back' double press.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                escClickCounter++;
                StartCoroutine(escClickTime());

                if (escClickCounter > 1 && Application.platform == RuntimePlatform.Android)
                {
                    Application.Quit();
                }
            }

            UpdatePrompt();

            lock (_uiCallbacks)
            {
                if (_uiCallbacks.Count > 0)
                {
                    Action[] temp;
                    lock (_uiCallbacks)
                    {
                        temp = _uiCallbacks.ToArray();
                    }
                    _uiCallbacks.Clear();

                    foreach (var callback in temp)
                    {
                        callback.Invoke();
                    }
                }
            }

            if (Screen.width > Screen.height && Screen.width > MaxResolution)
            {
                virtualWidth = MaxResolution;
                virtualHeight = (int)((MaxResolution * Screen.height) / (float)Screen.width);
            }
            else
            if (Screen.height > MaxResolution)
            {
                virtualHeight = MaxResolution;
                virtualWidth = (int)((MaxResolution * Screen.width) / (float)Screen.height);
            }
            else
            {
                virtualWidth = Screen.width;
                virtualHeight = Screen.height;
            }

            if (this.guiState == GUIState.Loading && AccountManager.Instance.Ready && !HasAnimation)
            {
                Animate(AnimationDirection.Up, true, () =>
                {
                    stateStack.Clear();
                    PushState(GUIState.Wallets);

                    if (AccountManager.Instance.Settings.nexusKind == NexusKind.Unknown || AccountManager.Instance.Settings.settingRequireReconfiguration)
                    {
                        PushState(GUIState.Settings);
                    }

                    Animate(AnimationDirection.Down, false);
                });
            }

            if (initialized && currentAnimation != AnimationDirection.None)
            {
                float animationDuration = 0.5f;
                var delta = (Time.time - animationTime) / animationDuration;

                bool finished = false;
                if (delta >= 1)
                {
                    delta = 1;
                    finished = true;
                }

                if (invertAnimation)
                {
                    delta = 1 - delta;
                }

                windowRect.x = defaultRect.x;
                windowRect.y = defaultRect.y;

                switch (currentAnimation)
                {
                    case AnimationDirection.Left:
                        windowRect.x = Mathf.Lerp(-defaultRect.width, defaultRect.x, delta);
                        break;

                    case AnimationDirection.Right:
                        windowRect.x = Mathf.Lerp(virtualWidth + defaultRect.width, defaultRect.x, delta);
                        break;

                    case AnimationDirection.Up:
                        windowRect.y = Mathf.Lerp(-defaultRect.height, defaultRect.y, delta);
                        break;

                    case AnimationDirection.Down:
                        windowRect.y = Mathf.Lerp(virtualHeight + defaultRect.height, defaultRect.y, delta);
                        break;
                }

                if (finished)
                {
                    currentAnimation = AnimationDirection.None;

                    var temp = animationCallback;
                    animationCallback = null;
                    temp?.Invoke();
                }
            }
            else
            {
                if (!initialized)
                {
                    initialized = true;
                }

                if (fullScreen)
                {
                    windowRect.width = virtualWidth;
                    windowRect.height = virtualHeight;
                }
                else
                {
                    windowRect.width = Mathf.Min(800, virtualWidth) - Border * 2;
                    windowRect.height = Mathf.Min(800, virtualHeight) - Border * 2;
                }

                windowRect.x = (virtualWidth - windowRect.width) / 2;
                windowRect.y = (virtualHeight - windowRect.height) / 2;

                defaultRect = new Rect(windowRect);
            }

            if (modalResult != PromptResult.Waiting)
            {
                var temp = modalCallback;
                var result = modalResult;
                var success = modalResult == PromptResult.Success;
                modalState = ModalState.None;
                modalCallback = null;
                modalResult = PromptResult.Waiting;

                ResetAllCombos();

                temp?.Invoke(result, success ? modalInput.Trim() : null);

                if (modalState == ModalState.None)
                {
                    modalTime = Time.time;
                }
            }
        }

        void OnGUI()
        {
            var scaleX = Screen.width / (float)virtualWidth;
            var scaleY = Screen.height / (float)virtualHeight;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scaleX, scaleY, 1.0f));

            if (AccountManager.Instance.Settings.uiFramerate > 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = AccountManager.Instance.Settings.uiFramerate;
            }
            
            var uiThemeName = AccountManager.Instance.Settings.uiThemeName;
            GUI.skin = Resources.Load($"Skins/{uiThemeName}/{uiThemeName}") as GUISkin;

            if (VerticalLayout)
                background.texture = Resources.Load<Texture2D>($"Skins/{uiThemeName}/mobile_background");
            else
                background.texture = Resources.Load<Texture2D>($"Skins/{uiThemeName}/background");
            soulMasterLogo = Resources.Load<Texture2D>($"Skins/{AccountManager.Instance.Settings.uiThemeName}/soul_master");

            GUI.enabled = true;

            if (guiState == GUIState.Loading)
            {
                if (!AccountManager.Instance.Ready)
                {
                    DrawCenteredText(AccountManager.Instance.Status);
                }
            }
            else
            {
                if (fullScreen)
                {
                    DoMainWindow(0);
                }
                else
                {
                    GUI.Window(0, windowRect, DoMainWindow, WalletTitle);
                }
            }

            if (modalState != ModalState.None && !modalRedirected)
            {
                var modalWidth = Units(44);
                var modalHeight = Units(25 + modalLineCount);

                int maxModalWidth = virtualWidth - Border * 2;
                if (modalWidth > maxModalWidth)
                {
                    modalWidth = maxModalWidth;
                }

                int maxModalHeight = virtualHeight - Border * 2;
                if (modalHeight > maxModalHeight)
                {
                    modalHeight = maxModalHeight;
                }

                modalRect = new Rect((virtualWidth - modalWidth) / 2, (virtualHeight - modalHeight) / 2, modalWidth, modalHeight);
                modalRect = GUI.ModalWindow(0, modalRect, DoModalWindow, modalTitle);
            }


            if (AccountManager.Instance.ReportGetPeersFailure)
            {
                AccountManager.Instance.ReportGetPeersFailure = false;
                MessageBox(MessageKind.Error, $"Couldn't load RPCs list.\nWallet might malfunction.");
            }
            if (AccountManager.Instance.ReportAllRpcsUnavailabe)
            {
                AccountManager.Instance.ReportAllRpcsUnavailabe = false;
                MessageBox(MessageKind.Error, "All Phantasma RPC servers are unavailable.\nPlease check your network connection.");
            }
        }

        void OnApplicationQuit()
        {
            AccountManager.Instance.Settings.SaveOnExit();
        }

        private void DoMainWindow(int windowID)
        {
            GUI.Box(new Rect(8, 8, windowRect.width - 16, Units(2)), WalletTitle);

            var style = GUI.skin.label;
            style.fontSize -= 6;
            GUI.Label(new Rect(windowRect.width / 2 + Units(5), 12, Units(4), Units(2)), Application.version);
            style.fontSize += 6;

            var accountManager = AccountManager.Instance;

            if (currentTitle != null && this.currentAnimation == AnimationDirection.None && !accountManager.BalanceRefreshing)
            {
                int curY = Units(3);

                var tempTitle = currentTitle;

                switch (guiState)
                {
                    case GUIState.Nft:
                    case GUIState.NftView:
                        if (nftTransferList.Count > 0)
                            tempTitle = $"{nftCount} ({nftTransferList.Count} selected) {tempTitle}";
                        else
                            tempTitle = $"{nftCount} {tempTitle}";
                        break;
                    case GUIState.NftTransferList:
                        tempTitle = $"{nftTransferList.Count} {tempTitle}";
                        break;
                    case GUIState.Account:
                    case GUIState.Balances:
                    case GUIState.History:
                        var state = accountManager.CurrentState;
                        if (state != null)
                        {
                            if (VerticalLayout)
                            {
                                tempTitle = $"{tempTitle} [{state.name}]";
                            }
                            else
                            {
                                tempTitle = $"{tempTitle} [{state.name} @ {accountManager.CurrentPlatform}]";
                            }
                        }
                        break;
                }

                DrawHorizontalCenteredText(curY - 4, Units(2) + (VerticalLayout ? 4 : 0), tempTitle);

                // Drawing build timestamp at the Settings screen
                if (guiState == GUIState.Settings)
                {
                    style = GUI.skin.label;
                    style.fontSize -= 6;
                    var temp = style.alignment;
                    style.alignment = TextAnchor.MiddleCenter;
                    GUI.Label(new Rect(0, curY + Units(1), windowRect.width, Units(2)), $"Version was built on: {Poltergeist.Build.Info.Instance.BuildTime} UTC");
                    style.alignment = temp;
                    style.fontSize += 6;
                }
            }

            switch (guiState)
            {
                case GUIState.Sending:
                    DrawCenteredText("Sending transaction...");
                    break;

                case GUIState.Confirming:
                    DoConfirmingScreen();
                    break;

                case GUIState.Wallets:
                    DoWalletsScreen();
                    break;

                case GUIState.WalletsManagement:
                    DoWalletsManagementScreen();
                    break;

                case GUIState.Settings:
                    DoSettingsScreen();
                    break;

                case GUIState.Balances:
                    DoBalanceScreen();
                    break;

                case GUIState.Nft:
                case GUIState.NftView:
                    DoNftScreen();
                    break;

                case GUIState.NftTransferList:
                    DoNftTransferListScreen();
                    break;

                case GUIState.History:
                    DoHistoryScreen();
                    break;

                case GUIState.Account:
                    DoAccountScreen();
                    break;

                case GUIState.ScanQR:
                    DoScanQRScreen();
                    break;

                case GUIState.Backup:
                    DoBackupScreen();
                    break;

                case GUIState.Fatal:
                    DoFatalScreen();
                    break;
            }

            //GUI.DragWindow(new Rect(0, 0, 10000, 10000));
        }

        private void DoConfirmingScreen()
        {
            var accountManager = AccountManager.Instance;

            DrawCenteredText($"Confirming transaction {transactionHash}...");

            if (transactionStillPending)
            {
                var now = DateTime.UtcNow;
                var diff = now - transactionLastCheck;
                // Checking for update every 3 seconds.
                if (diff.TotalSeconds >= 3)
                {
                    transactionLastCheck = now;
                    transactionStillPending = false;
                    transactionCheckCount++;
                    accountManager.RequestConfirmation(transactionHash.ToString(), transactionCheckCount, (txResult, msg) =>
                    {
                        if (string.IsNullOrEmpty(msg))
                        {
                            PopState();

                            if (refreshBalanceAfterConfirmation)
                            {
                                accountManager.RefreshBalances(true, PlatformKind.None, () =>
                                {
                                    InvokeTransactionCallback(transactionHash, txResult, null);
                                });
                            }
                            else
                            {
                                InvokeTransactionCallback(transactionHash, txResult, null);
                            }
                        }
                        else
                        if (msg.ToLower().Contains("pending"))
                        {
                            transactionStillPending = true;
                            transactionLastCheck = DateTime.UtcNow;
                        }
                        else
                        {
                            PopState();

                            InvokeTransactionCallback(transactionHash, txResult, msg);
                        }
                    });
                }
            }
        }

        private void LoginIntoAccount(int index, Action<bool> callback = null)
        {
            Log.Write("Login into account initiated.");

            var isNewAccount = !string.IsNullOrEmpty(newWalletSeedPhrase);

            var accountManager = AccountManager.Instance;
            accountManager.SelectAccount(index);

            RequestPassword("Open wallet", accountManager.CurrentAccount.platforms, true, true, (auth) =>
            {
                if (auth == PromptResult.Success)
                {
                    if (isNewAccount)
                    {
                        accountManager.BlankState();
                    }
                    else
                    {
                        accountManager.RefreshTokenPrices();
                    }

                    Animate(AnimationDirection.Down, true, () => {
                        PushState(GUIState.Balances);

                        Animate(AnimationDirection.Up, false, () =>
                        {
                            if (accountManager.CurrentAccount.misc != null && accountManager.CurrentAccount.misc.Contains("legacy-seed"))
                            {
                                //MessageBox(MessageKind.Default, "This account was created using legacy mnemonic phrase, please migrate to account created with Poltergeist 2.4 or newer to be compatible with future updates.", () =>
                                //{
                                    callback?.Invoke(true);
                                //});
                            }
                            else
                            {
                                callback?.Invoke(true);
                            }
                        });
                    });
                }
                else
                if (auth == PromptResult.Failure)
                {
                    var account = accountManager.Accounts[index];
                    MessageBox(MessageKind.Error, $"Could not open '{account.name}' account.", () =>
                    {
                        callback?.Invoke(false);
                    });
                }
            });
        }

        // pkIndex = -1 means single wallet created from pk or legacy seed.
        private void ImportWallet(string wif, int pkIndex, uint overallDerivationCount, string password, bool legacySeed, Action<int> callback)
        {
            var accountManager = AccountManager.Instance;

            var walletNumberString = overallDerivationCount > 1 ? $" #{pkIndex + 1}" : "";

            if (wif != null)
            {
                PhantasmaKeys keys = null;
                try
                {
                    keys = PhantasmaKeys.FromWIF(wif);
                }
                catch(Exception e)
                {
                    Log.Write("ImportWallet() exception: " + e);
                    MessageBox(MessageKind.Error, $"Incorrect WIF format.", () => { if (callback != null) { callback(-1); } });
                    return;
                }

                foreach (var account in accountManager.Accounts)
                {
                    if (account.phaAddress == keys.Address.ToString())
                    {
                        MessageBox(MessageKind.Error, $"Private key{walletNumberString} is already imported in a different account: {account.name}.", () => { if (callback != null) { callback(-1); } });
                        return;
                    }
                }
            }

            ShowModal("Wallet Name", $"Enter a name for your wallet{walletNumberString}", ModalState.Input, AccountManager.MinAccountNameLength, AccountManager.MaxAccountNameLength, ModalConfirmCancel, 1, (result, name) =>
            {
                if (result == PromptResult.Success)
                {
                    var nameAlreadyTaken = false;
                    for (int i = 0; i < accountManager.Accounts.Count(); i++)
                    {
                        if (accountManager.Accounts[i].name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            nameAlreadyTaken = true;
                        }
                    }

                    if (nameAlreadyTaken)
                    {
                        MessageBox(MessageKind.Error, "An account with this name already exists.", () => { ImportWallet(wif, pkIndex, overallDerivationCount, password, legacySeed, callback); });
                    }
                    else
                    {
                        if (password == null)
                        {
                            PromptBox($"Do you want to add a password to wallet{walletNumberString}?\nThe password will be required to open the wallet.\nIt will also be prompted every time you do a transaction", ModalYesNo, (wantsPass) =>
                            {
                                if (wantsPass == PromptResult.Success)
                                {
                                    TrySettingWalletPassword(name, wif, legacySeed, callback);
                                }
                                else
                                {
                                    FinishCreateAccount(name, wif, "", legacySeed, callback);
                                }
                            });
                        }
                        else
                        {
                            FinishCreateAccount(name, wif, password, legacySeed, callback);
                        }
                    }
                }
            });
        }

        private string[] commonPasswords = new string[]
        {
            "password", "123456", "1234567", "12345678", "baseball", "football","letmein","monkey","696969",
            "abc123","mustang","michael","shadow","master","jennifer","111111","jordan","superman","fuckme","hunter",
            "fuckyou", "trustno1", "ranger","buster","thomas","robert","bitcoin","phantasma","wallet","crypto"
        };

        

        private bool IsGoodPassword(string name, string password)
        {
            if (password == null || password.Length < AccountManager.MinPasswordLength)
            {
                return false;
            }

            // Password cannot contain account name.
            if (password.ToLowerInvariant().Contains(name.ToLowerInvariant()))
            {
                return false;
            }

            foreach (var common in commonPasswords)
            {
                // Password shouldn't be listed in a bad passwords list.
                if (password.Equals(common, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private void TrySettingWalletPassword(string name, string wif, bool legacySeed, Action<int> callback)
        {
            ShowModal("Wallet Password", "Enter a password for your wallet", ModalState.Password, AccountManager.MinPasswordLength, AccountManager.MaxPasswordLength, ModalConfirmCancel, 1, (passResult, password) =>
            {
                if (passResult == PromptResult.Success)
                {
                    if (IsGoodPassword(name, password))
                    {
                        FinishCreateAccount(name, wif, password, legacySeed, callback);
                    }
                    else
                    {
                        MessageBox(MessageKind.Error, $"That password is either too short or too weak.\nNeeds at least {AccountManager.MinPasswordLength} characters and can't be easy to guess.", () =>
                        {
                            TrySettingWalletPassword(name, wif, legacySeed, callback);
                        });
                    }
                }
                else
                {
                    FinishCreateAccount(name, wif, "", legacySeed, callback);
                }
            });
        }

        private void DeriveAccountFromSeed(string mnemonicPhrase, uint derivationIndex, uint overallDerivationCount)
        {
            ImportWallet(BIP39NBitcoin.MnemonicToWif(mnemonicPhrase, derivationIndex), (int)derivationIndex, overallDerivationCount, null, false, (walletIndex) =>
            {
                if (derivationIndex == overallDerivationCount - 1)
                {
                    if (derivationIndex == 0 && walletIndex >= 0)
                    {
                        // We login into account if only 1 account is created.
                        LoginIntoAccount(walletIndex);
                    }
                }
                else
                {
                    DeriveAccountFromSeed(mnemonicPhrase, derivationIndex + 1, overallDerivationCount);
                }
            });
        }
        private void DeriveAccountsFromSeed(string mnemonicPhrase)
        {
            ShowModal("Number of created wallets", "Enter number of wallets to derive from this seed phrase.\n\nUse \"1\" if unsure.", ModalState.Input, 1, -1, ModalConfirmCancel, 1, (success, input) =>
            {
                if (success == PromptResult.Success)
                {
                    if (UInt32.TryParse(input, out var numberOfWallets))
                    {
                        DeriveAccountFromSeed(mnemonicPhrase, 0, numberOfWallets);
                    }
                    else
                    {
                        MessageBox(MessageKind.Error, "Incorrect number", () => { DeriveAccountsFromSeed(mnemonicPhrase); });
                    }
                }
            }, 0, "1");
        }

        private void FinishCreateAccount(string name, string wif, string password, bool legacySeed, Action<int> callback)
        {
            try
            {
                var accountManager = AccountManager.Instance;

                int walletIndex = accountManager.AddWallet(name, wif, password, legacySeed);
                accountManager.SaveAccounts();
                if (callback == null)
                {
                    LoginIntoAccount(walletIndex);
                }
                else
                {
                    callback(walletIndex);
                }
            }
            catch (Exception e)
            {
                newWalletSeedPhrase = null; // seedPhrase is used to determine value of isNewWallet global flag, and should be reset in case of error.
                newWalletCallback = null;
                MessageBox(MessageKind.Error, "Error creating account.\n" + e.Message);
            }
        }

        private string[] accountOptions = new string[] { "Generate new wallet", "Import wallet", "Manage", "Settings" };

        private string[] walletsManagementOptions = new string[] { "Export", "Import", "Delete", "Cancel", "Save and Close" };

        private Vector2 accountScroll;
        private Vector2 balanceScroll;
        private Vector2 nftScroll;
        private Vector2 nftTransferListScroll;
        private Vector2 settingsScroll;

        private void DoWalletsScreen()
        {
            var accountManager = AccountManager.Instance;

            // This is a strange fix i don't fully understand.
            // On an old slow Mac there was an exception
            // that indicated that accounts list were modified
            // at the same time as DoWalletsScreen() was displaying accounts list.
            // It shouldn't be possible because Start() is called and should be finished
            // before OnGUI() call (at least that's what i read in Unity documentation).
            // But this fix helped and PG stopped crashing on that old Mac.
            if (!accountManager.AccountsAreReadyToBeUsed)
            {
                return;
            }

            // This fix is related to previous one.
            // If some account is added or edited, we can sometimes get "Collection was modified; enumeration operation may not execute." exception.
            // Duplicating accounts list to avoid that.
            List<Account> accountsCopy;
            try
            {
                accountsCopy = accountManager.Accounts.ToList();
            }
            catch
            {
                return;
            }

            int endY;
            DoButtonGrid<int>(true, accountOptions.Length, Units(2), 0, out endY, (index) =>
            {
                return new MenuEntry(index, accountOptions[index], true);
            },
            (selected) =>
            {
                switch (selected)
                {
                    case 0:
                        {
                            newWalletSeedPhrase = BIP39NBitcoin.GenerateMnemonic(AccountManager.Instance.Settings.mnemonicPhraseLength);

                            Animate(AnimationDirection.Down, true, () =>
                            {
                                newWalletCallback = new Action(() =>
                                {
                                    DeriveAccountsFromSeed(newWalletSeedPhrase);

                                    PopState();
                                });

                                PushState(GUIState.Backup);
                            });
                            
                            break;
                        }

                    case 1:
                        {
                            ShowModal("Wallet Import", "Supported inputs:\n12/24 word seed phrase\nPrivate key (HEX format)\nPrivate key (WIF format)", ModalState.Input, 32, 1024, ModalConfirmCancel, 4, (result, key) =>
                            {
                                if (result == PromptResult.Success)
                                {
                                    if (PhantasmaAPI.IsValidPrivateKey(key))
                                    {
                                        PromptBox("Was this private key created using a Poltergeist version earlier than v2.4 (before end of April 2021)?", ModalYesNo, (legacySeed) =>
                                        {
                                            ImportWallet(key, -1, 1, null, legacySeed == PromptResult.Success, null);
                                        });
                                    }
                                    else
                                    if (key.Length == 64 || (key.Length == 66 && key.ToUpper().StartsWith("0X")))
                                    {
                                        var priv = Base16.Decode(key);
                                        var tempKey = new PhantasmaKeys(priv);
                                        PromptBox("Was this WIF created using a Poltergeist version earlier than v2.4 (before end of April 2021)?", ModalYesNo, (legacySeed) =>
                                        {
                                            ImportWallet(tempKey.ToWIF(), -1, 1, null, legacySeed == PromptResult.Success, null);
                                        });
                                    }
                                    else
                                    if (key.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length == 12)
                                    {
                                        ImportSeedPhrase(key);
                                    }
                                    else
                                    if (key.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length == 24)
                                    {
                                        ImportSeedPhrase(key);
                                    }
                                    else
                                    {
                                        MessageBox(MessageKind.Error, "Seed phrase or private key that you entered is incorrect." +
"\nPlease check your spelling carefully, and try again." +
"\n" +
"\nEnsure that:" +
"\n* If copy / pasting - That you've selected the entire set of characters." +
"\n* If copy / pasting - That the characters have been copied into your clipboard correctly." +
"\n* If typing it -Take care to check that you're using English keyboard layout and the correct case for each letter.");
                                    }
                                }
                            });
                            break;
                        }

                    case 2:
                        {
                            Animate(AnimationDirection.Up, true, () =>
                            {
                                PushState(GUIState.WalletsManagement);
                                Animate(AnimationDirection.Down, false);
                            });
                            break;
                        }

                    case 3:
                        {
                            Animate(AnimationDirection.Up, true, () =>
                            {
                                PushState(GUIState.Settings);
                                Animate(AnimationDirection.Down, false);
                            });
                            break;
                        }

                    case 4:
                        {
                            Animate(AnimationDirection.Up, true, () =>
                            {
                                PushState(GUIState.Settings);
                                Animate(AnimationDirection.Down, false);
                            });
                            break;
                        }
                }
            });

            int startY = (int)(windowRect.y + Units(5));

            int panelHeight = Units(6);

            DoScrollArea<Account>(ref accountScroll, startY, endY, panelHeight, accountsCopy,
                (account, index, curY, rect) =>
                {
                    int btnWidth = Units(7);

                    Rect btnRect;

                    if (VerticalLayout)
                    {
                        GUI.Label(new Rect(Border * 2, curY , windowRect.width - Border * 2, Units(2) + 4), account.ToString());
                        btnRect = new Rect((rect.width - btnWidth)/2, curY + Units(3) + 4, btnWidth, Units(2));
                    }
                    else
                    {
                        GUI.Label(new Rect(Border * 2, curY + Units(1), windowRect.width - Border * 2, Units(2) + 4), account.ToString());
                        btnRect = new Rect(rect.width - (btnWidth + Units(2) + 4), curY + Units(2) - 4, btnWidth, Units(2));
                    }

                    DoButton(true, btnRect, "Open", () =>
                    {
                        LoginIntoAccount(index);
                    });
                });
        }

        private void DoWalletsManagementScreen()
        {
            var accountManager = AccountManager.Instance;

            int endY;
            DoButtonGrid<int>(true, walletsManagementOptions.Length, Units(2), 0, out endY, (index) =>
            {
                var enabled = true;
                if (index == 2 && accountManagementSelectedList.Count() == 0) // We disable Delete button if nothing is selected.
                    enabled = false;
                return new MenuEntry(index, walletsManagementOptions[index], enabled);
            },
            (selected) =>
            {
                switch (selected)
                {
                    case 0:
                        {
                            ShowModal("Wallets Export", 
                                ((accountManagementSelectedList.Count() == 0) ? $"All {accountManager.Accounts.Count()} wallets will be exported.\n\n" : $"Selected {accountManagementSelectedList.Count()} wallets will be exported.\n\n") +
                                "Do you want to protect exported data with a password?\nIf not, leave this field blank.", ModalState.Password, AccountManager.MinPasswordLength, AccountManager.MaxPasswordLength, ModalConfirmCancel, 1, (passResult, password) =>
                            {
                                var accountsExport = new AccountsExport();

                                accountsExport.walletIdentifier = accountManager.WalletIdentifier;
                                accountsExport.accountsVersion = PlayerPrefs.GetInt(AccountManager.WalletVersionTag, 1);

                                List<Account> accountsToExport;
                                if (accountManagementSelectedList.Count() > 0)
                                    accountsToExport = accountManager.Accounts.Where(x => accountManagementSelectedList.Contains(x.phaAddress)).ToList();
                                else
                                    accountsToExport = accountManager.Accounts;

                                if (passResult == PromptResult.Success)
                                {
                                    if (!String.IsNullOrEmpty(password))
                                    {
                                        accountsExport.passwordProtected = true;
                                        accountsExport.passwordIterations = AccountManager.PasswordIterations;

                                        var bytes = Serialization.Serialize(accountsToExport.ToArray());

                                        // Getting password hash.
                                        AccountManager.GetPasswordHash(password, accountsExport.passwordIterations, out accountsExport.salt, out string passwordHash);

                                        // Encrypting accounts.
                                        accountsExport.accounts = AccountManager.EncryptString(Convert.ToBase64String(bytes), passwordHash, out string iv);
                                        accountsExport.iv = iv;

                                        // Decrypting to ensure there are no exceptions.
                                        AccountManager.DecryptString(accountsExport.accounts, passwordHash, accountsExport.iv);
                                    }
                                    else
                                    {
                                        accountsExport.passwordProtected = false;
                                        var bytes = Serialization.Serialize(accountsToExport.ToArray());
                                        accountsExport.accounts = Convert.ToBase64String(bytes);
                                    }

                                    var serializedExportData = Convert.ToBase64String(Serialization.Serialize(accountsExport));

                                    ShowModal("Wallets Export", $"Copy wallets export data to the clipboard?",
                                        ModalState.Message, 0, 0, ModalConfirmCancel, 0, (result, input) =>
                                        {
                                            if (result == PromptResult.Success)
                                            {
                                                GUIUtility.systemCopyBuffer = serializedExportData;
                                                MessageBox(MessageKind.Default, "Wallets export data copied to the clipboard.");
                                            }
                                        });
                                }
                            });
                            break;
                        }

                    case 1:
                        {
                            ShowModal("Wallets Import", "Please enter wallets data that you received from Wallets Export dialog (on Wallets Management screen):", ModalState.Input, 1, -1, ModalConfirmCancel, 4, (result, walletsData) =>
                            {
                                if (result == PromptResult.Success)
                                {
                                    try
                                    {
                                        var accountsExport = Serialization.Unserialize<AccountsExport>(Convert.FromBase64String(walletsData));

                                        Log.Write($"Importing wallets. Source wallet identifier: {accountsExport.walletIdentifier}, accounts version: {accountsExport.accountsVersion}");

                                        var import = new Action<AccountsExport>((data) =>
                                        {
                                            var accounts = Serialization.Unserialize<Account[]>(Convert.FromBase64String(accountsExport.accounts)).ToList();
                                            var messageWillBeImported = "Following accounts will be imported:\n\n";
                                            var someWillBeImported = false;
                                            var messageWillBeSkipped = "Following accounts already exist and will be skipped:\n\n";
                                            var someWillBeSkipped = false;

                                            var accountsToImport = new List<Account>();
                                            foreach (var account in accounts)
                                            {
                                                if(accountManager.Accounts.Where(x => x.phaAddress.ToUpper() == account.phaAddress.ToUpper()).Any())
                                                {
                                                    messageWillBeSkipped += $"- {account.name} [{account.phaAddress}]\n";
                                                    someWillBeSkipped = true;
                                                }
                                                else
                                                {
                                                    messageWillBeImported += $"+ {account.name} [{account.phaAddress}]\n";
                                                    someWillBeImported = true;

                                                    accountsToImport.Add(account);
                                                }
                                            }

                                            if (accountsExport.accountsVersion == 2)
                                            {
                                                // Legacy seeds, we should mark accounts.
                                                for (var i = 0; i < accountsToImport.Count; i++)
                                                {
                                                    var account = accountsToImport[i];
                                                    account.misc = "legacy-seed";
                                                    accountsToImport[i] = account;
                                                }
                                            }

                                            ShowModal("Wallets Import",
                                                (someWillBeImported ? (messageWillBeImported + "\n\n") : "") + (someWillBeSkipped ? messageWillBeSkipped : ""),
                                                ModalState.Message, 0, 0, ModalConfirmCancel, 0, (result2, input) =>
                                                {
                                                    if (result2 == PromptResult.Success)
                                                    {
                                                        var count = 0;
                                                        foreach (var accountToImport in accountsToImport)
                                                        {
                                                            accountManager.Accounts.Add(accountToImport);
                                                            count++;
                                                        }
                                                        MessageBox(MessageKind.Default, $"{count} wallets successfully imported.");
                                                    }
                                                });
                                        });

                                        if (accountsExport.passwordProtected)
                                        {
                                            ShowModal("Wallets Import",
                                                "Please enter password:", ModalState.Password, AccountManager.MinPasswordLength, AccountManager.MaxPasswordLength, ModalConfirmCancel, 1, (passResult, password) =>
                                                {
                                                    if (passResult == PromptResult.Success && !String.IsNullOrEmpty(password))
                                                    {
                                                        try
                                                        {
                                                            // Getting password hash.
                                                            AccountManager.GetPasswordHashBySalt(password, accountsExport.passwordIterations, accountsExport.salt, out string passwordHash);

                                                            // Decrypting accounts.
                                                            accountsExport.accounts = AccountManager.DecryptString(accountsExport.accounts, passwordHash, accountsExport.iv);

                                                            import(accountsExport);
                                                        }
                                                        catch (Exception e)
                                                        {
                                                            Log.WriteWarning("Cannot decrypt wallets data: " + e.ToString());
                                                            MessageBox(MessageKind.Error, $"Cannot decrypt wallets data.");
                                                        }
                                                    }
                                                });
                                        }
                                        else
                                        {
                                            import(accountsExport);
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        Log.WriteWarning("Cannot open wallets data: " + e.ToString());
                                        MessageBox(MessageKind.Error, $"Cannot open wallets data.");
                                    }
                                }
                            });
                            break;
                        }

                    case 2:
                        {
                            PromptBox($"{accountManagementSelectedList.Count()} selected wallets will be deleted.\nMake sure you have backups of your private keys!\nOtherwise you will lose access to your funds.", ModalConfirmCancel, (result) =>
                            {
                                if (result == PromptResult.Success)
                                {
                                    var counter = 0;
                                    foreach(var accountToDelete in accountManagementSelectedList)
                                    {
                                        accountManager.Accounts.Remove(accountManager.Accounts.Where(x => x.phaAddress.ToUpper() == accountToDelete.ToUpper()).First());
                                        counter++;
                                    }

                                    accountManagementSelectedList.Clear();

                                    MessageBox(MessageKind.Default, $"{counter} wallets removed from this device.");
                                }
                            }, 10);
                            break;
                        }

                    case 3:
                        {
                            CloseCurrentStack();
                            return;
                        }

                    case 4:
                        {
                            accountManager.SaveAccounts();
                            CloseCurrentStack();
                            return;
                        }
                }
            });

            int startY = (int)(windowRect.y + Units(5));

            int panelHeight = VerticalLayout ? Units(7) : Units(4);

            // We should create copy since main list will be modified.
            var accountsListCopy = new List<Account>();
            accountManager.Accounts.ForEach(x => accountsListCopy.Add(x));

            DoScrollArea<Account>(ref accountScroll, startY, endY, panelHeight, accountsListCopy,
                (account, index, curY, rect) =>
                {
                    int btnWidth = Units(6);

                    Rect btnRect;
                    Rect btnRect2;
                    Rect btnRect3;
                    Rect btnRectToggle;

                    if (VerticalLayout)
                    {
                        GUI.Label(new Rect(Border * 2, curY, windowRect.width - Border * 2, Units(2) + 4), account.name);
                        var style = GUI.skin.label;
                        style.fontSize -= 4;
                        GUI.Label(new Rect(Border * 2, curY + Units(1) + 8, windowRect.width - Border * 2, Units(2) + 4), $"{account.phaAddress}");
                        style.fontSize += 4;

                        btnRect = new Rect(rect.width - (btnWidth + Units(2)), curY + Units(4), btnWidth, Units(2));
                        btnRect2 = new Rect(rect.width - (btnWidth + Units(1)) * 2 - Units(1), curY + Units(4), btnWidth, Units(2));
                        btnRect3 = new Rect(rect.width - (btnWidth + Units(1)) * 3 - Units(1), curY + Units(4), btnWidth, Units(2));

                        btnRectToggle = new Rect(rect.width - (btnWidth + Units(1) + 4) * 3 - Units(2), curY + Units(4) + 4, Units(1), Units(1));
                    }
                    else
                    {
                        GUI.Label(new Rect(Border * 2, curY, windowRect.width - Border * 2, Units(2) + 4), account.ToString());
                        var style = GUI.skin.label;
                        style.fontSize -= 4;
                        GUI.Label(new Rect(Border * 2, curY + Units(1) + 8, windowRect.width - Border * 2, Units(2) + 4), $"{account.phaAddress}");
                        style.fontSize += 4;
                        
                        btnRect = new Rect(rect.width - (btnWidth + Units(2)), curY + Units(1), btnWidth, Units(2));
                        btnRect2 = new Rect(rect.width - (btnWidth + Units(1)) * 2 - Units(1), curY + Units(1), btnWidth, Units(2));
                        btnRect3 = new Rect(rect.width - (btnWidth + Units(1)) * 3 - Units(1), curY + Units(1), btnWidth, Units(2));

                        btnRectToggle = new Rect(rect.width - (btnWidth + Units(1) + 4) * 3 - Units(2), curY + Units(1) + 4, Units(1), Units(1));
                    }

                    var accountIsSelected = accountManagementSelectedList.Exists(x => x == account.phaAddress);
                    if (GUI.Toggle(btnRectToggle, accountIsSelected, ""))
                    {
                        if (!accountIsSelected)
                        {
                            accountManagementSelectedList.Add(account.phaAddress);
                        }
                    }
                    else
                    {
                        if (accountIsSelected)
                        {
                            accountManagementSelectedList.Remove(accountManagementSelectedList.Single(x => x == account.phaAddress));
                        }
                    }
                    
                    DoButton(index != 0, btnRect3, "Move up", () =>
                    {
                        var accountToMoveUp = accountManager.Accounts.ElementAt(index);
                        accountManager.Accounts.RemoveAt(index);
                        accountManager.Accounts.Insert(index - 1, accountToMoveUp);
                    });

                    DoButton(index < accountManager.Accounts.Count() - 1, btnRect2, "Move down", () =>
                    {
                        var accountToMoveDown = accountManager.Accounts.ElementAt(index);
                        accountManager.Accounts.RemoveAt(index);
                        accountManager.Accounts.Insert(index + 1, accountToMoveDown);
                    });

                    DoButton(true, btnRect, "Rename", () =>
                    {
                        ShowModal("Rename", $"Current local name: {account.name}\nAddress: {account.phaAddress}\n\nEnter new local account name:", ModalState.Input, AccountManager.MinAccountNameLength, AccountManager.MaxAccountNameLength, ModalConfirmCancel, 1, (result, input) =>
                        {
                            if (input == null || input.Length < AccountManager.MinAccountNameLength ||
                                input.Length > AccountManager.MaxAccountNameLength)
                            {
                                MessageBox(MessageKind.Error, "Invalid account name.\n");
                                return;
                            }
                            
                            if ( accountManager.Accounts.Any(x => x.name.ToLower() == input.ToLower()))
                            {
                                MessageBox(MessageKind.Error, "Account with this name already exists.\n");
                                return;
                            }
                            
                            if (result == PromptResult.Success)
                            {
                                account.name = input;
                                accountManager.Accounts[index] = account;
                            }
                        });
                    });
                });
        }

        private void ImportSeedPhrase(string mnemonicPhrase)
        {
            try
            {

                DeriveAccountsFromSeed(mnemonicPhrase);
            }
            catch (Exception e)
            {
                MessageBox(MessageKind.Error, "Could not import wallet.\n" + e.Message);
            }
        }

        private void CloseCurrentStack()
        {
            Animate(AnimationDirection.Down, true, () =>
            {
                var accountManager = AccountManager.Instance;
                accountManager.UnselectAcount();
                stateStack.Clear();
                PushState(GUIState.Wallets);

                Animate(AnimationDirection.Up, false);
            });
        }

        private int DrawPlatformTopMenu(Action refresh, bool showCopyToClipboardButton = true)
        {
            var accountManager = AccountManager.Instance;

            int curY = VerticalLayout ? Units(6) : Units(1);

            string address = accountManager.CurrentAccount.phaAddress;

            var btnWidth = Units(8);

            if (refresh != null)
            {
                DoButton(true, new Rect(windowRect.width - (VerticalLayout ? windowRect.width / 2 + btnWidth / 2 : btnWidth + Border * 2), curY, btnWidth, Units(1) + (VerticalLayout ? 8 : 0)), "Refresh", () =>
                {
                    refresh();
                });
            }

            curY += Units(VerticalLayout ? 2 : 3);
            DrawHorizontalCenteredText(curY - 5, Units(VerticalLayout ? 3: 2), address);

            curY += Units(3);

            if (showCopyToClipboardButton)
            {
                DoButton(true, new Rect(windowRect.width / 2 - btnWidth - Border, curY, btnWidth, Units(1) + (VerticalLayout ? 8 : 0)), "Copy Address", () =>
                  {
                      GUIUtility.systemCopyBuffer = address;
                      MessageBox(MessageKind.Default, "Address copied to clipboard.");
                  });

                DoButton(true, new Rect(windowRect.width / 2 + Border, curY, btnWidth, Units(1) + (VerticalLayout ? 8 : 0)), "Explorer", () =>
                {
                    switch(accountManager.CurrentPlatform)
                    {
                        case PlatformKind.Phantasma:
                            Application.OpenURL(accountManager.GetPhantasmaAddressURL(address));
                            break;
                    }
                });

                curY += Units(3);
            }

            return curY;
        }

        // NFT tools for toolbar over NFT list - sort/filters combos, select/invert buttons etc.
        private void DrawNftTools(int posY)
        {
            var accountManager = AccountManager.Instance;

            var posX1 = Units(2);
            var posX2 = posX1 + toolLabelWidth + toolFieldWidth + toolFieldSpacing;
            // 2nd row of widgets for VerticalLayout
            var posX3 = (VerticalLayout) ? Units(2) : posX2 + toolLabelWidth + toolFieldWidth + toolFieldSpacing;
            var posX4 = posX3 + toolLabelWidth + toolFieldWidth + toolFieldSpacing;
            var posY2 = (VerticalLayout) ? posY + Units(2) : posY;
            var posY3 = (VerticalLayout) ? posY2 + Units(2) : posY + Units(2);

            // #5: Sorting mode combo
            if (transferSymbol == "TTRS")
            {
                DoNftToolComboBox(posX1, posY3, nftSortModeComboBox, Enum.GetValues(typeof(TtrsNftSortMode)).Cast<TtrsNftSortMode>().ToList().Select(x => x.ToString().Replace("_", ", ").Replace("Number", "#")).ToList(), "Sort: ", ref accountManager.Settings.ttrsNftSortMode);
            }
            else
            {
                DoNftToolComboBox(posX1, posY3, nftSortModeComboBox, Enum.GetValues(typeof(NftSortMode)).Cast<NftSortMode>().ToList().Select(x => x.ToString().Replace("_", ", ").Replace("Number", "#")).ToList(), "Sort: ", ref accountManager.Settings.nftSortMode);
            }

            // #6: Sorting direction button
            DoNftToolButton(posX2 + 4,
                            posY3,
                            (VerticalLayout) ? toolLabelWidth - toolFieldSpacing - 8 : toolLabelWidth - toolFieldSpacing, (accountManager.Settings.nftSortDirection == (int)SortDirection.Ascending) ? "Asc" : "Desc", () => { if (accountManager.Settings.nftSortDirection == (int)SortDirection.Ascending) accountManager.Settings.nftSortDirection = (int)SortDirection.Descending; else accountManager.Settings.nftSortDirection = (int)SortDirection.Ascending; });

            if (guiState != GUIState.NftView)
            {
                // #7: Select all button
                DoNftToolButton(posX4 + toolLabelWidth,
                                posY3,
                                (VerticalLayout) ? toolLabelWidth - toolFieldSpacing - 8 : toolLabelWidth - toolFieldSpacing, "Select", () =>
                                {
                                    if (nftFilteredList.Count > 0)
                                    {
                                        // If filter is applied, select button selects only filtered items.
                                        nftFilteredList.ForEach((x) => { if (!nftTransferList.Contains(x.ID)) nftTransferList.Add(x.ID); });
                                    }
                                    else
                                    {
                                        // If no filter is applied, select button selects all items.
                                        nftTransferList.Clear();
                                        accountManager.CurrentNfts.ForEach((x) => { nftTransferList.Add(x.ID); });
                                    }
                                });

                // #8: Invert selection button
                DoNftToolButton((VerticalLayout) ? posX4 + toolLabelWidth * 2 - toolFieldSpacing + 8 : posX4 + toolLabelWidth * 2 + toolFieldSpacing,
                                posY3,
                                (VerticalLayout) ? toolLabelWidth - toolFieldSpacing - 8 : toolLabelWidth - toolFieldSpacing, "Invert", () =>
                                {
                                    if (nftFilteredList.Count > 0)
                                    {
                                        // If filter is applied, invert button processes only filtered items.
                                        nftFilteredList.ForEach((x) => { if (!nftTransferList.Contains(x.ID)) nftTransferList.Add(x.ID); else nftTransferList.Remove(x.ID); });
                                    }
                                    else
                                    {
                                        // If no filter is applied, invert button processes all items.
                                        var nftTransferListCopy = new List<string>();
                                        accountManager.CurrentNfts.ForEach((x) => { if (!nftTransferList.Exists(y => y == x.ID)) { nftTransferListCopy.Add(x.ID); } });
                                        nftTransferList = nftTransferListCopy;
                                    }
                                });
            }

            if (transferSymbol == "TTRS")
            {
                // #3: NFT rarity filter
                DoNftToolComboBox(posX3, posY2, nftRarityComboBox, Enum.GetValues(typeof(ttrsNftRarity)).Cast<ttrsNftRarity>().ToList(), "Rarity: ", ref nftFilterRarity);
            }

            // #4: NFT mint date filter
            DoNftToolComboBox(posX4, posY2, nftMintedComboBox, Enum.GetValues(typeof(nftMinted)).Cast<nftMinted>().ToList().Select(x => x.ToString().Replace('_', ' ')).ToList(), "Minted: ", ref nftFilterMinted);

            // #1: NFT name filter
            DoNftToolTextField(posX1, posY, "Name: ", ref nftFilterName);

            if (transferSymbol == "TTRS")
            {
                // #2: NFT type filter
                DoNftToolComboBox(posX2, posY, nftTypeComboBox, Enum.GetValues(typeof(ttrsNftType)).Cast<ttrsNftType>().ToList(), "Type: ", ref nftFilterTypeIndex);
                if (Enum.IsDefined(typeof(ttrsNftType), nftFilterTypeIndex))
                    nftFilterType = ((ttrsNftType)nftFilterTypeIndex).ToString();
                else
                    nftFilterType = "All";
            }
        }

        private bool DrawNftToolsAreActive()
        {
            return nftTypeComboBox.DropDownIsOpened() || nftMintedComboBox.DropDownIsOpened() || nftRarityComboBox.DropDownIsOpened();
        }

        private void DrawBalanceLine(ref Rect subRect, string symbol, decimal amount, string caption)
        {
            if (amount > 0.0001m)
            {
                var style = GUI.skin.label;
                style.fontSize -= VerticalLayout ? 4: 2;

                var value = AccountManager.Instance.GetTokenWorth(symbol, amount);
                GUI.Label(subRect, $"{MoneyFormat(amount)} {symbol} {caption}" + (value == null ? "" : $" ({value})"));
                style.fontSize += VerticalLayout ? 4 : 2;

                // For vertical layout making a height correction proportional to font size difference.
                subRect.y += VerticalLayout ? (int)(Units(1) * (double)16 / 18) + 4 : Units(1) + 4;
            }
        }

        private WebCamTexture camTexture;
        private bool cameraError;
        private float scanTime;

        private void DoScanQRScreen()
        {
            var accountManager = AccountManager.Instance;

            if (cameraError)
            {
                DrawCenteredText("Failed to initialize camera...");
                DoBackButton();
                return;
            }

            if (WebCamTexture.devices.Count() == 0)
            {
                DrawCenteredText("Camera not found...");
                DoBackButton();
                return;
            }

            if (camTexture == null)
            {
                camTexture = new WebCamTexture();
                camTexture.requestedWidth = virtualWidth / 2;
                camTexture.requestedHeight = virtualHeight / 2;

                if (camTexture != null)
                {
                    camTexture.Play();
                }
                else
                {
                    cameraError = true;
                }
            }

            var camHeight = windowRect.height - Units(12);
            var camWidth  = (int)((camTexture.width * camHeight) / (float)camTexture.height);

            var camRect = new Rect((windowRect.width - camWidth)/2, Border + Units(5), camWidth, camHeight);
            DrawDropshadow(camRect);
            GUI.DrawTexture(camRect, camTexture, ScaleMode.ScaleToFit);

            var diff = Time.time - scanTime;
            if (diff >= 1 && camTexture != null && camTexture.isPlaying)
            {
                scanTime = Time.time;

                try
                {
                    IBarcodeReader barcodeReader = new BarcodeReader();
                    // decode the current frame
                    var result = barcodeReader.Decode(camTexture.GetPixels32(),
                      camTexture.width, camTexture.height);

                    if (result != null)
                    {
                        Log.Write("DECODED TEXT FROM QR: " + result.Text);

                        foreach (var platform in AccountManager.AvailablePlatforms)
                        {
                            var tag = platform.ToString().ToLower()+"://";
                            if (result.Text.StartsWith(tag))
                            {
                                modalInput = result.Text.Substring(tag.Length);
                                PopState();
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.WriteWarning(ex.Message); }
            }

            DoBackButton();
        }

        private static int GetNextInt32(System.Security.Cryptography.RNGCryptoServiceProvider rnd)
        {
            byte[] randomInt = new byte[4];
            rnd.GetBytes(randomInt);
            return Convert.ToInt32(randomInt[0]);
        }
        private void TrySeedVerification(int[] wordsOrder, Action<bool> callback)
        {
            if (AccountManager.Instance.Settings.mnemonicPhraseVerificationMode == MnemonicPhraseVerificationMode.Full)
            {
                ShowModal("Seed verification", $"To confirm that you have backed up your seed phrase, enter your seed words in the following order: {string.Join(" ", wordsOrder)}",
                    ModalState.Input, 24 + 11, -1, ModalConfirmCancel, 4, (result, input) =>
                {
                    if (result == PromptResult.Success)
                    {
                        try
                        {
                            var wordsToVerify = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            var wordsToVerifyOrdered = new string[wordsToVerify.Length];
                            for (var i = 0; i < wordsOrder.Length; i++)
                            {
                                wordsToVerifyOrdered[wordsOrder[i] - 1] = wordsToVerify[i];
                            }

                            if (BIP39NBitcoin.MnemonicToPK(string.Join(" ", wordsToVerifyOrdered)).SequenceEqual(BIP39NBitcoin.MnemonicToPK(newWalletSeedPhrase)))
                            {
                                callback(true);
                            }
                            else
                            {
                                MessageBox(MessageKind.Error, "Seed phrase is incorrect!", () =>
                                {
                                    TrySeedVerification(wordsOrder, callback);
                                });
                            }
                        }
                        catch (Exception e)
                        {
                            Log.WriteWarning("TrySeedVerification: Exception: " + e);
                            MessageBox(MessageKind.Error, "Seed phrase is incorrect!", () =>
                            {
                                TrySeedVerification(wordsOrder, callback);
                            });
                        }
                    }
                });
            }
            else
            {
                ShowModal("Seed verification", $"To confirm that you have backed up your seed phrase, enter your seed words:",
                    ModalState.Input, 24 + 11, -1, ModalConfirmCancel, 4, (result, input) =>
                {
                    if (result == PromptResult.Success)
                    {
                        try
                        {
                            var wordsToVerify = input.Split(' ');

                            if (BIP39NBitcoin.MnemonicToPK(string.Join(" ", wordsToVerify)).SequenceEqual(BIP39NBitcoin.MnemonicToPK(newWalletSeedPhrase)))
                            {
                                callback(true);
                            }
                            else
                            {
                                MessageBox(MessageKind.Error, "Seed phrase is incorrect!", () =>
                                {
                                    TrySeedVerification(wordsOrder, callback);
                                });
                            }
                        }
                        catch (Exception e)
                        {
                            Log.WriteWarning("TrySeedVerification: Exception: " + e);
                            MessageBox(MessageKind.Error, "Seed phrase is incorrect!", () =>
                            {
                                TrySeedVerification(wordsOrder, callback);
                            });
                        }
                    }
                });
            }
        }

        private string[] backupScreenOptions = new string[] { "Copy to clipboard", "Continue", "Cancel" };
        private void DoBackupScreen()
        {
            int curY;

            curY = Units(5);
            GUI.Label(new Rect(Border, curY, windowRect.width - Border * 2, Units(6)), newWalletSeedPhrase);

            curY += Units(11);
            int warningHeight = Units(16);
            int padding = 4;
            var rect = new Rect(padding, curY, windowRect.width - padding * 2, warningHeight);

            GUI.Box(rect, "");

            rect.x += Border;
            rect.y += 4;
            rect.width -= Border * 3;

            GUI.Label(rect, "WARNING");
            rect.y += Border*2;
            GUI.Label(rect, "For your own safety, write down these words on a piece of paper and store it safely and hidden.\nThese words serve as a back-up of your wallet.\nWithout a backup, it is impossible to recover your private key,\nand any funds in the account will be lost if something happens to this device.");

            DoButtonGrid<int>(true, backupScreenOptions.Length, Units(2), 0, out _, (index) =>
            {
                return new MenuEntry(index, backupScreenOptions[index], true);
            },
            (selected) =>
            {
                switch (selected)
                {
                    case 0:
                        {
                            GUIUtility.systemCopyBuffer = newWalletSeedPhrase;
                            MessageBox(MessageKind.Default, "Seed phrase copied to the clipboard.");
                            break;
                        }

                    case 1:
                        {
                            int[] wordsOrder;
                            if (AccountManager.Instance.Settings.mnemonicPhraseLength == MnemonicPhraseLength.Twelve_Words)
                                wordsOrder = Enumerable.Range(1, 12).ToArray();
                            else
                                wordsOrder = Enumerable.Range(1, 24).ToArray();

                            var rnd = new System.Security.Cryptography.RNGCryptoServiceProvider();
                            wordsOrder = wordsOrder.OrderBy(x => GetNextInt32(rnd)).ToArray();

                            TrySeedVerification(wordsOrder, (success) =>
                            {
                                if (success)
                                {
                                    newWalletCallback();
                                }
                                else
                                {
                                    PopState();
                                }
                            });
                            break;
                        }

                    case 2:
                        {
                            PopState();
                            break;
                        }
                }
            });
        }

        private void DoFatalScreen()
        {
            int curY;

            curY = Units(5);
            GUI.Label(new Rect(Border, curY, windowRect.width - Border * 2, windowRect.width - (Border+curY)), fatalError);

            var btnWidth = Units(12);
            curY = (int)(windowRect.height - Units(VerticalLayout ? 6 : 7));
            DoButton(true, new Rect((windowRect.width - btnWidth) / 2, curY, btnWidth, Units(2)), "Copy to Clipboard", () =>
            {
                GUIUtility.systemCopyBuffer = fatalError;
                MessageBox(MessageKind.Default, "Error log copied to clipboard.");
            });
        }

        private void DoBalanceScreen()
        {
            var accountManager = AccountManager.Instance;

            var state = accountManager.CurrentState;

            if (state != null && state.flags.HasFlag(AccountFlags.Master) && soulMasterLogo != null)
            {
                GUI.DrawTexture(new Rect(Units(1), Units(2) + 8, Units(8), Units(8)), soulMasterLogo);
            }

            var startY = DrawPlatformTopMenu(() =>
            {
                accountManager.RefreshBalances(false, accountManager.CurrentPlatform);
            });
            
            var endY = DoBottomMenu();

            if (accountManager.BalanceRefreshing)
            {
                DrawCenteredText("Fetching balances...");
                return;
            }

            if (state == null)
            {
                var message = "Temporary error, cannot display balances...";
                if(accountManager.rpcAvailablePhantasma == 0)
                {
                    message = $"Please check your internet connection. All Phantasma RPC servers are unavailable.";
                }
                DrawCenteredText(message);
                return;
            }

            if (state.balances == null)
            {
                DrawCenteredText($"No assets found in this {accountManager.CurrentPlatform} account.");
                return;
            }

            var balanceCount = DoScrollArea<Balance>(ref balanceScroll, startY, endY, VerticalLayout ? Units(7) : Units(6), state.balances.Where(x => x.Total >= 0.001m),
                DoBalanceEntry);

            if (balanceCount == 0)
            {
                DrawCenteredText($"No assets found in this {accountManager.CurrentPlatform} account.");
            }
        }

        private void DoBalanceEntry(Balance balance, int index, int curY, Rect rect)
        {
            var accountManager = AccountManager.Instance;
            var state = accountManager.CurrentState;

            GUI.Box(rect, "");

            var icon = ResourceManager.Instance.GetToken(balance.Symbol, accountManager.CurrentPlatform);
            if (icon != null)
            {
                if (VerticalLayout)
                {
                    var iconY = curY;
                    iconY += Units(1); // Adding border height
                    iconY += Units(1); // Adding first label height
                    iconY += (int)((Units(1) * (double)16 / 18)) * 2; // Adding 2nd and 3rd label heights
                    iconY += 4 * 3; // Adding 3 spacings
                    GUI.DrawTexture(new Rect(Units(2), iconY, Units(2), Units(2)), icon);
                }
                else
                {
                    GUI.DrawTexture(new Rect(Units(2), curY + Units(1), Units(2), Units(2)), icon);
                }
            }

            int btnWidth = Units(11);

            var posY = curY + Units(1) - 8;

            int posX = VerticalLayout ? Units(2) : Units(5);

            var style = GUI.skin.label;

            style.fontSize -= VerticalLayout ? 0 : 4;
            var value = accountManager.GetTokenWorth(balance.Symbol, balance.Available);
            var balanceFormat = $"{MoneyFormat(balance.Available)}";
            GUI.Label(new Rect(posX, posY, rect.width - posX, Units(2)), $"{balanceFormat} {balance.Symbol}" + (value == null ? "" : $" ({value})"));
            style.fontSize += VerticalLayout ? 0 : 4;

            var subRect = new Rect(posX, posY + Units(1) + 4, Units(20), Units(2));
            DrawBalanceLine(ref subRect, balance.Symbol, balance.Staked, "staked");
            DrawBalanceLine(ref subRect, balance.Symbol, balance.Claimable, "claimable");

            string secondaryAction = null;
            bool secondaryEnabled = false;
            Action secondaryCallback = null;

            string tertiaryAction = null;
            bool tertiaryEnabled = false;
            Action tertiaryCallback = null;

            switch (balance.Symbol)
            {
                case "SOUL":
                    if (accountManager.CurrentPlatform == PlatformKind.Phantasma)
                    {
                        if (Input.GetKey(KeyCode.LeftShift))
                        {
                            secondaryAction = "Info";
                            secondaryEnabled = true;
                            secondaryCallback = () =>
                            {
                                accountManager.GetPhantasmaAddressInfo(state.address, accountManager.CurrentAccount,(result, error) =>
                                {
                                    if (!string.IsNullOrEmpty(error))
                                    {
                                        MessageBox(MessageKind.Error, "Something went wrong!\n" + error);
                                        return;
                                    }
                                    else
                                    {
                                        ShowModal("Account information", result,
                                            ModalState.Message, 0, 0, ModalOkCopy, 0, (_, input) => { });
                                        return;
                                    }
                                });
                            };
                        }
                        else
                        {
                            secondaryAction = "Stake";
                            secondaryEnabled = balance.Available > 1.2m;
                            secondaryCallback = () =>
                            {
                                RequireAmount($"Stake SOUL", null, "SOUL", 0.1m, balance.Available, (selectedAmount) =>
                                {
                                    var crownMultiplier = 1m;
                                    var crownBalance = state.balances.Where(x => x.Symbol.ToUpper() == "CROWN").FirstOrDefault();
                                    if (crownBalance != default(Balance))
                                    {
                                        crownMultiplier += crownBalance.Available * 0.05m;
                                    }
                                    var expectedDailyKCAL = (selectedAmount + balance.Staked) * 0.002m * crownMultiplier;

                                    var twoSmsWarning = "";
                                    if (selectedAmount >= 100000)
                                    {
                                        twoSmsWarning = "\n\nSoul Master rewards are distributed evenly to every wallet with 50K or more SOUL. As you are staking over 100K SOUL, to maximise your rewards, you may wish to stake each 50K SOUL in a separate wallet.";
                                    }

                                    var kcalBalance = accountManager.CurrentState.balances.Where(s => s.Symbol == "KCAL").FirstOrDefault();
                                    decimal kcalClaimable = 0;
                                    if(kcalBalance != default)
                                    {
                                        kcalClaimable = kcalBalance.Claimable;
                                    }

                                    var message = $"Do you want to stake {selectedAmount} SOUL?" + 
                                        $"\nYou will be able to claim {MoneyFormat(expectedDailyKCAL, selectedAmount >= 1 ? MoneyFormatType.Standard : MoneyFormatType.Long)} KCAL per day." +
                                        $"\n\nPlease note, after staking you won't be able to unstake SOUL for next 24 hours.";
                                    
                                    if(kcalClaimable > 0)
                                    {
                                        message += $"\n\nAll unclaimed KCAL will be claimed: {MoneyFormat(kcalClaimable, kcalClaimable >= 1 ? MoneyFormatType.Standard : MoneyFormatType.Long)} KCAL.";
                                    }

                                    StakeSOUL(selectedAmount, message + twoSmsWarning, (hash, txResult, error) =>
                                    {
                                        TxResultMessage(hash, txResult, error, "Your SOUL was staked!");
                                    });
                                });
                            };
                        }

                        if (balance.Staked > 0)
                        {
                            tertiaryAction = "Unstake";
                            tertiaryEnabled = (Timestamp.Now - state.stakeTime) >= 86400;
                            tertiaryCallback = () =>
                            {
                                RequireAmount("Unstake SOUL", null, "SOUL", 0.1m, balance.Staked,
                                    (amount) =>
                                    {
                                        var line = "\nAll unclaimed KCAL will be claimed.";

                                        if (amount == balance.Staked && accountManager.CurrentState.name != ValidationUtils.ANONYMOUS_NAME)
                                        {
                                            line += "\nYour account will also lose the current registed name.";
                                        }

                                        PromptBox($"Do you want to unstake {amount} SOUL?\n{line}", ModalYesNo, (result) =>
                                        {
                                            if(result == PromptResult.Success)
                                            {
                                                RequestKCAL("SOUL", (kcal) =>
                                                {
                                                    if (kcal == PromptResult.Success)
                                                    {
                                                        var address = Address.FromText(state.address);

                                                        var sb = new ScriptBuilder();
                                                            
                                                        sb.AllowGas(address, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
                                                        sb.CallContract("stake", "Unstake", address, UnitConversion.ToBigInteger(amount, balance.Decimals));
                                                        sb.SpendGas(address);
                                                        var script = sb.EndScript();

                                                        SendTransaction($"Unstake {amount} SOUL", script, null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, null, DomainSettings.RootChainName, ProofOfWork.None, (hash, txResult, error) =>
                                                        {
                                                            TxResultMessage(hash, txResult, error, "Your SOUL was unstaked!");
                                                        });
                                                    }
                                                });
                                            }
                                        });
                                    });
                            };
                        }
                    }

                    break;

                case "KCAL":
                    if (balance.Claimable > 0)
                    {
                        secondaryAction = "Claim";
                        secondaryEnabled = true;
                        secondaryCallback = () =>
                        {
                            PromptBox($"Do you want to claim KCAL?\nThere is {balance.Claimable} KCAL available.\n\nPlease note, after claiming KCAL you won't be able to unstake SOUL for next 24 hours.", ModalYesNo, (result) =>
                            {
                                if (result == PromptResult.Success)
                                {
                                    RequestKCAL("SOUL", (feeResult) =>
                                    {
                                        if (feeResult == PromptResult.Success)
                                        {
                                            var address = Address.FromText(state.address);

                                            var sb = new ScriptBuilder();

                                            if (balance.Available > 0)
                                            {
                                                sb.AllowGas(address, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
                                                sb.CallContract("stake", "Claim", address, address);
                                            }
                                            else
                                            {
                                                sb.CallContract("stake", "Claim", address, address);
                                                sb.AllowGas(address, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
                                            }

                                            sb.SpendGas(address);
                                            var script = sb.EndScript();

                                            SendTransaction($"Claim {balance.Claimable} KCAL", script, null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, null, DomainSettings.RootChainName, ProofOfWork.None, (hash, txResult, error) =>
                                            {
                                                TxResultMessage(hash, txResult, error, "You claimed some KCAL!");
                                            });


                                        }
                                        else
                                        if (feeResult == PromptResult.Failure)
                                        {
                                            MessageBox(MessageKind.Error, $"KCAL is required to make transactions!");
                                        }
                                    });
                                }
                            });
                        };
                    }
                    break;

                default:
                {
                    if (Tokens.GetToken(balance.Symbol, accountManager.CurrentPlatform, out var token)) 
                    {
                        if (!token.IsFungible())
                        {
                            // It's an NFT. We add additional button to get to NFTs view mode.
                            secondaryAction = "View";
                            secondaryEnabled = balance.Available > 0;
                            secondaryCallback = () =>
                            {
                                // TODO remove later
                                if (!accountManager.Settings.devMode)
                                {
                                    MessageBox(MessageKind.Error, $"Operations with NFTs are not supported yet in this version.");
                                    return;
                                }

                                transferSymbol = balance.Symbol;

                                // We should do this initialization here and not in PushState,
                                // to allow "Back" button to work properly.
                                nftScroll = Vector2.zero;
                                nftTransferList.Clear();
                                nftFilterName = "";
                                nftFilterTypeIndex = 0;
                                nftFilterType = "All";
                                nftFilterRarity = 0;
                                nftFilterMinted = 0;
                                accountManager.RefreshNft(false, transferSymbol);

                                PushState(GUIState.NftView);
                                return;
                            };
                        }
                    }
                    break;
                }
            }

            int btnY = VerticalLayout ? Units(4) + 8 : Units(2);

            if (!string.IsNullOrEmpty(tertiaryAction))
            {
                DoButton(tertiaryEnabled, new Rect(rect.x + rect.width - (Units(18) + 8), curY + btnY, Units(4) + 8, Units(2)), tertiaryAction, () =>
                {
                    tertiaryCallback?.Invoke();
                });
            }

            if (!string.IsNullOrEmpty(secondaryAction))
            {
                DoButton(secondaryEnabled, new Rect(rect.x + rect.width - (Units(12) + 8), curY + btnY, Units(4) + 8, Units(2)), secondaryAction, () =>
                {
                    secondaryCallback?.Invoke();
                });
            }

            string mainAction;
            var mainActionEnabled = balance.Available > 0;
            if (accountManager.CurrentPlatform == PlatformKind.Phantasma &&
                balance.Burnable &&
                balance.Fungible &&
                Input.GetKey(KeyCode.LeftShift))
            {
                mainAction = "Burn";
            }
            else if (accountManager.CurrentPlatform == PlatformKind.Phantasma &&
                balance.Symbol.ToUpper() == "SOUL" &&
                balance.Staked >= 50000 &&
                Input.GetKey(KeyCode.LeftShift))
            {
                mainAction = "SM reward";
                mainActionEnabled = true; // This one should be always enabled
            }
            else
            {
                mainAction = "Send";
            }

            DoButton(mainActionEnabled, new Rect(rect.x + rect.width - (Units(6) + 8), curY + btnY, Units(4) + 8, Units(2)), mainAction, () =>
            {
                if (mainAction == "Send")
                {
                    transferSymbol = balance.Symbol;
                    var transferName = $"{transferSymbol} transfer";
                    Phantasma.SDK.Token transferToken;

                    Tokens.GetToken(transferSymbol, accountManager.CurrentPlatform, out transferToken);

                    // TODO remove later
                    if (!transferToken.IsFungible() && !accountManager.Settings.devMode)
                    {
                        MessageBox(MessageKind.Error, $"Operations with NFTs are not supported yet in this version.");
                        return;
                    }

                    if (string.IsNullOrEmpty(transferToken.flags))
                    {
                        MessageBox(MessageKind.Error, $"Operations with token {transferSymbol} are not supported yet in this version.");
                        return;
                    }

                    if (transferToken.IsTransferable() && !transferToken.IsFungible())
                    {
                        // We should do this initialization here and not in PushState,
                        // to allow "Back" button to work properly.
                        nftScroll = Vector2.zero;
                        nftTransferList.Clear();
                        nftFilterName = "";
                        nftFilterTypeIndex = 0;
                        nftFilterType = "All";
                        nftFilterRarity = 0;
                        nftFilterMinted = 0;
                        accountManager.RefreshNft(false, transferSymbol);

                        PushState(GUIState.Nft);
                        return;
                    }

                    if (!transferToken.IsTransferable())
                    {
                        MessageBox(MessageKind.Error, $"Transfers of {transferSymbol} tokens are not allowed.");
                        return;
                    }

                    ShowModal(transferName, "Enter destination address", ModalState.Input, 3, 64, ModalConfirmCancel, 1, (result, destAddress) =>
                    {
                        if (result == PromptResult.Failure)
                        {
                            return; // user canceled
                        }

                        var ethereumAddressUtil = new Poltergeist.PhantasmaLegacy.Ethereum.Util.AddressUtil();

                        if (Address.IsValidAddress(destAddress) && accountManager.CurrentPlatform.ValidateTransferTarget(transferToken, PlatformKind.Phantasma))
                        {
                            ContinuePhantasmaTransfer(transferName, transferSymbol, destAddress);
                        }
                        else
                        if (ValidationUtils.IsValidIdentifier(destAddress) && destAddress != state.name && accountManager.CurrentPlatform.ValidateTransferTarget(transferToken, PlatformKind.Phantasma))
                        {
                            BeginWaitingModal("Looking up account name");
                            accountManager.ValidateAccountName(destAddress, (lookupAddress) =>
                            {
                                EndWaitingModal();

                                if (lookupAddress != null)
                                {
                                    ContinuePhantasmaTransfer(transferName, transferSymbol, lookupAddress);
                                }
                                else
                                {
                                    MessageBox(MessageKind.Error, "No account with such name exists.");
                                }
                            });
                        }
                        else
                        {
                            MessageBox(MessageKind.Error, "Invalid destination address.");
                        }
                    });

                    modalHints = GenerateAccountHints(accountManager.CurrentPlatform.GetTransferTargets(transferToken));
                }
                else if (mainAction == "SM reward")
                {
                    byte[] script;
                    try
                    {
                        var address = Address.FromText(state.address);

                        var sb = new ScriptBuilder();

                        sb.AllowGas(address, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
                        sb.CallContract("stake", "MasterClaim", address);
                        sb.SpendGas(address);
                        script = sb.EndScript();
                    }
                    catch (Exception e)
                    {
                        MessageBox(MessageKind.Error, "Something went wrong!\n" + e.Message + "\n\n" + e.StackTrace);
                        return;
                    }

                    SendTransaction($"Claim SM reward", script, null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, null, DomainSettings.RootChainName, ProofOfWork.None, (hash, txResult, error) =>
                    {
                        TxResultMessage(hash, txResult, error, "You claimed SM reward!");
                    });
                }
                else if (mainAction == "Burn")
                {
                    RequireAmount($"Burn {balance.Symbol} tokens", null, balance.Symbol, 0.1m, balance.Available, (amountToBurn) =>
                    {
                        PromptBox($"Are you sure you want to burn {amountToBurn} {balance.Symbol} tokens?", ModalConfirmCancel, (result) =>
                        {
                            if (result == PromptResult.Success)
                            {
                                byte[] script;
                                try
                                {
                                    var target = Address.FromText(state.address);

                                    var sb = new ScriptBuilder();
                                    sb.AllowGas(target, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
                                    sb.CallInterop("Runtime.BurnTokens", target, balance.Symbol, UnitConversion.ToBigInteger(amountToBurn, balance.Decimals));
                                    sb.SpendGas(target);
                                    script = sb.EndScript();
                                }
                                catch (Exception e)
                                {
                                    MessageBox(MessageKind.Error, "Something went wrong!\n" + e.Message + "\n\n" + e.StackTrace);
                                    return;
                                }

                                SendTransaction($"Burn {amountToBurn} {balance.Symbol} tokens", script, null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, null, DomainSettings.RootChainName, ProofOfWork.None, (hash, txResult, error) =>
                                {
                                    TxResultMessage(hash, txResult, error, $"You burned {amountToBurn} {balance.Symbol} tokens!");
                                });
                            }
                        }, 10);
                    });
                }
            });
        }

        private void DoNftScreen()
        {
            var accountManager = AccountManager.Instance;

            var nfts = accountManager.CurrentNfts;
            if (accountManager.BalanceRefreshing)
            {
                DrawCenteredText((nfts != null) ? $"Fetching NFTs ({nfts.Count})..." : "Fetching NFTs...");
                return;
            }

            var startY = Units(VerticalLayout ? 11 : 7);
            var nftToolsY = startY;
            startY += (VerticalLayout) ? Units(6) : Units(4);
            var endY = DoBottomMenuForNft();

            if (nfts == null)
            {
                DrawCenteredText("Loading...");
                return;
            }

            // Sorting NFT list.
            accountManager.SortTtrsNfts(transferSymbol);
            nfts = accountManager.CurrentNfts;

            // Filtering NFT list, if filters are applied.
            nftFilteredList.Clear();
            if (!String.IsNullOrEmpty(nftFilterName) || nftFilterType != "All" || nftFilterRarity != (int)ttrsNftRarity.All || nftFilterMinted != (int)nftMinted.All)
            {
                nfts.ForEach((x) => {
                    if (transferSymbol == "TTRS")
                    {
                        var item = TtrsStore.GetNft(x.ID);

                        if ((String.IsNullOrEmpty(nftFilterName) || item.item_info.name_english.ToUpper().Contains(nftFilterName.ToUpper())) &&
                            (nftFilterType == "All" || item.item_info.display_type_english == nftFilterType) &&
                            (nftFilterRarity == (int)ttrsNftRarity.All || (int)item.item_info.rarity == nftFilterRarity) &&
                            (nftFilterMinted == (int)nftMinted.All ||
                             (nftFilterMinted == (int)nftMinted.Last_15_Mins && DateTime.Compare(item.timestampDT(), DateTime.Now.AddMinutes(-15)) >= 0) ||
                             (nftFilterMinted == (int)nftMinted.Last_Hour && DateTime.Compare(item.timestampDT(), DateTime.Now.AddHours(-1)) >= 0) ||
                             (nftFilterMinted == (int)nftMinted.Last_24_Hours && DateTime.Compare(item.timestampDT(), DateTime.Now.AddDays(-1)) >= 0) ||
                             (nftFilterMinted == (int)nftMinted.Last_Week && DateTime.Compare(item.timestampDT(), DateTime.Now.AddDays(-7)) >= 0) ||
                             (nftFilterMinted == (int)nftMinted.Last_Month && DateTime.Compare(item.timestampDT(), DateTime.Now.AddMonths(-1)) >= 0)
                            ))
                        {
                            nftFilteredList.Add(x);
                        }
                    }
                    else if (transferSymbol == "GAME")
                    {
                        var item = GameStore.GetNft(x.ID);

                        if ((String.IsNullOrEmpty(nftFilterName) || (item.meta?.name_english.ToUpper().Contains(nftFilterName.ToUpper()) ?? false)) &&
                            (nftFilterMinted == (int)nftMinted.All ||
                             (nftFilterMinted == (int)nftMinted.Last_15_Mins && DateTime.Compare(item.parsed_rom.timestampDT(), DateTime.Now.AddMinutes(-15)) >= 0) ||
                             (nftFilterMinted == (int)nftMinted.Last_Hour && DateTime.Compare(item.parsed_rom.timestampDT(), DateTime.Now.AddHours(-1)) >= 0) ||
                             (nftFilterMinted == (int)nftMinted.Last_24_Hours && DateTime.Compare(item.parsed_rom.timestampDT(), DateTime.Now.AddDays(-1)) >= 0) ||
                             (nftFilterMinted == (int)nftMinted.Last_Week && DateTime.Compare(item.parsed_rom.timestampDT(), DateTime.Now.AddDays(-7)) >= 0) ||
                             (nftFilterMinted == (int)nftMinted.Last_Month && DateTime.Compare(item.parsed_rom.timestampDT(), DateTime.Now.AddMonths(-1)) >= 0)
                            ))
                        {
                            nftFilteredList.Add(x);
                        }
                    }
                    else
                    {
                        var item = accountManager.GetNft(x.ID);

                        if ((String.IsNullOrEmpty(nftFilterName) || item.parsedRom.GetName().ToUpper().Contains(nftFilterName.ToUpper())) &&
                            (nftFilterMinted == (int)nftMinted.All ||
                             (nftFilterMinted == (int)nftMinted.Last_15_Mins && DateTime.Compare(item.parsedRom.GetDate(), DateTime.Now.AddMinutes(-15)) >= 0) ||
                             (nftFilterMinted == (int)nftMinted.Last_Hour && DateTime.Compare(item.parsedRom.GetDate(), DateTime.Now.AddHours(-1)) >= 0) ||
                             (nftFilterMinted == (int)nftMinted.Last_24_Hours && DateTime.Compare(item.parsedRom.GetDate(), DateTime.Now.AddDays(-1)) >= 0) ||
                             (nftFilterMinted == (int)nftMinted.Last_Week && DateTime.Compare(item.parsedRom.GetDate(), DateTime.Now.AddDays(-7)) >= 0) ||
                             (nftFilterMinted == (int)nftMinted.Last_Month && DateTime.Compare(item.parsedRom.GetDate(), DateTime.Now.AddMonths(-1)) >= 0)
                            ))
                        {
                            nftFilteredList.Add(x);
                        }
                    }
                });
                nfts = nftFilteredList;
            }

            // Number of displayed NFTs changed, switching to first page.
            if (nfts.Count != nftCount)
            {
                nftPageNumber = 0;
            }

            nftCount = nfts.Count;
            nftPageCount = nftCount / nftPageSize + 1;

            // Making NFT list for current page.
            var nftPage = new List<string>();
            for(int i = nftPageSize * nftPageNumber; i < Math.Min(nftPageSize * (nftPageNumber + 1), nfts.Count); i++)
            {
                nftPage.Add(nfts[i].ID);
            }
            var nftOnPageCount = DoScrollArea<string>(ref nftScroll, startY, endY, VerticalLayout ? Units(5) : Units(4), nftPage,
                DoNftEntry);

            if (nftOnPageCount == 0)
            {
                DrawCenteredText($"No {transferSymbol} NFTs found for this {accountManager.CurrentPlatform} account.");
            }

            DrawNftTools(nftToolsY);

            DrawPlatformTopMenu(() =>
            {
                accountManager.RefreshBalances(false, accountManager.CurrentPlatform);
                accountManager.RefreshNft(false, transferSymbol);
                accountManager.ResetNftsSorting();
            }, false);
        }

        // Used for both NFT list and transfer NFT list.
        private void DoNftEntry(string entryId, int index, int curY, Rect rect)
        {
            if(string.IsNullOrEmpty(entryId))
            {
                return;
            }
            var accountManager = AccountManager.Instance;

            string imageUrl = "";
            string nftName;
            string nftDescription;
            string infusionDescription = "";

            if (transferSymbol == "TTRS")
            {
                var item = TtrsStore.GetNft(entryId);

                if (!String.IsNullOrEmpty(item.item_info.name_english))
                {
                    imageUrl = item.img;
                }

                string rarity;
                switch (item.item_info.rarity)
                {
                    case 1:
                        rarity = VerticalLayout ? "/Con" : " / Consumer";
                        break;
                    case 2:
                        rarity = VerticalLayout ? "/Ind" : " / Industrial";
                        break;
                    case 3:
                        rarity = VerticalLayout ? "/Pro" : " / Professional";
                        break;
                    case 4:
                        rarity = VerticalLayout ? "/Col" : " / Collector";
                        break;
                    default:
                        rarity = "";
                        break;
                }

                nftName = item.item_info.name_english;

                var nftType = item.item_info.display_type_english;
                if (VerticalLayout)
                {
                    switch (nftType)
                    {
                        case "Vehicle":
                            nftType = "Veh";
                            break;
                        case "Part":
                            nftType = "Prt";
                            break;
                        case "License":
                            nftType = "Lic";
                            break;
                        default:
                            break;
                    }
                }

                nftDescription = item.mint == 0 ? "" : (VerticalLayout ? "#" : "Mint #") + item.mint + " " + (VerticalLayout ? item.timestamp.ToString("dd.MM.yy") : item.timestamp.ToString("dd.MM.yyyy HH:mm:ss")) + (VerticalLayout ? " " : " / ") + nftType + rarity;
            }
            else if (transferSymbol == "GAME")
            {
                var item = GameStore.GetNft(entryId);

                if (!String.IsNullOrEmpty(item.meta?.name_english))
                {
                    imageUrl = item.parsed_rom.img_url;
                }

                nftName = item.meta?.name_english;

                nftDescription = item.mint == 0 ? "" : (VerticalLayout ? "#" : "Mint #") + item.mint + " " + (VerticalLayout ? item.parsed_rom.timestampDT().ToString("dd.MM.yy") : item.parsed_rom.timestampDT().ToString("dd.MM.yyyy HH:mm:ss")) + (VerticalLayout ? " " : " / ") + item.meta?.description_english;
            }
            else
            {
                var item = accountManager.GetNft(entryId);

                imageUrl = item.GetPropertyValue("ImageURL");

                DateTime nftDate = new DateTime();
                if (item.parsedRom != null)
                {
                    nftDate = item.parsedRom.GetDate();
                }

                nftName = item.GetPropertyValue("Name");
                nftDescription = item.GetPropertyValue("Description");

                nftDescription = (item.mint ?? 0) == 0 ? "" : (VerticalLayout ? "#" : "Mint #") + item.mint + " " +
                    (nftDate == DateTime.MinValue ? "" : (VerticalLayout ? nftDate.ToString("dd.MM.yy") : nftDate.ToString("dd.MM.yyyy HH:mm:ss"))) +
                    (String.IsNullOrEmpty(nftDescription) ? "" : ((VerticalLayout ? " " : " / ") + nftDescription));

                if (item.infusion != null && item.infusion.Length > 0)
                {
                    infusionDescription = VerticalLayout ? "" : "Infusions: ";

                    var fungibleInfusions = new Dictionary<string, decimal>();
                    var nftInfusions = new Dictionary<string, int>();
                    for (var i = 0; i < item.infusion.Length; i++)
                    {
                        var symbol = item.infusion[i].Key;
                        var amountOrId = item.infusion[i].Value;

                        if (Tokens.GetToken(symbol, accountManager.CurrentPlatform, out var token))
                        {
                            if (token.IsFungible())
                                fungibleInfusions.Add(symbol, UnitConversion.ToDecimal(amountOrId, token.decimals));
                            else
                            {
                                if (nftInfusions.ContainsKey(symbol))
                                    nftInfusions[symbol] += 1;
                                else
                                    nftInfusions.Add(symbol, 1);
                            }
                        }
                    }
                    for (var i = 0; i < fungibleInfusions.Count(); i++)
                    {
                        infusionDescription += (i > 0 ? ", " : "") + fungibleInfusions.ElementAt(i).Value + " " + fungibleInfusions.ElementAt(i).Key;
                    }
                    if (VerticalLayout)
                    {
                        var nftInfusedCount = nftInfusions.Sum(x => x.Value);
                        if(nftInfusedCount > 0)
                            infusionDescription += (fungibleInfusions.Count() > 0 ? ", " : "") + nftInfusedCount + " NFT" + (nftInfusedCount > 1 ? "s" : "");
                    }
                    else
                    {
                        for (var i = 0; i < nftInfusions.Count(); i++)
                        {
                            infusionDescription += (fungibleInfusions.Count() > 0 || i > 0 ? ", " : "") + (nftInfusions.ElementAt(i).Value > 1 ? nftInfusions.ElementAt(i).Value + " " : "") + nftInfusions.ElementAt(i).Key + " NFT" + (nftInfusions.ElementAt(i).Value > 1 ? "s" : "");
                        }
                    }
                }
            }

            // Fixing CROWNs image url
            imageUrl = imageUrl?.Replace("phantasma.io", "phantasma.info");

            if (!String.IsNullOrEmpty(imageUrl))
            {
                var image = NftImages.GetImage(imageUrl);

                if (!String.IsNullOrEmpty(image.Url))
                {
                    var textureDisplayedWidth = VerticalLayout ? Units(7) - Units(3) : Units(6) - Units(3) + 8;
                    var textureDisplayedHeight = VerticalLayout ? Units(3) : Units(3) - 8;

                    if (image.Url.StartsWith("ipfs-audio://"))
                        GUI.DrawTexture(new Rect(Units(2), VerticalLayout ? curY + Units(1) : curY + 12, (float)textureDisplayedHeight * ((float)ResourceManager.Instance.NftAudioPlaceholder.width / (float)ResourceManager.Instance.NftAudioPlaceholder.height), textureDisplayedHeight), ResourceManager.Instance.NftAudioPlaceholder);
                    else if (image.Url.StartsWith("ipfs-video://"))
                        GUI.DrawTexture(new Rect(Units(2), VerticalLayout ? curY + Units(1) : curY + 12, (float)textureDisplayedHeight * ((float)ResourceManager.Instance.NftVideoPlaceholder.width / (float)ResourceManager.Instance.NftVideoPlaceholder.height), textureDisplayedHeight), ResourceManager.Instance.NftVideoPlaceholder);
                    else if (image.Texture == null)
                        GUI.DrawTexture(new Rect(Units(2), VerticalLayout ? curY + Units(1) : curY + 12, (float)textureDisplayedHeight * ((float)ResourceManager.Instance.NftPhotoPlaceholder.width / (float)ResourceManager.Instance.NftPhotoPlaceholder.height), textureDisplayedHeight), ResourceManager.Instance.NftPhotoPlaceholder);
                    else
                    {
                        var width = (float)textureDisplayedHeight * ((float)image.Texture.width / (float)image.Texture.height);
                        var height = (float)textureDisplayedHeight;
                        if (width > textureDisplayedWidth)
                        {
                            var correction = textureDisplayedWidth / width;
                            width = textureDisplayedWidth;
                            height = height * correction;
                        }

                        // Following code helps to center images in the image area.
                        var x = Units(2);
                        if (width < textureDisplayedWidth)
                            x += (int)((textureDisplayedWidth - width) / 2);
                        var y = VerticalLayout ? curY + Units(1) : curY + 12;
                        if (height < textureDisplayedHeight)
                            y += (int)((textureDisplayedHeight - height) / 2);

                        GUI.DrawTexture(new Rect(x, y, width, height), image.Texture);
                    }
                }
            }

            if (String.IsNullOrEmpty(nftName))
            {
                if (VerticalLayout)
                {
                    nftName = "#" + entryId.Substring(0, 4) + "..." + entryId.Substring(entryId.Length - 4);
                }
                else
                {
                    nftName = "#" + entryId.Substring(0, 8) + "..." + entryId.Substring(entryId.Length - 8);
                }
            }

            if (VerticalLayout && nftName.Length > 18)
                nftName = nftName.Substring(0, 15) + "...";
            else if (nftName.Length > 103)
                nftName = nftName.Substring(0, 100) + "...";

            float nameYPosition = curY;
            float descYPosition = curY;
            if (!String.IsNullOrEmpty(infusionDescription))
            {
                nameYPosition += VerticalLayout ? - 2 : - 8;
                descYPosition += VerticalLayout ? Units(1) + 6 : Units(1) - 2;
            }
            else
            {
                nameYPosition += VerticalLayout ? 4 : 0;
                descYPosition += VerticalLayout ? Units(2) + 4 : Units(1) + 8;
            }

            GUI.Label(new Rect(VerticalLayout ? Units(7) : Units(6) + 8, nameYPosition, rect.width - Units(6), Units(2) + 4), nftName);

            if (!String.IsNullOrEmpty(nftDescription))
            {
                if (VerticalLayout)
                {
                    if (nftDescription.Length > 25)
                        nftDescription = nftDescription.Substring(0, 22) + "...";
                }
                else
                {
                    if (nftDescription.Length > 100)
                        nftDescription = nftDescription.Substring(0, 97) + "...";
                }

                var style = GUI.skin.label;
                style.fontSize -= VerticalLayout ? 2 : 4;
                GUI.Label(new Rect(VerticalLayout ? Units(7) : Units(6) + 8, descYPosition, rect.width - Units(6), Units(2)), nftDescription);
                style.fontSize += VerticalLayout ? 2 : 4;
            }

            if (!String.IsNullOrEmpty(infusionDescription))
            {
                var style = GUI.skin.label;
                style.fontSize -= VerticalLayout ? 2 : 4;
                GUI.Label(new Rect(VerticalLayout ? Units(7) : Units(6) + 8, VerticalLayout ? curY + Units(2) + 10 : curY + Units(2), rect.width - Units(6), Units(2)), infusionDescription);
                style.fontSize += VerticalLayout ? 2 : 4;
            }

            Rect btnRectToggle;
            Rect btnRect;

            if (VerticalLayout)
            {
                curY += Units(2);
                btnRectToggle = new Rect(rect.x + rect.width - Units(8), curY - 4, Units(1), Units(1));
                btnRect = new Rect(rect.x + rect.width - Units(6), curY, Units(4), Units(1));
            }
            else
            {
                btnRectToggle = new Rect(rect.x + rect.width - Units(8), curY + Units(1) + 4, Units(1), Units(1));
                btnRect = new Rect(rect.x + rect.width - Units(6), curY + Units(1) + 8, Units(4), Units(1));
            }

            if (DrawNftToolsAreActive())
            {
                GUI.enabled = false;
            }
            if (guiState != GUIState.NftView)
            {
                var nftIsSelected = nftTransferList.Exists(x => x == entryId);
                if (GUI.Toggle(btnRectToggle, nftIsSelected, ""))
                {
                    if (!nftIsSelected)
                    {
                        nftTransferList.Add(entryId);
                    }
                }
                else
                {
                    if (nftIsSelected)
                    {
                        nftTransferList.Remove(nftTransferList.Single(x => x == entryId));
                    }
                }
            }
            GUI.enabled = true;

            DoButton(!DrawNftToolsAreActive(), btnRect, "View", () =>
            {
                if (transferSymbol == "TTRS")
                    Application.OpenURL("https://www.22series.com/part_info?id=" + entryId);
                else
                    Application.OpenURL(accountManager.GetPhantasmaNftURL(transferSymbol, entryId));
            });
        }

        private void DoNftTransferListScreen()
        {
            var accountManager = AccountManager.Instance;

            var startY = DrawPlatformTopMenu(() =>
            {
            }, false);
            var endY = DoBottomMenuForNftTransferList();

            // We have to remake whole list to have correct order of selected items.
            var nftTransferListCopy = new List<string>();
            accountManager.CurrentNfts.ForEach((x) => { if (nftTransferList.Exists(y => y == x.ID)) { nftTransferListCopy.Add(x.ID); } });
            nftTransferList = nftTransferListCopy;

            // We can modify nftTransferList while enumerating,
            // so we should use a copy of it.
            nftTransferListCopy = new List<string>();
            nftTransferList.ForEach(x => nftTransferListCopy.Add(x));

            var nftTransferCount = DoScrollArea<string>(ref nftTransferListScroll, startY, endY, VerticalLayout ? Units(5) : Units(4), nftTransferListCopy,
                DoNftEntry);

            if (nftTransferCount == 0)
            {
                DrawCenteredText($"No NFTs selected for transfer.");
            }
        }

        private void DoHistoryScreen()
        {
            var accountManager = AccountManager.Instance;

            var startY = DrawPlatformTopMenu(() =>
            {
                accountManager.RefreshHistory(false, accountManager.CurrentPlatform);
            });
            
            var endY = DoBottomMenu();

            if (accountManager.HistoryRefreshing)
            {
                DrawCenteredText("Fetching history...");
                return;
            }

            var history = accountManager.CurrentHistory;

            if (history == null)
            {
                var message = "Temporary error, cannot display history...";
                if (accountManager.rpcAvailablePhantasma == 0)
                {
                    message = $"Please check your internet connection. All Phantasma RPC servers are unavailable.";
                }
                DrawCenteredText(message);
                return;
            }

            int curY = Units(12);

            var historyCount = DoScrollArea<HistoryEntry>(ref balanceScroll, startY, endY, VerticalLayout ? Units(4) : Units(3), history,
                DoHistoryEntry);

            if (historyCount == 0)
            {
                DrawCenteredText($"No transactions found for this {accountManager.CurrentPlatform} account.");
            }
        }

        private void DoHistoryEntry(HistoryEntry entry, int index, int curY, Rect rect)
        {
            var accountManager = AccountManager.Instance;

            var date = String.Format("{0:g}", entry.date);

            GUI.Label(new Rect(Units(2), curY + 4, Units(20), Units(2)), VerticalLayout ? entry.hash.Substring(0, 16)+"...": entry.hash);

            Rect btnRect;

            if (VerticalLayout)
            {
                curY += Units(2);
                GUI.Label(new Rect(Units(2), curY, Units(20), Units(2)), date);
                btnRect = new Rect(rect.x + rect.width - Units(6), curY - 8, Units(4), Units(1));
            }
            else
            {
                GUI.Label(new Rect(Units(26), curY + 4, Units(20), Units(2)), date);
                btnRect = new Rect(rect.x + rect.width - Units(6), curY + Units(1), Units(4), Units(1));
            }

            DoButton(!string.IsNullOrEmpty(entry.url), btnRect, "View", () =>
            {
                Application.OpenURL(entry.url);
            });
        }

        private void DoAccountScreen()
        {
            var accountManager = AccountManager.Instance;

            var startY = DrawPlatformTopMenu(null);

            int curY = startY;

            curY = Units(10);

            if (VerticalLayout)
            {
                curY += Units(2) + 8;
            }

            int btnWidth = Units(8);
            int centerX = (int)(windowRect.width - btnWidth) / 2;

            var platform = accountManager.CurrentPlatform;
            if (QRCodeTextures.ContainsKey(platform))
            {
                var qrTex = QRCodeTextures[platform];
                var qrResolution = 200;
                var qrRect = new Rect((windowRect.width - qrResolution) / 2, VerticalLayout ? curY + Units(2) : curY, qrResolution, qrResolution);

                DrawDropshadow(qrRect);
                GUI.DrawTexture(qrRect, qrTex);
                curY += qrResolution;
                curY += Units(1);
            }

            int btnOffset = Units(2) + 8;

            if (VerticalLayout)
            {
                btnOffset += Units(7);
            }

            DoAccountManagementMenu(btnOffset);

            DoBottomMenu();
        }

        private List<string> StringSplit(string input, int splitBy)
        {
            List<string> result = new();
            var parts = (int)Math.Ceiling((decimal)input.Length / splitBy);
            for (var i = 0; i < parts; i++)
            {
                if (i == parts - 1)
                {
                    result.Add(input.Substring(i * splitBy, input.Length - i * splitBy));
                }
                else
                {
                    result.Add(input.Substring(i * splitBy, splitBy));
                }
            }

            return result;
        }

        private string KeyPrepareForMessageBox(string key)
        {
            if (VerticalLayout)
            {
                return string.Join('\n', StringSplit(key, 24).Select(x => string.Join(' ', StringSplit(x, 8))));
            }
            else
            {
                return string.Join(' ', StringSplit(key, 8));
            }
        }

        private void DoAccountManagementMenu(int btnOffset)
        {
            var accountManager = AccountManager.Instance;
            int posY;

            var menu = explorerMenu;

            DoButtonGrid<int>(false, menu.Length, 0, -btnOffset * 2, out posY, (index) =>
            {
                return new MenuEntry(index, menu[index], enabled);
            },
            (selected) =>
            {
                switch (selected)
                {
                    case 0:
                        {
                            Application.OpenURL(accountManager.GetEthExplorerURL(accountManager.CurrentAccount.ethAddress));
                            break;
                        }
                    case 1:
                        {
                            Application.OpenURL(accountManager.GetBscExplorerURL(accountManager.CurrentAccount.ethAddress));
                            break;
                        }
                    case 2:
                        {
                            Application.OpenURL(accountManager.GetN2ExplorerURL(accountManager.CurrentAccount.neoAddress));
                            break;
                        }
                }
            });

            menu = managerMenu;
            if (Input.GetKey(KeyCode.LeftShift))
            {
                menu = (string[])managerMenu.Clone();
                menu[3] = "Sign message";
            }
            else if (Input.GetKey(KeyCode.LeftControl))
            {
                menu = (string[])managerMenu.Clone();
                menu[3] = "Verify signature";
            }

            DoButtonGrid<int>(false, menu.Length, 0, -btnOffset, out posY, (index) =>
            {
                var enabled = true;

                if (accountManager.CurrentState != null)
                {
                    switch (index)
                    {
                        case 1:
                            // Disable account migration for now
                            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
                            {
                                enabled = false;
                            }
                            break;
                    }
                }
                else
                {
                    enabled = false;
                }

                return new MenuEntry(index, menu[index], enabled);
            },
            (selected) =>
            {
                switch (selected)
                {
                    case 0:
                        {
                            ShowModal("Private key export", $"Show private key in WIF format (recommended) or in HEX format." +
                                "\n\nNEVER SHARE YOUR PRIVATE KEY with ANYONE, including TEAM, SUPPORT or COMMUNITY ADMINS." +
                                "\n\nFollowing screen will reveal your private key. It provides full access to your wallet and funds." +
                                " Press 'WIF format' or 'HEX format' buttons to expose private key in corresponding format." +
                                "\n\nMake sure NO ONE IS LOOKING AT YOUR SCREEN.",
                                ModalState.Message, 0, 0, ModalHexWifCancel, 0, (result, input) =>
                                {
                                    if (result == PromptResult.Custom_1)
                                    {
                                        RequestPassword("Export private key (HEX)", accountManager.CurrentPlatform, true, false, (auth) =>
                                        {
                                            if (auth == PromptResult.Success)
                                            {
                                                var keys = EthereumKey.FromWIF(accountManager.CurrentWif);
                                                var hexKey = PhantasmaLegacy.Ethereum.Hex.HexConvertors.Extensions.HexByteConvertorExtensions.ToHex(keys.PrivateKey);

                                                ShowModal("Your private key (HEX)",
                                                    KeyPrepareForMessageBox(hexKey),
                                                    ModalState.Message, 0, 0, ModalOkCopy_NoAutoCopy, 1, (result1, _) =>
                                                 {
                                                     if (result1 != PromptResult.Success) // Means "Copy to clipboard" button was pressed
                                                     {
                                                         GUIUtility.systemCopyBuffer = hexKey;
                                                         MessageBox(MessageKind.Default, "Your private key was copied to the clipboard.");
                                                     }
                                                 });
                                            }
                                        },
                                        ignoreStoredPassword: true);
                                    }
                                    else if (result == PromptResult.Custom_2)
                                    {
                                        RequestPassword("Export private key (WIF)", accountManager.CurrentPlatform, true, false, (auth) =>
                                        {
                                            if (auth == PromptResult.Success)
                                            {
                                                ShowModal("Your private key (WIF)", KeyPrepareForMessageBox(accountManager.CurrentWif), ModalState.Message, 0, 0, ModalOkCopy_NoAutoCopy, 1, (result1, _) =>
                                                {
                                                    if (result1 != PromptResult.Success) // Means "Copy to clipboard" button was pressed
                                                    {
                                                        GUIUtility.systemCopyBuffer = accountManager.CurrentWif;
                                                        MessageBox(MessageKind.Default, "Your private key was copied to the clipboard.");
                                                    }
                                                });
                                            }
                                        },
                                        ignoreStoredPassword: true);
                                    }
                                });
                            break;
                        }

                    case 1:
                        {
                            ShowModal("Account migration", "Insert WIF of the target account", ModalState.Input, 32, 64, ModalConfirmCancel, 1, (wifResult, wif) =>
                            {
                                if (wifResult != PromptResult.Success)
                                {
                                    return; // user cancelled
                                }

                                {
                                    var oldWif = accountManager.CurrentWif;

                                    var newKeys = PhantasmaKeys.FromWIF(wif);
                                    if (newKeys.Address.Text != accountManager.CurrentState.address)
                                    {
                                        PromptBox("Are you sure you want to migrate this account?\n\nBefore doing migration, make sure that both old and new private keys (WIFs or seed phrases) are safely stored.\n\nCheck your Eth/Neo/BSC balances for current wallet, if they have funds, move them to a new wallet before doing migration.\n\nBy doing a migration, any existing Phantasma rewards will be transferred without penalizations.\nTarget address: " + newKeys.Address.Text, ModalYesNo, (result) =>
                                        {
                                            if (result == PromptResult.Success)
                                            {
                                                var address = Address.FromText(accountManager.CurrentState.address);

                                                var sb = new ScriptBuilder();

                                                sb.AllowGas(address, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
                                                sb.CallContract("account", "Migrate", address, newKeys.Address);
                                                sb.SpendGas(address);
                                                var script = sb.EndScript();

                                                SendTransaction("Migrate account", script, null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, null, DomainSettings.RootChainName, ProofOfWork.None, (hash, txResult, error) =>
                                                {
                                                    if (string.IsNullOrEmpty(error) && hash != Hash.Null)
                                                    {
                                                        accountManager.ReplaceAccountWIF(accountManager.CurrentIndex, wif, accountManager.CurrentPasswordHash, out var deletedDuplicateWallet);
                                                        CloseCurrentStack();

                                                        ShowModal("Message",
                                                            $"The account was migrated.\n{(string.IsNullOrEmpty(deletedDuplicateWallet) ? "" : $"\nDuplicate account '{deletedDuplicateWallet}' was deleted.\n")}If you haven't stored old account's WIF yet, please do it now.\n\nOld WIF: {oldWif}",
                                                            ModalState.Message, 0, 0, ModalOkCopy, 0, (_, input) =>
                                                            {
                                                            });
                                                    }
                                                    else
                                                    {
                                                        TxResultMessage(hash, txResult, error, null, "It was not possible to migrate the account.");
                                                    }
                                                });
                                            }
                                        });
                                    }
                                    else
                                    {
                                        MessageBox(MessageKind.Error, "You need to provide a different WIF.");
                                    }
                                }

                            });
                            break;
                        }

                    case 2:
                        {
                            var state = accountManager.CurrentState;
                            decimal stake = state != null ? state.balances.Where(x => x.Symbol == DomainSettings.StakingTokenSymbol).Select(x => x.Staked).FirstOrDefault() : 0;

                            if (stake >= 1)
                            {
                                ShowModal("Setup Name", $"Enter a name for the chain address.\nOther users will be able to transfer assets directly to this name.", ModalState.Input, AccountManager.MinAccountNameLength, AccountManager.MaxAccountNameLength, ModalConfirmCancel, 1, (result, name) =>
                                {
                                    if (result == PromptResult.Success)
                                    {
                                        if (ValidationUtils.IsValidIdentifier(name))
                                        {
                                            RequestKCAL(null, (kcalResult) =>
                                            {
                                                if (kcalResult == PromptResult.Success)
                                                {
                                                    byte[] script;

                                                    try
                                                    {
                                                        var source = Address.FromText(accountManager.CurrentState.address);

                                                        var sb = new ScriptBuilder();
                                                        sb.AllowGas(source, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
                                                        sb.CallContract("account", "RegisterName", source, name);
                                                        sb.SpendGas(source);
                                                        script = sb.EndScript();
                                                    }
                                                    catch (Exception e)
                                                    {
                                                        MessageBox(MessageKind.Error, "Something went wrong!\n" + e.Message + "\n\n" + e.StackTrace);
                                                        return;
                                                    }

                                                    SendTransaction($"Register address name\nName: {name}\nAddress: {accountManager.CurrentState.address}?", script, null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, null, DomainSettings.RootChainName, ProofOfWork.None, (hash, txResult, error) =>
                                                    {
                                                        if (string.IsNullOrEmpty(error) && hash != Hash.Null)
                                                        {
                                                            SetState(guiState); // force updating the current UI

                                                            if (AccountManager.Instance.CurrentAccount.name != name)
                                                            {
                                                                PromptBox("The address name was set successfully.\nDo you also want to change the local name for the account?\nThe local name is only visible in this device.", ModalYesNo, (localChange) =>
                                                                {
                                                                    if (localChange == PromptResult.Success)
                                                                    {
                                                                        if (accountManager.RenameAccount(name))
                                                                        {
                                                                            MessageBox(MessageKind.Default, $"The local account name was renamed '{name}'.");
                                                                        }
                                                                        else
                                                                        {
                                                                            MessageBox(MessageKind.Error, $"Was not possible to rename the local account.\nHowever the public address was renamed with success.");
                                                                        }
                                                                    }
                                                                });
                                                            }
                                                        }
                                                        else
                                                        {
                                                            MessageBox(MessageKind.Error, "An error occured when trying to setup the address name.");
                                                        }
                                                    });

                                                }
                                            });
                                        }
                                        else
                                        {
                                            MessageBox(MessageKind.Error, "That name is not a valid Phantasma address name.\nNo spaces allowed, only lowercase letters and numbers.\nMust be between 3 and 15 characters in length.");
                                        }
                                    }
                                });
                            }
                            else
                            {
                                MessageBox(MessageKind.Error, $"To register an address name you will need at least some SOUL staked.");
                            }
                            break;
                        }
                    case 3:
                        {
                            if (Input.GetKey(KeyCode.LeftShift))
                            {
                                ShowModal("", "Select chain", ModalState.Input, 1, 10, ModalConfirmCancel, 1, (result, chain) =>
                                {
                                    if (result == PromptResult.Failure)
                                    {
                                        return; // user cancelled
                                    }

                                    ShowModal("", "Enter message", ModalState.Input, 1, -1, ModalConfirmCancel, 4, (result2, message) =>
                                    {
                                        if (result2 == PromptResult.Failure)
                                        {
                                            return; // user cancelled
                                        }

                                        var messageBytes = System.Text.Encoding.ASCII.GetBytes(message);

                                        var hash = Base16.Encode(Phantasma.Core.Cryptography.Hashing.SHA256.ComputeHash(messageBytes));
                                        if (accountManager.Settings.devMode)
                                        {
                                            Log.Write($"Signed message: '{message}', hash: '{hash}'");
                                        }

                                        var wif = AccountManager.Instance.CurrentAccount.GetWif(AccountManager.Instance.CurrentPasswordHash);
                                        byte[] signatureBytes;

                                        if (chain == "Phantasma")
                                        {
                                            var keys = PhantasmaKeys.FromWIF(wif);
                                            var phaSignature = keys.Sign(messageBytes);
                                            signatureBytes = ((Ed25519Signature)phaSignature).Bytes;
                                        }
                                        else if (chain == "Ethereum")
                                        {
                                            var keys = EthereumKey.FromWIF(wif);
                                            signatureBytes = ECDsa.SignDeterministic(messageBytes, keys.PrivateKey, ECDsaCurve.Secp256k1);
                                        }
                                        else if (chain == "Neo Legacy")
                                        {
                                            var keys = NeoKeys.FromWIF(wif);
                                            signatureBytes = ECDsa.SignDeterministic(messageBytes, keys.PrivateKey, ECDsaCurve.Secp256r1);
                                        }
                                        else
                                        {
                                            MessageBox(MessageKind.Error, "Unsupported chain");
                                            return;
                                        }

                                        var signature = Base16.Encode(signatureBytes);
                                        
                                        ShowModal("Signature", signature, ModalState.Message, 0, 0, ModalOkCopy_NoAutoCopy, 0, (result3, input) =>
                                        {
                                            if (result3 != PromptResult.Success)
                                            {
                                                if (accountManager.Settings.devMode)
                                                {
                                                    Log.Write($"Signature: '{signature}'");
                                                }

                                                GUIUtility.systemCopyBuffer = signature;
                                                MessageBox(MessageKind.Default, "Signature copied to the clipboard.");
                                            }
                                        });
                                    });
                                });

                                modalHints = new Dictionary<string, string>() { { "Phantasma", "Phantasma" }, { "Ethereum", "Ethereum" }, { "Neo Legacy", "Neo Legacy" } };
                            }
                            else if (Input.GetKey(KeyCode.LeftControl))
                            {
                                ShowModal("", "Select chain", ModalState.Input, 1, 10, ModalConfirmCancel, 1, (result, chain) =>
                                {
                                    if (result == PromptResult.Failure)
                                    {
                                        return; // user cancelled
                                    }

                                    ShowModal("", "Enter message", ModalState.Input, 1, -1, ModalConfirmCancel, 4, (result2, message) =>
                                    {
                                        if (result2 == PromptResult.Failure)
                                        {
                                            return; // user cancelled
                                        }

                                        ShowModal("", "Enter signature", ModalState.Input, 1, -1, ModalConfirmCancel, 4, (result3, signature) =>
                                        {
                                            if (result3 == PromptResult.Failure)
                                            {
                                                return; // user cancelled
                                            }

                                            var messageBytes = System.Text.Encoding.ASCII.GetBytes(message);
                                            var signatureBytes = Base16.Decode(signature);

                                            if (accountManager.Settings.devMode)
                                            {
                                                var hash = Base16.Encode(Phantasma.Core.Cryptography.Hashing.SHA256.ComputeHash(messageBytes));
                                                Log.WriteRaw($"Verified message: '{message}'");
                                                Log.WriteRaw("\n\n");
                                                Log.Write($"Hash: '{hash}'");
                                                Log.Write($"Signature: '{signature}'");
                                            }

                                            var wif = AccountManager.Instance.CurrentAccount.GetWif(AccountManager.Instance.CurrentPasswordHash);
                                            var verificationResult = false;

                                            if (chain == "Phantasma")
                                            {
                                                var keys = PhantasmaKeys.FromWIF(wif);
                                                verificationResult = Ed25519.Verify(signatureBytes, messageBytes, keys.PublicKey);
                                            }
                                            else if (chain == "Ethereum")
                                            {
                                                var keys = EthereumKey.FromWIF(wif);
                                                verificationResult = ECDsa.Verify(messageBytes, signatureBytes, keys.PublicKey, ECDsaCurve.Secp256k1);
                                            }
                                            else if (chain == "Neo Legacy")
                                            {
                                                var keys = NeoKeys.FromWIF(wif);
                                                verificationResult = ECDsa.Verify(messageBytes, signatureBytes, keys.PublicKey, ECDsaCurve.Secp256r1);
                                            }
                                            else
                                            {
                                                MessageBox(MessageKind.Error, "Unsupported chain");
                                                return;
                                            }

                                            if (verificationResult)
                                            {
                                                MessageBox(MessageKind.Success, "Signature is correct");
                                            }
                                            else
                                            {
                                                MessageBox(MessageKind.Error, "Signature is incorrect");
                                            }
                                        });
                                    });
                                });

                                modalHints = new Dictionary<string, string>() { { "Phantasma", "Phantasma" }, { "Ethereum", "Ethereum" }, { "Neo Legacy", "Neo Legacy" } };
                            }
                            else
                            {
                                var signer = new ProofOfAddressesSigner(AccountManager.Instance.CurrentAccount.GetWif(AccountManager.Instance.CurrentPasswordHash));

                                ShowModal("Proof of addresses", signer.GenerateMessage(),
                                    ModalState.Message, AccountManager.MinAccountNameLength, AccountManager.MaxAccountNameLength, ModalSignCancel, 1, (result, name) =>
                                {
                                    if (result == PromptResult.Success)
                                    {
                                        var message = signer.GenerateSignedMessage();
                                        ShowModal("Signed proof of addresses", message, ModalState.Message, 0, 0, ModalSendCancel, 0, (result2, input) =>
                                        {
                                            if (result2 == PromptResult.Success)
                                            {
                                                var signedPoaBase64 = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(message));
                                                if (accountManager.Settings.devMode)
                                                {
                                                    Log.Write($"Signed POA message (Base64): '{signedPoaBase64}'");
                                                }

                                                var url = string.Format("{0}/{1}", accountManager.Settings.phantasmaPoaUrl.TrimEnd('/'), "api/v1/poa/register");
                                                
                                                var jsonMessage = "{\"message\": \"" + signedPoaBase64 + "\"}";

                                                StartCoroutine(Phantasma.SDK.WebClient.RESTRequest<string>(url, jsonMessage, false, (error, msg) =>
                                                {
                                                    MessageBox(MessageKind.Error, "Error occured. Please try later.");
                                                },
                                                (result) =>
                                                {
                                                    MessageBox(MessageKind.Default, "Message sent.");
                                                }));

                                                if (accountManager.Settings.devMode)
                                                {
                                                    GUIUtility.systemCopyBuffer = message;
                                                    MessageBox(MessageKind.Default, "Message copied to the clipboard.");
                                                }
                                            }
                                        });
                                    }
                                });
                            }
                            break;
                        }
                }
            });
        }

        private void StakeSOUL(decimal selectedAmount, string msg, Action<Hash, Phantasma.SDK.Transaction?, string> callback)
        {
            var accountManager = AccountManager.Instance;
            var state = accountManager.CurrentState;

            PromptBox(msg, ModalYesNo, (result) =>
            {
                if (result == PromptResult.Success)
                {
                    RequestKCAL("SOUL", (kcal) =>
                    {
                        if (kcal == PromptResult.Success)
                        {
                            // In case we swapped SOUL to KCAL we should check if selected amound is still available
                            // If not - reduce to balance
                            // We should update balance object first
                            var balance = AccountManager.Instance.CurrentState.balances.Where(x => x.Symbol == "SOUL").FirstOrDefault();

                            if (selectedAmount > balance.Available)
                                selectedAmount = balance.Available;

                            var address = Address.FromText(state.address);

                            var sb = new ScriptBuilder();

                            sb.AllowGas(address, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
                            sb.CallContract("stake", "Stake", address, UnitConversion.ToBigInteger(selectedAmount, balance.Decimals));
                            sb.SpendGas(address);

                            var script = sb.EndScript();

                            SendTransaction($"Stake {selectedAmount} SOUL", script, null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, null, DomainSettings.RootChainName, ProofOfWork.None, (hash, txResult, error) =>
                            {
                                callback(hash, txResult, error);
                            });
                        }
                    });
                }
            });
        }

        private string[] explorerMenu = new string[] { "ETH: Etherscan", "BSC: Bscscan", "N2: Neotube" };

        private string[] managerMenu = new string[] { "Export Private Key", "Migrate", "Set Name", "Prove addresses" };

        private GUIState[] bottomMenu = new GUIState[] { GUIState.Balances, GUIState.History, GUIState.Account, GUIState.Exit };

        private int DoBottomMenu()
        {
            int posY;
            DoButtonGrid<GUIState>(false, bottomMenu.Length, 0, 0, out posY, (index) =>
            {
                var btnKind = bottomMenu[index];
                return new MenuEntry(btnKind, btnKind.ToString(), btnKind != this.guiState);
            },
            (selected) =>
            {
                PushState(selected);
            });

            return posY;
        }

        private int DoBottomMenuForNft()
        {
            var accountManager = AccountManager.Instance;

            int posY;

            var border = Units(1);

            int panelHeight = VerticalLayout ? Border * 2 + (Units(2) + 4) * 3 : (border + Units(3));
            posY = (int)((windowRect.y + windowRect.height) - (panelHeight + border));

            var rect = new Rect(border, posY, windowRect.width - border * 2, panelHeight);

            int halfWidth = (int)(windowRect.width / 2);
            int btnWidth = VerticalLayout ? Units(7) : Units(11);

            // Close
            DoButton(true, new Rect(VerticalLayout ? rect.x + border * 2 : (halfWidth - btnWidth) / 2,
                                    VerticalLayout ? (int)rect.y + border + (Units(2) + 4) * 2 : (int)rect.y + border,
                                    VerticalLayout ? rect.width - border * 4 : btnWidth, Units(2)), "Close", () =>
            {
                PushState(GUIState.Balances);

                // Saving sorting.
                accountManager.Settings.SaveOnExit();
            });

            int pageLabelWidth = Units(4);
            int pageButtonWidth = Units(2);
            int pageButtonSpacing = 12;

            // <<
            DoButton(nftPageNumber > 0, new Rect(halfWidth - pageLabelWidth / 2 - (pageButtonWidth + pageButtonSpacing) * 2,
                                                 VerticalLayout ? (int)rect.y + border : (int)rect.y + border,
                                                 pageButtonWidth, Units(2)), "<<", () =>
            {
                nftPageNumber = 0;
            });

            // <
            DoButton(nftPageNumber > 0, new Rect(halfWidth - pageLabelWidth / 2 - (pageButtonWidth + pageButtonSpacing),
                                                 VerticalLayout ? (int)rect.y + border : (int)rect.y + border,
                                                 pageButtonWidth, Units(2)), "<", () =>
            {
                nftPageNumber--;
            });

            // Current page number
            var style = GUI.skin.GetStyle("Label");
            var prevAlignment = style.alignment;
            style.alignment = TextAnchor.MiddleCenter;

            GUI.Label(new Rect(halfWidth - pageLabelWidth / 2 - 6,
                               (int)rect.y + 12,
                               pageLabelWidth, Units(2)), (nftPageNumber + 1).ToString(), style);

            style.alignment = prevAlignment;

            // >
            DoButton(nftPageNumber < nftPageCount - 1, new Rect(halfWidth + pageLabelWidth / 2 + pageButtonSpacing,
                                                                VerticalLayout ? (int)rect.y + border : (int)rect.y + border,
                                                                pageButtonWidth, Units(2)), ">", () =>
            {
                nftPageNumber++;
            });

            // >>
            DoButton(nftPageNumber < nftPageCount - 1, new Rect(halfWidth + pageLabelWidth / 2 + pageButtonWidth + pageButtonSpacing * 2,
                                                                VerticalLayout ? (int)rect.y + border : (int)rect.y + border,
                                                                pageButtonWidth, Units(2)), ">>", () =>
            {
                nftPageNumber = nftPageCount - 1;
            });

            if (guiState != GUIState.NftView)
            {
                // To transfer list
                DoButton(nftTransferList.Count > 0, new Rect(VerticalLayout ? rect.x + border * 2 : halfWidth + (halfWidth - btnWidth) / 2,
                                        VerticalLayout ? (int)rect.y + border + (Units(2) + 4) : (int)rect.y + border,
                                        VerticalLayout ? rect.width - border * 4 : btnWidth, Units(2)), "To transfer list", () =>
                {
                    /*var nftTransferLimit = 100;
                    if (nftTransferList.Count > nftTransferLimit)
                    {
                        PromptBox($"Currently sending is limited to {nftTransferLimit} NFTs for one transfer, reduce selection to first {nftTransferLimit}? ", ModalConfirmCancel, (result) =>
                        {
                            if (result == PromptResult.Success)
                            {
                                nftTransferList.RemoveRange(nftTransferLimit, nftTransferList.Count - nftTransferLimit);
                                PushState(GUIState.NftTransferList);
                            }
                        });
                    }
                    else*/
                    {
                        PushState(GUIState.NftTransferList);
                    }
                });
            }
            else
            {
                if (transferSymbol == "TTRS")
                {
                    // Online inventory
                    DoButton(true, new Rect(VerticalLayout ? rect.x + border * 2 : halfWidth + (halfWidth - btnWidth) / 2,
                                            VerticalLayout ? (int)rect.y + border + (Units(2) + 4) : (int)rect.y + border,
                                            VerticalLayout ? rect.width - border * 4 : btnWidth, Units(2)), "Online inventory", () =>
                                            {
                                                Application.OpenURL("https://www.22series.com/inventory?#" + accountManager.GetAddress(AccountManager.Instance.CurrentIndex, AccountManager.Instance.CurrentPlatform));
                                            });
                }
                else
                {
                    // Contract information
                    DoButton(true, new Rect(VerticalLayout ? rect.x + border * 2 : halfWidth + (halfWidth - btnWidth) / 2,
                        VerticalLayout ? (int)rect.y + border + (Units(2) + 4) : (int)rect.y + border,
                        VerticalLayout ? rect.width - border * 4 : btnWidth, Units(2)), "Contract information", () =>
                        {
                            Application.OpenURL(accountManager.GetPhantasmaContractURL(transferSymbol));
                        });
                }
            }

            return posY;
        }

        private int DoBottomMenuForNftTransferList()
        {
            int posY;

            var border = Units(1);

            int panelHeight = VerticalLayout ? Border * 2 + (Units(2) + 4) * 2 : (border + Units(3));
            posY = (int)((windowRect.y + windowRect.height) - (panelHeight + border));

            var rect = new Rect(border, posY, windowRect.width - border * 2, panelHeight);

            int halfWidth = (int)(windowRect.width / 2);
            int btnWidth = VerticalLayout ? Units(7) : Units(11);

            // Back
            DoButton(true, new Rect(VerticalLayout ? rect.x + border * 2 : (halfWidth - btnWidth) / 2, VerticalLayout ? (int)rect.y + border + (Units(2) + 4) : (int)rect.y + border, VerticalLayout ? rect.width - border * 4 : btnWidth, Units(2)), "Back", () =>
            {
                PushState(GUIState.Nft);
            });

            // Burn
            DoButton(true, new Rect(VerticalLayout ? rect.x + border * 2 : halfWidth - btnWidth / 2, VerticalLayout ? (int)rect.y + border : (int)rect.y + border, VerticalLayout ? rect.width - border * 4 : btnWidth, Units(2)), "Burn", () =>
            {
                PromptBox("Are you sure you want to burn (destroy) selected NFTs?", ModalConfirmCancel, (result) =>
                {
                    if (result == PromptResult.Success)
                    {
                        var accountManager = AccountManager.Instance;
                        var state = accountManager.CurrentState;
                        
                        var gasPrice = accountManager.Settings.feePrice;
                        var gasLimit = accountManager.Settings.feeLimit;

                        byte[] script;
                        try
                        {
                            var target = Address.FromText(state.address);

                            var sb = new ScriptBuilder();
                            sb.AllowGas(target, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
                            foreach (var nftToBurn in nftTransferList)
                            {
                                sb.CallInterop("Runtime.BurnToken", target, transferSymbol, BigInteger.Parse(nftToBurn));
                            }
                            sb.SpendGas(target);
                            script = sb.EndScript();
                        }
                        catch (Exception e)
                        {
                            MessageBox(MessageKind.Error, "Something went wrong!\n" + e.Message + "\n\n" + e.StackTrace);
                            return;
                        }

                        SendTransaction($"Burn {nftTransferList.Count} {transferSymbol} NFTs", script, null, gasPrice, gasLimit * nftTransferList.Count, null, DomainSettings.RootChainName, ProofOfWork.None, (hash, txResult, error) =>
                        {
                            TxResultMessage(hash, txResult, error, $"You burned {nftTransferList.Count} NFTs!");
                        });
                    }
                }, 10);
            });

            // Send
            DoButton(nftTransferList.Count > 0, new Rect(VerticalLayout ? rect.x + border * 2 : halfWidth + (halfWidth - btnWidth) / 2, VerticalLayout ? (int)rect.y + border - (Units(2) + 4) : (int)rect.y + border, VerticalLayout ? rect.width - border * 4 : btnWidth, Units(2)), "Send", () =>
            {
                var accountManager = AccountManager.Instance;
                var state = accountManager.CurrentState;
                var transferName = $"{transferSymbol} transfer";
                Phantasma.SDK.Token transferToken;

                Tokens.GetToken(transferSymbol, accountManager.CurrentPlatform, out transferToken);

                if (string.IsNullOrEmpty(transferToken.flags))
                {
                    MessageBox(MessageKind.Error, $"Operations with token {transferSymbol} are not supported yet in this version.");
                    return;
                }

                if (!transferToken.IsTransferable())
                {
                    MessageBox(MessageKind.Error, $"Transfers of {transferSymbol} tokens are not allowed.");
                    return;
                }

                ShowModal(transferName, "Enter destination address", ModalState.Input, 3, 64, ModalConfirmCancel, 1, (result, destAddress) =>
                {
                    if (result == PromptResult.Failure)
                    {
                        return; // user canceled
                    }

                    var ethereumAddressUtil = new Poltergeist.PhantasmaLegacy.Ethereum.Util.AddressUtil();

                    if (Address.IsValidAddress(destAddress) && accountManager.CurrentPlatform.ValidateTransferTarget(transferToken, PlatformKind.Phantasma))
                    {
                        if (accountManager.CurrentPlatform == PlatformKind.Phantasma)
                        {
                            ContinuePhantasmaNftTransfer(transferName, transferSymbol, destAddress);
                        }
                        else
                        {
                            MessageBox(MessageKind.Error, $"Direct transfers from {accountManager.CurrentPlatform} to this type of address not supported.");
                        }
                    }
                    else
                    if (Poltergeist.PhantasmaLegacy.Neo2.NeoUtils.IsValidAddress(destAddress))
                    {
                        MessageBox(MessageKind.Error, $"Direct transfers from {accountManager.CurrentPlatform} to Neo address not supported.");
                    }
                    else
                    if (ethereumAddressUtil.IsValidEthereumAddressHexFormat(destAddress) && ethereumAddressUtil.IsChecksumAddress(destAddress))
                    {
                        MessageBox(MessageKind.Error, $"Direct transfers from {accountManager.CurrentPlatform} to Ethereum/BSC address not supported.");
                    }
                    else
                    if (ValidationUtils.IsValidIdentifier(destAddress) && destAddress != state.name && accountManager.CurrentPlatform.ValidateTransferTarget(transferToken, PlatformKind.Phantasma))
                    {
                        BeginWaitingModal("Looking up account name");
                        accountManager.ValidateAccountName(destAddress, (lookupAddress) =>
                        {
                            EndWaitingModal();

                            if (lookupAddress != null)
                            {
                                ContinuePhantasmaNftTransfer(transferName, transferSymbol, lookupAddress);
                            }
                            else
                            {
                                MessageBox(MessageKind.Error, "No account with such name exists.");
                            }
                        });
                    }
                    else
                    {
                        MessageBox(MessageKind.Error, "Invalid destination address.");
                    }
                });

                modalHints = GenerateAccountHints(accountManager.CurrentPlatform.GetTransferTargets(transferToken));
            });

            return posY;
        }

        private Action<Hash, Phantasma.SDK.Transaction?, string> transactionCallback;

        private void InvokeTransactionCallback(Hash hash, Phantasma.SDK.Transaction? txResult, string error)
        {
            var temp = transactionCallback;
            transactionCallback = null;
            temp?.Invoke(hash, txResult, error);
        }

        public void SendTransaction(string description, byte[] script, TransferRequest? transferRequest, BigInteger phaGasPrice, BigInteger phaGasLimit, byte[] payload, string chain, ProofOfWork PoW, Action<Hash, Phantasma.SDK.Transaction?, string> callback)
        {
            if (script == null && transferRequest == null)
            {
                MessageBox(MessageKind.Error, "Null transaction script and request", () =>
                {
                    callback(Hash.Null, null, "Null transaction scrip and request");
                });
            }

            var accountManager = AccountManager.Instance;
            if (accountManager.CurrentPlatform == PlatformKind.Phantasma)
            {
                BigInteger usedGas;

                try
                {
                    var vm = new GasMachine(script, 0, null);
                    var result = vm.Execute();
                    usedGas = vm.UsedGas;
                }
                catch
                {
                    usedGas = 400;
                }

                var estimatedFee = usedGas * phaGasPrice;
                var feeDecimals = Tokens.GetTokenDecimals("KCAL", accountManager.CurrentPlatform);
                description += $"\nEstimated fee: {UnitConversion.ToDecimal(estimatedFee, feeDecimals)} KCAL";
            }

            RequestPassword(description, accountManager.CurrentPlatform, false, false, (auth) =>
            {
                if (auth == PromptResult.Success)
                {
                    Animate(AnimationDirection.Right, true, () =>
                    {
                        Animate(AnimationDirection.Left, false, () =>
                        {
                            PromptBox($"Preparing transaction...\n{description}", ModalSendCancel, (result) =>
                            {
                                if (result == PromptResult.Success)
                                {
                                    PushState(GUIState.Sending);

                                    accountManager.SignAndSendTransaction(chain, script, transferRequest, phaGasPrice, phaGasLimit, payload, PoW, null, (hash, error) =>
                                    {
                                        if (string.IsNullOrEmpty(error))
                                        {
                                            ShowConfirmationScreen(hash, true, callback);
                                        }
                                        else
                                        {
                                            PopState();

                                            callback(Hash.Null, null, "Cannot send transaction. Details:\n" + error);
                                        }
                                    });
                                }
                                else
                                {
                                    callback(Hash.Null, null, null); // User cancelled tx
                                };
                            });
                        });
                    });
                }
                else
                if (auth == PromptResult.Failure)
                {
                    MessageBox(MessageKind.Error, $"Authorization failed.", () =>
                    {
                        callback(Hash.Null, null, "Authorization failed.");
                    });
                }
            });
        }

        public void SendPhaTransactions(string description, List<byte[]> scripts, BigInteger gasPrice, BigInteger gasLimit, byte[] payload, string chain, ProofOfWork PoW, Action<Hash, Phantasma.SDK.Transaction?, string> callback)
        {
            if (scripts.Count() == 0)
            {
                MessageBox(MessageKind.Error, "Null transaction script", () =>
                {
                    callback(Hash.Null, null, "Null transaction script");
                });
            }

            var accountManager = AccountManager.Instance;

            BigInteger usedGas = 0;

            foreach (var script in scripts)
            {
                try
                {
                    var vm = new GasMachine(script, 0, null);
                    var result = vm.Execute();
                    usedGas += vm.UsedGas;
                }
                catch
                {
                    usedGas += 400;
                }
            }

            var estimatedFee = usedGas * gasPrice;
            var feeDecimals = Tokens.GetTokenDecimals("KCAL", accountManager.CurrentPlatform);
            description += $"\nEstimated fee: {UnitConversion.ToDecimal(estimatedFee, feeDecimals)} KCAL";

            RequestPassword(description, accountManager.CurrentPlatform, false, false, (auth) =>
            {
                if (auth == PromptResult.Success)
                {
                    Animate(AnimationDirection.Right, true, () =>
                    {
                        Animate(AnimationDirection.Left, false, () =>
                        {
                            PromptBox(scripts.Count() > 1 ? $"Preparing {scripts.Count()} transactions...\n{description}" : $"Preparing transaction...\n{description}", ModalSendCancel, (result) =>
                            {
                                if (result == PromptResult.Success)
                                {
                                    SendTransactionsInternal(accountManager, description, scripts, gasPrice, gasLimit, payload, chain, PoW, callback);
                                }
                                else
                                {
                                    callback(Hash.Null, null, null); // Cancelled by user
                                };
                            });
                        });
                    });
                }
                else
                if (auth == PromptResult.Failure)
                {
                    MessageBox(MessageKind.Error, $"Authorization failed.", () =>
                    {
                        callback(Hash.Null, null, "Authorization failed.");
                    });
                }
            });
        }

        private void SendTransactionsInternal(AccountManager accountManager, string description, List<byte[]> scripts, BigInteger gasPrice, BigInteger gasLimit, byte[] payload, string chain, ProofOfWork PoW, Action<Hash, Phantasma.SDK.Transaction?, string> callback)
        {
            PushState(GUIState.Sending);

            accountManager.SignAndSendTransaction(chain, scripts[0], null, gasPrice, gasLimit, payload, PoW, null, (hash, error) =>
            {
                if (string.IsNullOrEmpty(error) && hash != Hash.Null)
                {
                    if (scripts.Count() > 1)
                    {
                        ShowConfirmationScreen(hash, false, (txHash, txResult, error) =>
                        {
                            if (string.IsNullOrEmpty(error))
                            {
                                SendTransactionsInternal(accountManager, description, scripts.Skip(1).ToList(), gasPrice, gasLimit, payload, chain, PoW, callback);
                            }
                        });
                    }
                    else
                    {
                        // Finishing, last script.
                        ShowConfirmationScreen(hash, true, callback);
                    }
                }
                else
                {
                    PopState();

                    MessageBox(MessageKind.Error, $"Error sending transaction.\n{error}", () =>
                    {
                        callback(Hash.Null, null, error);
                    });
                }
            });
        }

        private void ShowConfirmationScreen(Hash hash, bool refreshBalanceAfterConfirmation, Action<Hash, Phantasma.SDK.Transaction?, string> callback)
        {
            transactionCallback = callback;
            transactionStillPending = true;
            transactionCheckCount = 0;
            transactionHash = hash;
            transactionLastCheck = DateTime.UtcNow;
            this.refreshBalanceAfterConfirmation = refreshBalanceAfterConfirmation;

            if (guiState == GUIState.Sending)
            {
                SetState(GUIState.Confirming);
            }
            else
            {
                PushState(GUIState.Confirming);
            }
        }

#region transfers
        private void ContinuePhantasmaTransfer(string transferName, string symbol, string destAddress)
        {
            var accountManager = AccountManager.Instance;
            var state = accountManager.CurrentState;

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                MessageBox(MessageKind.Error, $"Current platform must be " + PlatformKind.Phantasma);
                return;
            }

            var source = Address.FromText(state.address);
            var destination = Address.FromText(destAddress);

            if (source == destination)
            {
                MessageBox(MessageKind.Error, $"Source and destination address must be different!");
                return;
            }

            var balance = state.GetAvailableAmount(symbol);
            RequireAmount(transferName, destAddress, symbol, 0.001m, balance, (amount) =>
            {
                RequestKCAL(symbol, (feeResult) =>
                {
                    if (feeResult == PromptResult.Success)
                    {
                        // In case we swapped SOUL to KCAL we should check if selected amound is still available
                        // If not - reduce to balance
                        // We should update balance first
                        balance = AccountManager.Instance.CurrentState.GetAvailableAmount(symbol);
                        if (amount > balance && !(accountManager.Settings.devMode && accountManager.Settings.devMode_NoValidation))
                            amount = balance;

                        byte[] script;

                        try
                        {
                            var decimals = Tokens.GetTokenDecimals(symbol, accountManager.CurrentPlatform);

                            var sb = new ScriptBuilder();
                            sb.AllowGas(source, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);

                            if (symbol == "KCAL" && amount == balance)
                            {
                                sb.TransferBalance(symbol, source, destination);
                            }
                            else
                            {
                                sb.TransferTokens(symbol, source, destination, UnitConversion.ToBigInteger(amount, decimals));
                            }

                            sb.SpendGas(source);
                            script = sb.EndScript();
                        }
                        catch (Exception e)
                        {
                            MessageBox(MessageKind.Error, "Something went wrong!\n" + e.Message + "\n\n" + e.StackTrace);
                            return;
                        }

                        SendTransaction($"Transfer {MoneyFormat(amount, MoneyFormatType.Long)} {symbol}\nDestination: {destination}", script, null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, null, DomainSettings.RootChainName, ProofOfWork.None, (hash, txResult, error) =>
                        {
                            TxResultMessage(hash, txResult, error, $"You transferred {MoneyFormat(amount, MoneyFormatType.Long)} {symbol}!\n\nThe transaction has successfully completed, but it may take up to 30 seconds until the change is reflected in your wallet balance\n");
                        });
                    }
                    else
                    if (feeResult == PromptResult.Failure)
                    {
                        MessageBox(MessageKind.Error, $"KCAL is required to make transactions!");
                    }
                });
            });
        }

        public static IEnumerable<List<T>> SplitList<T>(List<T> locations, int nSize = 30)
        {
            for (int i = 0; i < locations.Count; i += nSize)
            {
                yield return locations.GetRange(i, Math.Min(nSize, locations.Count - i));
            }
        }
        private void ContinuePhantasmaNftTransfer(string transferName, string symbol, string destAddress)
        {
            var accountManager = AccountManager.Instance;
            var state = accountManager.CurrentState;

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                MessageBox(MessageKind.Error, $"Current platform must be " + PlatformKind.Phantasma);
                return;
            }

            var source = Address.FromText(state.address);
            var destination = Address.FromText(destAddress);

            if (source == destination)
            {
                MessageBox(MessageKind.Error, $"Source and destination address must be different!");
                return;
            }

            var balance = state.GetAvailableAmount(symbol);
            var amount = nftTransferList.Count;
            RequestKCAL(symbol, (feeResult) =>
            {
                if (feeResult == PromptResult.Success)
                {
                    var scripts = new List<byte[]>();
                    string description;

                    var gasPrice = accountManager.Settings.feePrice;
                    var gasLimit = accountManager.Settings.feeLimit;
                    
                    try
                    {
                        var nftTransferLimit = 100;
                        var nftSublists = SplitList<string>(nftTransferList, nftTransferLimit).ToArray();

                        description = $"Transfer {symbol} NFTs\n";

                        foreach (var nftSublist in nftSublists)
                        {
                            var decimals = Tokens.GetTokenDecimals(symbol, accountManager.CurrentPlatform);

                            var sb = new ScriptBuilder();
                            sb.AllowGas(source, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);

                            foreach (var nft in nftSublist)
                            {
                                sb.TransferNFT(symbol, source, destination, BigInteger.Parse(nft));

                                string nftDescription = "";
                                if (symbol == "TTRS")
                                {
                                    var item = TtrsStore.GetNft(nft);

                                    if (item.item_info.name_english != null)
                                        nftDescription = " " + ((item.item_info.name_english.Length > 25) ? item.item_info.name_english.Substring(0, 22) + "..." : item.item_info.name_english);

                                    nftDescription += " Minted " + item.timestamp.ToString("dd.MM.yy") + " #" + item.mint;
                                }

                                description += $"#{nft.Substring(0, 5) + "..." + nft.Substring(nft.Length - 5)}{nftDescription}\n";
                            }

                            gasLimit *= nftSublist.Count;

                            sb.SpendGas(source);
                            scripts.Add(sb.EndScript());
                        }

                        description += $"to {destination}.";
                    }
                    catch (Exception e)
                    {
                        MessageBox(MessageKind.Error, "Something went wrong!\n" + e.Message + "\n\n" + e.StackTrace);
                        return;
                    }

                    SendPhaTransactions(description, scripts, gasPrice, gasLimit, null, DomainSettings.RootChainName, ProofOfWork.None, (hash, txResult, error) =>
                    {
                        if (string.IsNullOrEmpty(error) && hash != Hash.Null)
                        {
                            TxResultMessage(hash, txResult, error, $"You transferred {MoneyFormat(amount, MoneyFormatType.Long)} {symbol}!\n\nThe transaction has successfully completed, but it may take up to 30 seconds until the change is reflected in your wallet balance\n");

                            // Removing sent NFTs from current NFT list.
                            var nfts = accountManager.CurrentNfts;
                            foreach (var nft in nftTransferList)
                            {
                                nfts.Remove(nfts.Find(x => x.ID == nft));
                            }

                            // Returning to NFT's first screen.
                            nftScroll = Vector2.zero;
                            nftTransferList.Clear();
                            PushState(GUIState.Nft);
                        }
                        else
                        {
                            TxResultMessage(hash, txResult, error, null, "Some or all transactions failed.");
                        }
                    });
                }
                else
                if (feeResult == PromptResult.Failure)
                {
                    MessageBox(MessageKind.Error, $"KCAL is required to make transactions!");
                }
            });
        }

        private bool ValidDecimals(decimal amount, string symbol)
        {
            var decimals = Tokens.GetTokenDecimals(symbol, AccountManager.Instance.CurrentPlatform);

            if (decimals > 0)
            {
                return true;
            }

            var temp = amount - (long)amount;
            return temp == 0;
        }

        private void RequireAmount(string description, string destination, string symbol, decimal min, decimal max, Action<decimal> callback)
        {
            var accountManager = AccountManager.Instance;
            var state = accountManager.CurrentState;
            var caption = $"Enter {symbol} amount:\nMax: {MoneyFormat(max, MoneyFormatType.Long)} {symbol}";
            if (symbol == "GAS" && accountManager.CurrentPlatform == PlatformKind.Phantasma && destination == null)
            {
                caption += "\nWarning: Swapping back consumes GAS (around 0.1) so if your GAS balance falls below that, swap back to NEO will fail.";
            }

            if (!string.IsNullOrEmpty(destination))
            {
                caption += $"\nDestination: {destination}";
            }

            ShowModal(description, caption, ModalState.Input, 1, 64, ModalConfirmCancel, 1, (result, temp) =>
            {
                if (result == PromptResult.Failure)
                {
                    return; // user cancelled
                }

                decimal amount = ParseNumber(temp);

                if(accountManager.Settings.devMode && accountManager.Settings.devMode_NoValidation)
                {
                    callback(amount);
                    return;
                }

                if (amount > 0 && ValidDecimals(amount, symbol))
                {
                    if (amount > max)
                    {
                        MessageBox(MessageKind.Error, $"Not enough {symbol}!");
                        return;
                    }
                    else
                    if (amount < min)
                    {
                        MessageBox(MessageKind.Error, $"Amount is too small.\nMinimum accepted is {min} {symbol}!");
                        return;
                    }
                    else
                    {
                        callback(amount);
                    }
                }
                else
                {
                    MessageBox(MessageKind.Error, "Invalid amount!");
                    return;
                }
            });

            modalHints = new Dictionary<string, string>() { { $"Max ({MoneyFormat(max, MoneyFormatType.Short)} {symbol})", max.ToString() } };
        }

        private void RequestKCAL(string swapSymbol, Action<PromptResult> callback)
        {
            RequestFee(swapSymbol, "KCAL", 0.1m, callback);
        }

        private void RequestFee(string swapSymbol, string feeSymbol, decimal min, Action<PromptResult> callback)
        {
            var accountManager = AccountManager.Instance;
            var state = accountManager.CurrentState;

            if (swapSymbol == "NEO")
            {
                swapSymbol = "GAS";
            }

            decimal feeBalance = state.GetAvailableAmount(feeSymbol);

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                callback(feeBalance >= min ? PromptResult.Success : PromptResult.Failure);
                return;
            }

            if (swapSymbol == feeSymbol)
            {
                callback(PromptResult.Success);
                return;
            }

            if (feeBalance >= min)
            {
                callback(PromptResult.Success);
                return;
            }

            if (swapSymbol == null)
            {
                MessageBox(MessageKind.Error, $"Not enough {feeSymbol} for transaction fees.", () =>
                {
                    callback(PromptResult.Failure);
                });                
                return;
            }

            var swapDecimals = Tokens.GetTokenDecimals(swapSymbol, accountManager.CurrentPlatform);
            decimal swapBalance = state.GetAvailableAmount(swapSymbol);

            if (Tokens.GetToken(swapSymbol, accountManager.CurrentPlatform, out var tokenInfo))
            {
                if(!tokenInfo.IsFungible())
                {
                    // We cannot swap NFTs.
                    MessageBox(MessageKind.Error, $"Not enough {feeSymbol} for transaction fees.");
                    return;
                }
            }

            if (swapDecimals> 0 || swapBalance > 1)
            {
                PromptBox($"Not enough {feeSymbol} for transaction fees.\nUse some {swapSymbol} to perform a cosmic swap?", ModalYesNo,
                     (result) =>
                     {
                         if (result == PromptResult.Success)
                         {
                             byte[] script;

                             try
                             {
                                 var source = Address.FromText(state.address);

                                 var decimals = Tokens.GetTokenDecimals(feeSymbol, accountManager.CurrentPlatform);
                                 var decimalsSwap = Tokens.GetTokenDecimals(swapSymbol, accountManager.CurrentPlatform);

                                 var sb = new ScriptBuilder();
                                 if (feeSymbol == "KCAL")
                                 {
                                     sb.CallContract("swap", "SwapFee", source, swapSymbol, UnitConversion.ToBigInteger(1m, decimals));
                                 }
                                 else
                                 {
                                     sb.CallContract("swap", "SwapReverse", source, swapSymbol, feeSymbol, UnitConversion.ToBigInteger(1m, decimals));
                                 }
                                 sb.AllowGas(source, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);
                                 sb.SpendGas(source);
                                 script = sb.EndScript();
                             }
                             catch (Exception e)
                             {
                                 MessageBox(MessageKind.Error, "Something went wrong!\n" + e.Message + "\n\n" + e.StackTrace);
                                 return;
                             }

                             var swapSymbolBalance = AccountManager.Instance.CurrentState.GetAvailableAmount(swapSymbol);
                             var feeSymbolBalance = AccountManager.Instance.CurrentState.GetAvailableAmount(feeSymbol);
                             Log.Write($"Balance before swap: {swapSymbol}: {swapSymbolBalance}, {feeSymbol}: {feeSymbolBalance}.");
                             SendTransaction($"Swap {swapSymbol} for {feeSymbol}", script, null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit, null, DomainSettings.RootChainName, ProofOfWork.None, (hash, txResult, error) =>
                             {
                                 if (!string.IsNullOrEmpty(error) || hash == Hash.Null)
                                 {
                                     callback(PromptResult.Failure);
                                 }
                                 else
                                 {
                                     // We should check if balance is properly updated,
                                     // to prevent further potential errors.
                                     var swapSymbolBalanceNew = AccountManager.Instance.CurrentState.GetAvailableAmount(swapSymbol);
                                     var feeSymbolBalanceNew = AccountManager.Instance.CurrentState.GetAvailableAmount(feeSymbol);

                                     if (swapSymbolBalance == swapSymbolBalanceNew || feeSymbolBalance == feeSymbolBalanceNew)
                                     {
                                         Log.Write($"Balance is not refreshed properly, #1. {swapSymbol}: {swapSymbolBalanceNew}, {feeSymbol}: {feeSymbolBalanceNew}");
                                         // Balance is not refreshed properly, retrying.
                                         Thread.Sleep(2000);
                                         accountManager.RefreshBalances(true, accountManager.CurrentPlatform, () =>
                                         {
                                             swapSymbolBalanceNew = AccountManager.Instance.CurrentState.GetAvailableAmount(swapSymbol);
                                             feeSymbolBalanceNew = AccountManager.Instance.CurrentState.GetAvailableAmount(feeSymbol);

                                             if (swapSymbolBalance == swapSymbolBalanceNew || feeSymbolBalance == feeSymbolBalanceNew)
                                             {
                                                 Log.Write($"Balance is not refreshed properly, #2. {swapSymbol}: {swapSymbolBalanceNew}, {feeSymbol}: {feeSymbolBalanceNew}");
                                                 // Still not updated, waiting another 4 seconds.
                                                 Thread.Sleep(4000);
                                                 accountManager.RefreshBalances(true, accountManager.CurrentPlatform, () =>
                                                 {
                                                     swapSymbolBalanceNew = AccountManager.Instance.CurrentState.GetAvailableAmount(swapSymbol);
                                                     feeSymbolBalanceNew = AccountManager.Instance.CurrentState.GetAvailableAmount(feeSymbol);

                                                     if (swapSymbolBalance == swapSymbolBalanceNew || feeSymbolBalance == feeSymbolBalanceNew)
                                                     {
                                                         Log.Write($"Balance is not refreshed properly, #3. {swapSymbol}: {swapSymbolBalanceNew}, {feeSymbol}: {feeSymbolBalanceNew}");
                                                         // Still not updated, aborting.

                                                         MessageBox(MessageKind.Error, "Cannot update balance after cosmic swap.\nPlease try again later.");
                                                         return;
                                                     }
                                                     else
                                                     {
                                                         // Balance updated after swap.
                                                         callback(PromptResult.Success);
                                                     }
                                                 });
                                             }
                                             else
                                             {
                                                 // Balance updated after swap.
                                                 callback(PromptResult.Success);
                                             }
                                         });
                                     }
                                     else
                                     {
                                         // Balance updated after swap.
                                         callback(PromptResult.Success);
                                     }
                                 }
                             });
                         }
                         else
                         {
                             callback(result);
                         }
                     });
            }
            else
            {
                MessageBox(MessageKind.Error, $"Not enough {feeSymbol} for transaction fees.\nHowever to use {swapSymbol} cosmic swaps, you need at least 2 {swapSymbol}.");
            }
        }
        #endregion

        private Dictionary<string, string> GenerateAccountHints(PlatformKind targets)
        {
            var accountManager = AccountManager.Instance;
            var hints = new Dictionary<string, string>();

            hints["Scan QR"] = $"|{GUIState.ScanQR}";

            // Adding this account addresses at the beggining of item list.
            var platformsForCurrentAccount = accountManager.CurrentAccount.platforms.Split();

            foreach (var platform in platformsForCurrentAccount)
            {
                if (platform == accountManager.CurrentPlatform)
                {
                    continue;
                }
            }

            for (int index=0; index< accountManager.Accounts.Count(); index++)
            {
                var account = accountManager.Accounts[index];
                var platforms = account.platforms.Split();

                foreach (var platform in platforms)
                {
                    if (account.name == accountManager.CurrentAccount.name)
                    {
                        continue;
                    }

                    if (targets.HasFlag(platform))
                    {
                        if(accountManager.CurrentPlatform == PlatformKind.Ethereum && platform == PlatformKind.Phantasma ||
                            accountManager.CurrentPlatform == PlatformKind.BSC && platform == PlatformKind.Phantasma ||
                            accountManager.CurrentPlatform == PlatformKind.Neo && platform == PlatformKind.Phantasma ||
                            accountManager.CurrentPlatform == PlatformKind.Phantasma && platform != PlatformKind.Phantasma)
                        {
                            // In Poltergeist we support swaps only within same account.
                            continue;
                        }
                        var addr = accountManager.GetAddress(index, platform);
                        if (!string.IsNullOrEmpty(addr))
                        {
                            var shortenedPlatform = platform.ToString();
                            switch (platform)
                            {
                                case PlatformKind.Phantasma:
                                    shortenedPlatform = "Pha";
                                    break;
                                case PlatformKind.Ethereum:
                                    shortenedPlatform = "Eth";
                                    break;
                            }
                            var key = $"{account.name} [{shortenedPlatform}]";
                            hints[key] = addr;
                        }
                    }
                }
            }

            return hints;
        }

#region QR CODES
        public Texture2D GenerateQR(string text)
        {
            var encoded = new Texture2D(256, 256);
            var color32 = EncodeQR(text, encoded.width, encoded.height);
            encoded.SetPixels32(color32);
            encoded.Apply();
            return encoded;
        }

        private static Color32[] EncodeQR(string textForEncoding, int width, int height)
        {
            var writer = new BarcodeWriter
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions
                {
                    Height = height,
                    Width = width
                }
            };
            return writer.Write(textForEncoding);
        }
#endregion

        private decimal ParseNumber(string s)
        {
            s = s.Trim().Replace(" ", "").Replace("_", "");
            s = s.Replace(",", CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator);
            decimal result;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
            {
                return result;
            }

            return -1;
        }

        static string BytesToString(long byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
            if (byteCount == 0)
                return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num).ToString() + suf[place];
        }

#region UI THREAD UTILS
        private List<Action> _uiCallbacks = new List<Action>();

        public void CallOnUIThread(Action callback)
        {
            lock (_uiCallbacks)
            {
                _uiCallbacks.Add(callback);
            }
        }
#endregion

#region DAPP Interface
        public Address GetAddress()
        {
            return Address.FromText(AccountManager.Instance.CurrentState.address);
        }

        public Dictionary<string, decimal>  GetBalances(string chain)
        {
            throw new NotImplementedException();
        }

        public void InvokeScript(string chain, byte[] script, Action<string[], string> callback)
        {
            if (script == null || script.Length == 0)
            {
                callback(null, $"Error invoking script. Script is null.");
            }

            var accountManager = AccountManager.Instance;

            accountManager.InvokeScript(chain, script, (results, error) =>
            {
                if (String.IsNullOrEmpty(error))
                {
                    callback(results, null);
                }
                else
                {
                    callback(null, $"Error invoking script.\n{error}\nScript: {System.Text.Encoding.UTF8.GetString(script)}");
                }
            });
        }

        public void WriteArchive(Hash hash, int blockIndex, byte[] data, Action<bool, string> callback)
        {
            if (data == null || data.Length == 0)
            {
                callback(false, $"Error writing archive. No data available.");
            }

            var accountManager = AccountManager.Instance;

            accountManager.WriteArchive(hash, blockIndex, data, (result, error) =>
            {
                callback(result, error);
            });
        }
#endregion
    }

}
