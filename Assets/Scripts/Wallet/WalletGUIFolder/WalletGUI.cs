using System;
using System.Linq;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

using ZXing;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Cryptography.Extensions;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Unity.Core.Logging;
using PhantasmaPhoenix.Unity.Core;
using PhantasmaPhoenix.NFT.Extensions;
using PhantasmaPhoenix.Core.Extensions;
using Poltergeist.Wallet;

namespace Poltergeist
{
    public partial class WalletGUI : MonoBehaviour, IWalletTransactionUi
    {
        private static WalletApplicationContext SharedContext => WalletApplicationContext.Instance;

        public static void MessageForUser(string message, string title = "Warning", MessageKind kind = MessageKind.Default)
        {
            SharedContext.Messages.Push(message, title, kind);
        }

        public Font monoFont;
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

        private WalletNavigation navigation;
        private WalletMessageQueue messageQueue;
        private WalletUserMessage? activeUserMessage;
        private bool activeUserMessageLogged;
        private WalletModalService modalService;
        private WalletModalActions modalActions;
        private WalletDataProvider dataProvider;
        private WalletBalancePresenter balancePresenter;
        private WalletHistoryPresenter historyPresenter;
        private WalletAccountHintsService accountHintsService;
        private WalletQrCodeGenerator qrCodeGenerator;
        private WalletTransferService transferService;
        private BalanceViewRenderer balanceRenderer;
        private HistoryViewRenderer historyRenderer;
        private NftListRenderer nftRenderer;
        private NftTransferListRenderer nftTransferRenderer;
        private WalletFeeService feeService;
        private WalletFeeRequirement feeRequirement;
        private WalletStakeService stakeService;
        private WalletBurnService burnService;
        private WalletNftTransferService nftTransferService;
        private WalletAccountAdminService accountAdminService;
        private WalletNftPresenter nftViewPresenter;
        private WalletNftTransactionBuilder nftTxBuilder;
        private WalletTransactionOrchestrator transactionOrchestrator;
        private WalletUiSignals uiSignals;
        private WalletSettingsService settingsService;
        private WalletBalanceViewSnapshot balancesSnapshot;
        private WalletHistoryViewSnapshot historySnapshot;
        private readonly Dictionary<string, WalletNftViewSnapshot> nftViewSnapshots = new Dictionary<string, WalletNftViewSnapshot>();
        private readonly HashSet<string> dirtyNftSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool balancesDirty = true;
        private bool historyDirty = true;
        private bool signalsSubscribed;
        private GUIState CurrentState => navigation.CurrentState;

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
        private ComboBox nftTypeComboBox = new ComboBox();
        private ComboBox nftRarityComboBox = new ComboBox();
        private ComboBox nftMintedComboBox = new ComboBox();

        // NFT pagination and selection.
        private List<TokenDataResult> nftFilteredList = new List<TokenDataResult>(); // List of displayed NFT items (after applying filters).

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
            passwordModeComboBox.ResetState();
            logLevelComboBox.ResetState();
            uiThemeComboBox.ResetState();
            nftSortModeComboBox.ResetState();
            nftTypeComboBox.ResetState();
            nftRarityComboBox.ResetState();
            nftMintedComboBox.ResetState();
        }

        private void MarkBalancesDirty()
        {
            balancesDirty = true;
        }

        private void MarkHistoryDirty()
        {
            historyDirty = true;
        }

        private void MarkNftDirty(string symbol = null)
        {
            symbol ??= transferSymbol;
            if (string.IsNullOrEmpty(symbol))
            {
                return;
            }

            dirtyNftSymbols.Add(symbol);
        }

        private WalletBalanceViewSnapshot GetBalancesSnapshot()
        {
            if (balancesDirty || balancesSnapshot == null)
            {
                Log.Write($"[GUI] Fetch balances snapshot dirty={balancesDirty} currentPlatform={AccountManager.Instance?.CurrentPlatform}"); //TODO Check if still needed once refactoring is over
                balancesSnapshot = balancePresenter.BuildSnapshot();
                balancesDirty = false;
            }

            return balancesSnapshot;
        }

        private WalletHistoryViewSnapshot GetHistorySnapshot()
        {
            if (historyDirty || historySnapshot == null)
            {
                Log.Write($"[GUI] Fetch history snapshot dirty={historyDirty} currentPlatform={AccountManager.Instance?.CurrentPlatform}"); //TODO Check if still needed once refactoring is over
                historySnapshot = historyPresenter.BuildSnapshot();
                historyDirty = false;
            }

            return historySnapshot;
        }

        private WalletNftViewSnapshot GetNftViewSnapshot(string symbol)
        {
            symbol ??= transferSymbol;
            if (string.IsNullOrEmpty(symbol))
            {
                Log.Write("[GUI] Build NFT snapshot with empty symbol"); //TODO Check if still needed once refactoring is over
                return nftViewPresenter.BuildSnapshot(string.Empty);
            }

            if (dirtyNftSymbols.Contains(symbol) || !nftViewSnapshots.TryGetValue(symbol, out var snapshot))
            {
                Log.Write($"[GUI] Build NFT snapshot symbol={symbol} dirty={dirtyNftSymbols.Contains(symbol)}"); //TODO Check if still needed once refactoring is over
                snapshot = nftViewPresenter.BuildSnapshot(symbol);
                nftViewSnapshots[symbol] = snapshot;
                dirtyNftSymbols.Remove(symbol);
            }

            return snapshot;
        }

        private void ResetSnapshots()
        {
            balancesSnapshot = null;
            historySnapshot = null;
            nftViewSnapshots.Clear();
            dirtyNftSymbols.Clear();
            MarkBalancesDirty();
            MarkHistoryDirty();
        }

        private bool ShouldHandlePlatform(PlatformKind platform)
        {
            var accountManager = AccountManager.Instance;
            return accountManager != null && accountManager.CurrentPlatform == platform;
        }

        public static int Units(int n)
        {
            return 16 * n;
        }

        private void Awake()
        {
            Instance = this;
            var context = WalletApplicationContext.Instance;
            navigation = context.Navigation;
            messageQueue = context.Messages;
            modalService = new WalletModalService(context.Modals);
            modalActions = new WalletModalActions(modalService, () => VerticalLayout, ResetModalUiHints, context.AmountValidator);
            dataProvider = context.Data;
            balancePresenter = context.BalancePresenter;
            historyPresenter = context.HistoryPresenter;
            accountHintsService = context.AccountHintsService;
            qrCodeGenerator = context.QrCodeGenerator;
            transferService = context.TransferService;
            balanceRenderer = new BalanceViewRenderer(this);
            historyRenderer = new HistoryViewRenderer(this);
            nftRenderer = new NftListRenderer(this);
            nftTransferRenderer = new NftTransferListRenderer(this);
            feeService = context.FeeService;
            feeRequirement = context.FeeRequirement;
            stakeService = context.StakeService;
            burnService = context.BurnService;
            nftTransferService = context.NftTransferService;
            accountAdminService = context.AccountAdminService;
            nftViewPresenter = context.NftViewPresenter;
            nftTxBuilder = context.NftTransactions;
            transactionOrchestrator = new WalletTransactionOrchestrator(() => AccountManager.Instance, this);
            uiSignals = context.UiSignals;
            settingsService = context.SettingsService;

            ResetSnapshots();
            SubscribeToSignals();
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
                       "Wallet version: " + UnityEngine.Application.version + $" built on: {Poltergeist.Build.Info.Instance.BuildTime} UTC\n" +
                       "Log level: " + _logLevel.ToString());

            Cache.Init("cache");

            initialized = false;

            navigation.Reset(GUIState.Loading);

            Log.Write(Screen.width + " x " + Screen.height);
            settingsOptions = new WalletSettingsOptions(AccountManager.Instance);

            // We will use this RawImage object to set/change background image.
            background = GameObject.Find("Background").GetComponent<RawImage>();
        }

        void OnEnable()
        {
            Application.logMessageReceived += LogCallback;
            SubscribeToSignals();
            Log.Write("[GUI] OnEnable"); //TODO Check if still needed once refactoring is over
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

            UnsubscribeFromSignals();
            Log.Write("[GUI] OnDisable"); //TODO Check if still needed once refactoring is over
        }

        #region UTILS
        private void PushState(GUIState state)
        {
            var previousState = CurrentState;

            if (state == GUIState.Exit)
            {
                ApplyStateChange(previousState, state);
                return;
            }

            previousState = navigation.MoveTo(state, CurrentState != GUIState.Loading);
            ApplyStateChange(previousState, state);
        }

        private void SetState(GUIState state)
        {
            var previousState = CurrentState;

            if (state == GUIState.Exit)
            {
                ApplyStateChange(previousState, state);
                return;
            }

            previousState = navigation.Replace(state);
            ApplyStateChange(previousState, state);
        }

        private void ApplyStateChange(GUIState previousState, GUIState newState)
        {
            ResetAllCombos();
            // Clear cached snapshots when navigating to ensure fresh data on next paint.
            switch (previousState)
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

                case GUIState.MessageForUser:
                    activeUserMessage = null;
                    activeUserMessageLogged = false;
                    break;
            }

            if (newState == GUIState.Exit)
            {
                CloseCurrentStack();
                return;
            }

            var accountManager = AccountManager.Instance;

            currentTitle = null;

            switch (newState)
            {
                case GUIState.Fatal:
                    currentTitle = "Fatal Error";
                    break;

                case GUIState.MessageForUser:
                    currentTitle = activeUserMessage?.Title ?? "Message";
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
                    balancePresenter.State.Scroll = Vector2.zero;
                    MarkBalancesDirty();

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
                    nftViewPresenter.ResetSorting();
                    MarkNftDirty(transferSymbol);
                    break;

                case GUIState.NftTransferList:
                    currentTitle = transferSymbol + " NFTs transfer list for " + accountManager.CurrentAccount.name;
                    nftTransferListScroll = Vector2.zero;
                    MarkNftDirty(transferSymbol);
                    break;

                case GUIState.History:
                    currentTitle = "History for " + accountManager.CurrentAccount.name;
                    historyPresenter.State.Scroll = Vector2.zero;
                    MarkHistoryDirty();

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
                            var tex = qrCodeGenerator.Generate($"{platform.ToString().ToLower()}://{address}");
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
                        settingsOptions ??= new WalletSettingsOptions(accountManager);
                        settingsOptions.RefreshCurrencyOptions();

                        if (accountManager.Settings.nexusKind == NexusKind.Unknown)
                        {
                            currentTitle = "Wallet Setup";
                        }
                        else if (accountManager.Settings.settingRequireReconfiguration)
                        {
                            currentTitle = "Wallet Setup (Connection failed)";
                        }
                        else
                        {
                            currentTitle = "Settings";
                        }

                        settingsScroll = Vector2.zero;
                        currencyIndex = settingsOptions.GetCurrencyIndex(accountManager.Settings.currency);
                        currencyComboBox.SelectedItemIndex = currencyIndex;

                        nexusIndex = settingsOptions.GetNexusIndex(accountManager.Settings.nexusKind);
                        nexusComboBox.SelectedItemIndex = nexusIndex;

                        mnemonicPhraseLengthIndex = settingsOptions.GetMnemonicIndex(accountManager.Settings.mnemonicPhraseLength);
                        mnemonicPhraseLengthComboBox.SelectedItemIndex = mnemonicPhraseLengthIndex;

                        passwordModeIndex = settingsOptions.GetPasswordModeIndex(accountManager.Settings.passwordMode);
                        passwordModeComboBox.SelectedItemIndex = passwordModeIndex;

                        logLevelIndex = settingsOptions.GetLogLevelIndex(accountManager.Settings.logLevel);
                        logLevelComboBox.SelectedItemIndex = logLevelIndex;

                        uiThemeIndex = settingsOptions.GetUiThemeIndex(accountManager.Settings.uiThemeName);
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
            if (modalContext.Redirected)
            {
                modalContext.Redirected = false;
            }

            // We don't have any states left,
            // most likely we have been interrupted during wallet initialization
            // and now we should continue.
            var previousState = navigation.PopOr(GUIState.Wallets);
            ApplyStateChange(previousState, navigation.CurrentState);
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

        private void SubscribeToSignals()
        {
            if (uiSignals == null || signalsSubscribed)
            {
                return;
            }

            uiSignals.EnsureSubscribed();
            uiSignals.BalancesUpdated += OnBalancesUpdated;
            uiSignals.HistoryUpdated += OnHistoryUpdated;
            uiSignals.NftsUpdated += OnNftsUpdated;
            uiSignals.BalancesRefreshStarted += OnBalancesRefreshStarted;
            uiSignals.HistoryRefreshStarted += OnHistoryRefreshStarted;
            uiSignals.NftsRefreshStarted += OnNftsRefreshStarted;
            signalsSubscribed = true;
            Log.Write($"[GUI] Subscribed to wallet UI signals (UiSignals.IsSubscribed={uiSignals.IsSubscribed})"); //TODO Check if still needed once refactoring is over
        }

        private void UnsubscribeFromSignals()
        {
            if (uiSignals == null || !signalsSubscribed)
            {
                return;
            }

            uiSignals.BalancesUpdated -= OnBalancesUpdated;
            uiSignals.HistoryUpdated -= OnHistoryUpdated;
            uiSignals.NftsUpdated -= OnNftsUpdated;
            uiSignals.BalancesRefreshStarted -= OnBalancesRefreshStarted;
            uiSignals.HistoryRefreshStarted -= OnHistoryRefreshStarted;
            uiSignals.NftsRefreshStarted -= OnNftsRefreshStarted;
            signalsSubscribed = false;
            Log.Write("[GUI] Unsubscribed from wallet UI signals"); //TODO Check if still needed once refactoring is over
        }

        private void OnBalancesRefreshStarted(PlatformKind platform)
        {
            if (!ShouldHandlePlatform(platform))
            {
                return;
            }

            MarkBalancesDirty();
            Log.Write($"[GUI] Balances refresh started for {platform}"); //TODO Check if still needed once refactoring is over
        }

        private void OnBalancesUpdated(PlatformKind platform)
        {
            if (!ShouldHandlePlatform(platform))
            {
                return;
            }

            MarkBalancesDirty();
            Log.Write($"[GUI] Balances updated for {platform}"); //TODO Check if still needed once refactoring is over
        }

        private void OnHistoryRefreshStarted(PlatformKind platform)
        {
            if (!ShouldHandlePlatform(platform))
            {
                return;
            }

            MarkHistoryDirty();
            Log.Write($"[GUI] History refresh started for {platform}"); //TODO Check if still needed once refactoring is over
        }

        private void OnHistoryUpdated(PlatformKind platform)
        {
            if (!ShouldHandlePlatform(platform))
            {
                return;
            }

            MarkHistoryDirty();
            Log.Write($"[GUI] History updated for {platform}"); //TODO Check if still needed once refactoring is over
        }

        private void OnNftsUpdated(PlatformKind platform, string symbol)
        {
            if (!ShouldHandlePlatform(platform))
            {
                return;
            }

            MarkNftDirty(symbol);
            Log.Write($"[GUI] NFTs updated for {platform} symbol={symbol}"); //TODO Check if still needed once refactoring is over
        }

        private void OnNftsRefreshStarted(PlatformKind platform, string symbol)
        {
            if (!ShouldHandlePlatform(platform))
            {
                return;
            }

            MarkNftDirty(symbol);
            Log.Write($"[GUI] NFTs refresh started for {platform} symbol={symbol}"); //TODO Check if still needed once refactoring is over
        }

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

            modalActions.YesNo(_promptText, (result) =>
            {
                var temp = _promptCallback;
                _promptText = null;
                temp(result == PromptResult.Success);
            });
        }
        #endregion

        // This code is needed for Android to quit wallet on 'Back' double press.
        int escClickCounter = 0;
        private async Task EscClickTimeAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(0.5));
            escClickCounter = 0;
        }
        private void Update()
        {
            try
            {
                // This allows to touch scroll on mobile devices.
                if (Input.touchCount > 0)
                {
                    var touch = Input.touches[0];
                    if (touch.phase == TouchPhase.Moved)
                    {
                        if (hintComboBox.DropDownIsOpened())
                            hintComboBox.ListScroll.y += touch.deltaPosition.y;
                        else if ((CurrentState == GUIState.Wallets || CurrentState == GUIState.WalletsManagement) && !(modalContext.State != ModalState.None && !modalContext.Redirected))
                            accountScroll.y += touch.deltaPosition.y;
                        else if (CurrentState == GUIState.Balances && !(modalContext.State != ModalState.None && !modalContext.Redirected))
                            balancePresenter.State.Scroll.y += touch.deltaPosition.y;
                        else if (CurrentState == GUIState.History && !(modalContext.State != ModalState.None && !modalContext.Redirected))
                            historyPresenter.State.Scroll.y += touch.deltaPosition.y;
                        else if (CurrentState == GUIState.NftView && !(modalContext.State != ModalState.None && !modalContext.Redirected))
                            nftScroll.y += touch.deltaPosition.y;
                        else if (CurrentState == GUIState.NftTransferList && !(modalContext.State != ModalState.None && !modalContext.Redirected))
                            nftTransferListScroll.y += touch.deltaPosition.y;
                        else if (CurrentState == GUIState.Settings && !(modalContext.State != ModalState.None && !modalContext.Redirected))
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
                    EscClickTimeAsync().Forget(ex => Log.WriteWarning(ex.ToString()));

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

                if (CurrentState == GUIState.Loading && AccountManager.Instance.Ready && !HasAnimation)
                {
                    Animate(AnimationDirection.Up, true, () =>
                    {
                        navigation.ClearHistory();
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

                if (modalContext.Result != PromptResult.Waiting)
                {
                    var temp = modalContext.Callback;
                    var result = modalContext.Result;
                    var success = modalContext.Result == PromptResult.Success;
                    modalContext.State = ModalState.None;
                    modalContext.Callback = null;
                    modalContext.Result = PromptResult.Waiting;

                    ResetAllCombos();

                    temp?.Invoke(result, success ? modalContext.Input.Trim() : null);

                    if (modalContext.State == ModalState.None)
                    {
                        modalContext.Time = Time.time;
                    }
                }
            }
            catch (Exception e)
            {
                WalletGUI.MessageForUser($"Unknown error: {e.ToString()}");
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

            if (CurrentState == GUIState.Loading)
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

            if (modalContext.State != ModalState.None && !modalContext.Redirected)
            {
                var modalWidth = Units(44);
                var modalHeight = Units(25 + modalContext.LineCount);

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
                modalRect = GUI.ModalWindow(0, modalRect, DoModalWindow, modalContext.Title);
            }

            if (!activeUserMessage.HasValue && messageQueue.TryDequeue(out var pendingMessage))
            {
                activeUserMessage = pendingMessage;
                activeUserMessageLogged = false;
            }

            if (activeUserMessage.HasValue && CurrentState != GUIState.MessageForUser)
            {
                SetState(GUIState.MessageForUser);
                return;
            }

            if (AccountManager.Instance.ReportGetPeersFailure)
            {
                AccountManager.Instance.ReportGetPeersFailure = false;
                modalActions.Error("Couldn't load RPCs list.\nWallet might malfunction.");
            }
            if (AccountManager.Instance.ReportAllRpcsUnavailabe)
            {
                AccountManager.Instance.ReportAllRpcsUnavailabe = false;
                modalActions.Error("All Phantasma RPC servers are unavailable.\nPlease check your network connection.");
            }
        }

        void OnApplicationQuit()
        {
            AccountManager.Instance.Settings.SaveOnExit();
        }

        private string GetNetworkBadge()
        {
            var settings = AccountManager.Instance.Settings;
            return settings.nexusKind switch
            {
                NexusKind.Test_Net => "<color=#FF8A00>[TESTNET]</color>",
                NexusKind.Dev_Net => "<color=#FFD247>[DEVNET]</color>",
                NexusKind.Local_Net => "<color=#4CAF50>[LOCALNET]</color>",
                NexusKind.Custom => "<color=#FF6F6F>[CUSTOM]</color>",
                _ => string.Empty
            };
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

                var selectedNftCount = nftViewPresenter.State.SelectedCount;

                switch (CurrentState)
                {
                    case GUIState.Nft:
                    case GUIState.NftView:
                        if (selectedNftCount > 0)
                            tempTitle = $"{nftViewPresenter.State.TotalCount} ({selectedNftCount} selected) {tempTitle}";
                        else
                            tempTitle = $"{nftViewPresenter.State.TotalCount} {tempTitle}";
                        break;
                    case GUIState.NftTransferList:
                        tempTitle = $"{selectedNftCount} {tempTitle}";
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

                var networkBadge = GetNetworkBadge();
                if (!string.IsNullOrEmpty(networkBadge))
                {
                    tempTitle = $"{tempTitle} {networkBadge}";
                }

                DrawHorizontalCenteredText(curY - 4, Units(2) + (VerticalLayout ? 4 : 0), tempTitle);

                // Drawing build timestamp at the Settings screen
                if (CurrentState == GUIState.Settings)
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

            switch (CurrentState)
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

                case GUIState.MessageForUser:
                    DoMessageForUserScreen();
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

                    Animate(AnimationDirection.Down, true, () =>
                    {
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
                    modalActions.Error($"Could not open '{account.name}' account.", () =>
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
                catch (Exception e)
                {
                    Log.Write("ImportWallet() exception: " + e);
                    modalActions.Error("Incorrect WIF format.", () => { if (callback != null) { callback(-1); } });
                    return;
                }

                foreach (var account in accountManager.Accounts)
                {
                    if (account.phaAddress == keys.Address.ToString())
                    {
                        modalActions.Error($"Private key{walletNumberString} is already imported in a different account: {account.name}.", () => { if (callback != null) { callback(-1); } });
                        return;
                    }
                }
            }

            ShowModal("Wallet Name", $"Enter a name for your wallet{walletNumberString}", ModalState.Input, AccountManager.MinAccountNameLength, AccountManager.MaxAccountNameLength, modalActions.ConfirmCancelOptions, 1, (result, name) =>
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
                        modalActions.Error("An account with this name already exists.", () => { ImportWallet(wif, pkIndex, overallDerivationCount, password, legacySeed, callback); });
                    }
                    else
                    {
                        if (password == null)
                        {
                            modalActions.YesNo($"Do you want to add a password to wallet{walletNumberString}?\nThe password will be required to open the wallet.\nIt will also be prompted every time you do a transaction", (wantsPass) =>
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
            ShowModal("Wallet Password", "Enter a password for your wallet", ModalState.Password, AccountManager.MinPasswordLength, AccountManager.MaxPasswordLength, modalActions.ConfirmCancelOptions, 1, (passResult, password) =>
            {
                if (passResult == PromptResult.Success)
                {
                    if (IsGoodPassword(name, password))
                    {
                        FinishCreateAccount(name, wif, password, legacySeed, callback);
                    }
                    else
                    {
                        modalActions.Error($"That password is either too short or too weak.\nNeeds at least {AccountManager.MinPasswordLength} characters and can't be easy to guess.", () =>
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
            var (wif, incorrectWord) = Mnemonics.MnemonicToWif(mnemonicPhrase, derivationIndex);

            if (wif == null)
            {
                if (incorrectWord != null)
                {
                    modalActions.Error($"Seed phrase that you entered is incorrect.\nIncorrect word: '{incorrectWord}'.");
                }
                else
                {
                    modalActions.Error("Seed phrase that you entered is incorrect." +
"\nPlease check your spelling carefully, and try again." +
"\n" +
"\nEnsure that:" +
"\n* If copy / pasting - That you've selected the entire set of characters." +
"\n* If copy / pasting - That the characters have been copied into your clipboard correctly." +
        "\n* If typing it - Take care to check that you're using English keyboard layout and the correct case for each letter.");
                }
                return;
            }

            ImportWallet(wif, (int)derivationIndex, overallDerivationCount, null, false, (walletIndex) =>
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
            ShowModal("Number of created wallets", "Enter number of wallets to derive from this seed phrase.\n\nUse \"1\" if unsure.", ModalState.Input, 1, -1, modalActions.ConfirmCancelOptions, 1, (success, input) =>
            {
                if (success == PromptResult.Success)
                {
                    if (UInt32.TryParse(input, out var numberOfWallets))
                    {
                        DeriveAccountFromSeed(mnemonicPhrase, 0, numberOfWallets);
                    }
                    else
                    {
                        modalActions.Error("Incorrect number", () => { DeriveAccountsFromSeed(mnemonicPhrase); });
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
                modalActions.Error("Error creating account.\n" + e.Message);
            }
        }

        private string[] accountOptions = new string[] { "Generate new wallet", "Import wallet", "Manage", "Settings" };

        private string[] walletsManagementOptions = new string[] { "Export", "Import", "Delete", "Cancel", "Save and Close" };

        private Vector2 accountScroll;
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
                            ShowModal("Attention!",
                                "For your own safety, write down generated seed words on a piece of paper and store it safely and hidden.\n\nThese words serve as a back-up of your wallet.\n\nWithout a backup, it is impossible to recover your private key,\nand any funds in the account will be lost if something happens to this device.",
                                ModalState.Message, -1, -1, modalActions.ConfirmCancelOptions, 0,
                                (result, _) =>
                            {
                                if (result == PromptResult.Success)
                                {
                                    newWalletSeedPhrase = Mnemonics.GenerateMnemonic(AccountManager.Instance.Settings.mnemonicPhraseLength);

                                    Animate(AnimationDirection.Down, true, () =>
                                    {
                                        newWalletCallback = new Action(() =>
                                        {
                                            DeriveAccountsFromSeed(newWalletSeedPhrase);

                                            PopState();
                                        });

                                        PushState(GUIState.Backup);
                                    });
                                }
                            });

                            break;
                        }

                    case 1:
                        {
                            ShowModal("Wallet Import", "Supported inputs:\n12/24 word seed phrase\nPrivate key (HEX format)\nPrivate key (WIF format)", ModalState.Input, 32, 1024, modalActions.ConfirmCancelOptions, 4, (result, key) =>
                            {
                                if (result == PromptResult.Success)
                                {
                                    if (PhantasmaAPI.IsValidPrivateKey(key) && !key.Contains(' '))
                                    {
                                        modalActions.YesNo("Was this private key created using a Poltergeist version earlier than v2.4 (before end of April 2021)?", (legacySeed) =>
                                        {
                                            ImportWallet(key, -1, 1, null, legacySeed == PromptResult.Success, null);
                                        });
                                    }
                                    else
                                    if ((key.Length == 64 || (key.Length == 66 && key.ToUpper().StartsWith("0X"))) && !key.Contains(' '))
                                    {
                                        var priv = Base16.Decode(key);
                                        var tempKey = new PhantasmaKeys(priv);
                                        modalActions.YesNo("Was this WIF created using a Poltergeist version earlier than v2.4 (before end of April 2021)?", (legacySeed) =>
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
                                        modalActions.Error("Seed phrase or private key that you entered is incorrect." +
"\nPlease check your spelling carefully, and try again." +
"\n" +
"\nEnsure that:" +
"\n* If copy / pasting - That you've selected the entire set of characters." +
"\n* If copy / pasting - That the characters have been copied into your clipboard correctly." +
"\n* If typing it - Take care to check that you're using English keyboard layout and the correct case for each letter.");
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
                        GUI.Label(new Rect(Border * 2, curY, windowRect.width - Border * 2, Units(2) + 4), account.ToString());
                        btnRect = new Rect((rect.width - btnWidth) / 2, curY + Units(3) + 4, btnWidth, Units(2));
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
                                "Do you want to protect exported data with a password?\nIf not, leave this field blank.",
                                 ModalState.Password,
                                 -1, -1,
                                 modalActions.ConfirmCancelOptions, 1, (passResult, password) =>
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

                                    modalActions.ConfirmCancel("Copy wallets export data to the clipboard?", (result) =>
                                    {
                                        if (result == PromptResult.Success)
                                        {
                                            GUIUtility.systemCopyBuffer = serializedExportData;
                                            modalActions.Info("Wallets export data copied to the clipboard.");
                                        }
                                    });
                                }
                            });
                            break;
                        }

                    case 1:
                        {
                            ShowModal("Wallets Import", "Please enter wallets data that you received from Wallets Export dialog (on Wallets Management screen):", ModalState.Input, 1, -1, modalActions.ConfirmCancelOptions, 4, (result, walletsData) =>
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
                                                if (accountManager.Accounts.Where(x => x.phaAddress.ToUpper() == account.phaAddress.ToUpper()).Any())
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
                                                ModalState.Message, 0, 0, modalActions.ConfirmCancelOptions, 0, (result2, input) =>
                                                {
                                                    if (result2 == PromptResult.Success)
                                                    {
                                                        var count = 0;
                                                        foreach (var accountToImport in accountsToImport)
                                                        {
                                                            accountManager.Accounts.Add(accountToImport);
                                                            count++;
                                                        }
                                                        modalActions.Info($"{count} wallets successfully imported.");
                                                    }
                                                });
                                        });

                                        if (accountsExport.passwordProtected)
                                        {
                                            ShowModal("Wallets Import",
                                                "Please enter password:", ModalState.Password, AccountManager.MinPasswordLength, AccountManager.MaxPasswordLength, modalActions.ConfirmCancelOptions, 1, (passResult, password) =>
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
                                                            modalActions.Error("Cannot decrypt wallets data.");
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
                                        modalActions.Error("Cannot open wallets data.");
                                    }
                                }
                            });
                            break;
                        }

                    case 2:
                        {
                            modalActions.ConfirmCancel($"{accountManagementSelectedList.Count()} selected wallets will be deleted.\nMake sure you have backups of your private keys!\nOtherwise you will lose access to your funds.", (result) =>
                            {
                                if (result == PromptResult.Success)
                                {
                                    var counter = 0;
                                    foreach (var accountToDelete in accountManagementSelectedList)
                                    {
                                        accountManager.Accounts.Remove(accountManager.Accounts.Where(x => x.phaAddress.ToUpper() == accountToDelete.ToUpper()).First());
                                        counter++;
                                    }

                                    accountManagementSelectedList.Clear();

                                    modalActions.Info($"{counter} wallets removed from this device.");
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
                        ShowModal("Rename", $"Current local name: {account.name}\nAddress: {account.phaAddress}\n\nEnter new local account name:", ModalState.Input, AccountManager.MinAccountNameLength, AccountManager.MaxAccountNameLength, modalActions.ConfirmCancelOptions, 1, (result, input) =>
                        {
                            if (input == null || input.Length < AccountManager.MinAccountNameLength ||
                                input.Length > AccountManager.MaxAccountNameLength)
                            {
                                modalActions.Error("Invalid account name.\n");
                                return;
                            }

                            if (accountManager.Accounts.Any(x => x.name.ToLower() == input.ToLower()))
                            {
                                modalActions.Error("Account with this name already exists.\n");
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
                modalActions.Error("Could not import wallet.\n" + e.Message);
            }
        }

        private void CloseCurrentStack()
        {
            Animate(AnimationDirection.Down, true, () =>
            {
                var accountManager = AccountManager.Instance;
                accountManager.UnselectAcount();
                navigation.ClearHistory();
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
            DrawHorizontalCenteredText(curY - 5, Units(VerticalLayout ? 3 : 2), address);

            curY += Units(3);

            if (showCopyToClipboardButton)
            {
                DoButton(true, new Rect(windowRect.width / 2 - btnWidth - Border, curY, btnWidth, Units(1) + (VerticalLayout ? 8 : 0)), "Copy Address", () =>
                  {
                      GUIUtility.systemCopyBuffer = address;
                      modalActions.Info("Address copied to clipboard.");
                  });

                DoButton(true, new Rect(windowRect.width / 2 + Border, curY, btnWidth, Units(1) + (VerticalLayout ? 8 : 0)), "Explorer", () =>
                {
                    switch (accountManager.CurrentPlatform)
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
            var viewState = nftViewPresenter.State;
            var filterName = viewState.FilterName;
            var filterTypeIndex = viewState.FilterTypeIndex;
            var filterType = viewState.FilterType;
            var filterRarity = viewState.FilterRarity;
            var filterMinted = viewState.FilterMinted;
            var prevSortMode = accountManager.Settings.nftSortMode;
            var prevTtrsSortMode = accountManager.Settings.ttrsNftSortMode;
            var prevSortDirection = accountManager.Settings.nftSortDirection;

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

            if (CurrentState != GUIState.NftView)
            {
                // #7: Select all button
                DoNftToolButton(posX4 + toolLabelWidth,
                                posY3,
                                (VerticalLayout) ? toolLabelWidth - toolFieldSpacing - 8 : toolLabelWidth - toolFieldSpacing, "Select", () =>
                                {
                                    if (nftFilteredList.Count > 0)
                                    {
                                        // If filter is applied, select button selects only filtered items.
                                        nftViewPresenter.Select(nftFilteredList.Select(x => x.Id));
                                    }
                                    else
                                    {
                                        // If no filter is applied, select button selects all items.
                                        nftViewPresenter.ClearSelection();
                                        nftViewPresenter.Select(accountManager.CurrentNfts?.Select(x => x.Id));
                                    }
                                    MarkNftDirty(transferSymbol);
                                });

                // #8: Invert selection button
                DoNftToolButton((VerticalLayout) ? posX4 + toolLabelWidth * 2 - toolFieldSpacing + 8 : posX4 + toolLabelWidth * 2 + toolFieldSpacing,
                                posY3,
                                (VerticalLayout) ? toolLabelWidth - toolFieldSpacing - 8 : toolLabelWidth - toolFieldSpacing, "Invert", () =>
                                {
                                    if (nftFilteredList.Count > 0)
                                    {
                                        // If filter is applied, invert button processes only filtered items.
                                        nftViewPresenter.InvertSelection(nftFilteredList.Select(x => x.Id));
                                    }
                                    else
                                    {
                                        // If no filter is applied, invert button processes all items.
                                        nftViewPresenter.InvertSelection(accountManager.CurrentNfts?.Select(x => x.Id));
                                    }
                                    MarkNftDirty(transferSymbol);
                                });
            }

            if (transferSymbol == "TTRS")
            {
                // #3: NFT rarity filter
                DoNftToolComboBox(posX3, posY2, nftRarityComboBox, Enum.GetValues(typeof(ttrsNftRarity)).Cast<ttrsNftRarity>().ToList(), "Rarity: ", ref filterRarity);
            }

            // #4: NFT mint date filter
            DoNftToolComboBox(posX4, posY2, nftMintedComboBox, Enum.GetValues(typeof(nftMinted)).Cast<nftMinted>().ToList().Select(x => x.ToString().Replace('_', ' ')).ToList(), "Minted: ", ref filterMinted);

            // #1: NFT name filter
            DoNftToolTextField(posX1, posY, "Name: ", ref filterName);

            if (transferSymbol == "TTRS")
            {
                // #2: NFT type filter
                DoNftToolComboBox(posX2, posY, nftTypeComboBox, Enum.GetValues(typeof(ttrsNftType)).Cast<ttrsNftType>().ToList(), "Type: ", ref filterTypeIndex);
                if (Enum.IsDefined(typeof(ttrsNftType), filterTypeIndex))
                    filterType = ((ttrsNftType)filterTypeIndex).ToString();
                else
                    filterType = "All";
            }
            else
            {
                filterType = "All";
                filterTypeIndex = 0;
            }

            if (viewState.UpdateFilters(filterName, filterTypeIndex, filterType, filterRarity, filterMinted))
            {
                nftScroll = Vector2.zero;
                nftViewPresenter.ClearSelection();
                MarkNftDirty(transferSymbol);
            }

            if (transferSymbol == "TTRS")
            {
                if (accountManager.Settings.ttrsNftSortMode != prevTtrsSortMode || accountManager.Settings.nftSortDirection != prevSortDirection)
                {
                    MarkNftDirty(transferSymbol);
                }
            }
            else
            {
                if (accountManager.Settings.nftSortMode != prevSortMode || accountManager.Settings.nftSortDirection != prevSortDirection)
                {
                    MarkNftDirty(transferSymbol);
                }
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
                style.fontSize -= VerticalLayout ? 4 : 2;

                var value = AccountManager.Instance.GetTokenWorth(symbol, amount);
                GUI.Label(subRect, $"{WalletAmountFormatter.Format(amount)} {symbol} {caption}" + (value == null ? "" : $" ({value})"));
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
            var camWidth = (int)((camTexture.width * camHeight) / (float)camTexture.height);

            var camRect = new Rect((windowRect.width - camWidth) / 2, Border + Units(5), camWidth, camHeight);
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
                            var tag = platform.ToString().ToLower() + "://";
                            if (result.Text.StartsWith(tag))
                            {
                                modalContext.Input = result.Text.Substring(tag.Length);
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
        private void TrySeedVerification(string[] seed, Action<bool> callback)
        {
            // Verifying 3 random words
            int[] indices = Enumerable.Range(0, seed.Length)
                          .OrderBy(_ => UnityEngine.Random.value)
                          .Take(3)
                          .OrderBy(i => i)
                          .ToArray();
            ShowModal("Seed verification", $"To confirm that you have backed up your seed phrase, enter your seed words {string.Join(", ", indices.Select(i => $"#{i + 1}"))}, using space to separate them:",
                ModalState.Input, 5, -1, modalActions.ConfirmCancelOptions, 4, (result, input) =>
            {
                if (result == PromptResult.Success)
                {
                    try
                    {
                        var wordsToVerify = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                        if (seed[indices[0]] == wordsToVerify[0] &&
                            seed[indices[1]] == wordsToVerify[1] &&
                            seed[indices[2]] == wordsToVerify[2]
                        )
                        {
                            callback(true);
                        }
                        else
                        {
                            modalActions.Error("Seed phrase is incorrect!", () =>
                            {
                                TrySeedVerification(seed, callback);
                            });
                        }
                    }
                    catch (Exception e)
                    {
                        Log.WriteWarning("TrySeedVerification: Exception: " + e);
                        modalActions.Error("Seed phrase is incorrect!\n" + e.Message, () =>
                        {
                            TrySeedVerification(seed, callback);
                        });
                    }
                }
            });
        }

        private string[] backupScreenOptions = new string[] { "Copy to clipboard", "Continue", "Cancel" };
        private void DoBackupScreen()
        {
            string[] words = newWalletSeedPhrase.Split(' ');
            var lines = new List<string>();

            for (int i = 0; i < words.Length; i += 3)
            {
                lines.Add(string.Format("{0,2}. {1,-10}{2,2}. {3,-10}{4,2}. {5,-10}",
                    i + 1, words[i],
                    i + 2, words[i + 1],
                    i + 3, words[i + 2]
                ));
            }

            string seedPhraseForDisplay = string.Join("\n", lines);

            if (monoFont == null)
            {
                // Load mono font for the first time
                monoFont = Resources.Load<Font>("DejaVuSansMono");
            }
            var style = new GUIStyle(GUI.skin.label);
            style.font = monoFont;
            style.alignment = TextAnchor.UpperLeft;

            float curY = windowRect.height / 2 - Units(6);

            GUI.Label(new Rect(Border, curY, windowRect.width - Border * 2, Units(24)),
                seedPhraseForDisplay,
                style);

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
                            modalActions.Info("Seed phrase copied to the clipboard.");
                            break;
                        }

                    case 1:
                        {
                            TrySeedVerification(words, (success) =>
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
            GUI.Label(new Rect(Border, curY, windowRect.width - Border * 2, windowRect.height - (Border + curY)), fatalError);

            var btnWidth = Units(12);
            curY = (int)(windowRect.height - Units(VerticalLayout ? 6 : 7));
            DoButton(true, new Rect((windowRect.width - btnWidth) / 2, curY, btnWidth, Units(2)), "Copy to Clipboard", () =>
            {
                GUIUtility.systemCopyBuffer = fatalError;
                modalActions.Info("Error log copied to clipboard.");
            });
        }

        private void DoMessageForUserScreen()
        {
            if (activeUserMessage.HasValue && !activeUserMessageLogged)
            {
                Log.WriteWarning(activeUserMessage.Value.Body);
                activeUserMessageLogged = true;
            }

            int curY;

            curY = Units(5);
            var messageBody = activeUserMessage?.Body ?? string.Empty;
            GUI.Label(new Rect(Border, curY, windowRect.width - Border * 2, windowRect.height - (Border + curY)), messageBody);

            var btnWidth = Units(12);
            curY = (int)(windowRect.height - Units(VerticalLayout ? 6 : 7));
            DoButton(true, new Rect((windowRect.width - btnWidth) / 2, curY, btnWidth, Units(2)),
                "Continue", () =>
            {
                PopState();
                activeUserMessage = null;
                activeUserMessageLogged = false;
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
                balancePresenter.Refresh(false);
                MarkBalancesDirty();
            });
            var endY = DoBottomMenu();

            var balancesModel = GetBalancesSnapshot();

            balanceRenderer.Render(balancesModel, ref balancePresenter.State.Scroll, startY, endY);
        }

        private void DoBalanceEntry(WalletBalanceEntry balance, int index, int curY, Rect rect)
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
            var value = balance.FiatWorth;
            var balanceFormat = $"{WalletAmountFormatter.Format(balance.Available)}";
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
                        if (Input.GetKey(KeyCode.LeftShift) &&
                            accountManager.Settings.devMode // TODO remove later
                        )
                        {
                            secondaryAction = "Info";
                            secondaryEnabled = true;
                            secondaryCallback = () =>
                            {
                                accountManager.GetPhantasmaAddressInfo(state.address, accountManager.CurrentAccount, (result, error) =>
                                {
                                    if (!string.IsNullOrEmpty(error))
                                    {
                                        modalActions.Error("Something went wrong!\n" + error);
                                        return;
                                    }
                                    else
                                    {
                                        modalActions.CopyableMessage("Account information", result, closeOnCopy: false);
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
                                modalActions.RequireAmount("Stake SOUL", null, "SOUL", 0.1m, balance.Available, (selectedAmount) =>
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
                                    if (kcalBalance != default)
                                    {
                                        kcalClaimable = kcalBalance.Claimable;
                                    }

                                    var message = $"Do you want to stake {selectedAmount} SOUL?" +
                                        $"\nYou will be able to claim {WalletAmountFormatter.Format(expectedDailyKCAL, selectedAmount >= 1 ? MoneyFormatType.Standard : MoneyFormatType.Long)} KCAL per day." +
                                        $"\n\nPlease note, after staking you won't be able to unstake SOUL tokens for next 24 hours.";

                                    if (kcalClaimable > 0)
                                    {
                                        message += $"\n\nAll unclaimed KCAL will be claimed: {WalletAmountFormatter.Format(kcalClaimable, kcalClaimable >= 1 ? MoneyFormatType.Standard : MoneyFormatType.Long)} KCAL.";
                                    }

                                    StakeSOUL(selectedAmount, message + twoSmsWarning, (hash, txResult, error) =>
                                    {
                                        TxResultMessage(hash, txResult, error, "Your SOUL tokens were staked!\n\nThe transaction has successfully completed, but it may take up to 30 seconds until the change is reflected in your wallet balance\n");
                                    });
                                });
                            };
                        }

                        var unstakeAvailability = stakeService.GetUnstakeAvailability();
                        if (balance.Staked > 0)
                        {
                            tertiaryAction = "Unstake";
                            tertiaryEnabled = unstakeAvailability.Success;
                            tertiaryCallback = () =>
                            {
                                modalActions.RequireAmount("Unstake SOUL", null, "SOUL", 0.1m, balance.Staked,
                                    (amount) =>
                                    {
                                        var unstakeMessage = stakeService.BuildUnstakeMessage(amount);
                                        if (!unstakeMessage.Success)
                                        {
                                            modalActions.Error(unstakeMessage.Error);
                                            return;
                                        }

                                        var messageText = string.IsNullOrEmpty(unstakeMessage.Message) ? unstakeMessage.Error : unstakeMessage.Message;
                                        modalActions.YesNo(messageText, (result) =>
                                        {
                                            if (result == PromptResult.Success)
                                            {
                                                RequestKCAL("SOUL", (kcal) =>
                                                {
                                                    if (kcal == PromptResult.Success)
                                                    {
                                                        var address = Address.Parse(state.address);

                                                        var planResult = stakeService.BuildUnstakeDraft(amount);
                                                        SendTransactionDraft(planResult, (hash, txResult, error) =>
                                                        {
                                                            TxResultMessage(hash, txResult, error, "Your SOUL tokens were unstaked!\n\nThe transaction has successfully completed, but it may take up to 30 seconds until the change is reflected in your wallet balance\n");
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
                            var claimMessage = stakeService.BuildClaimKcalMessage(balance.Claimable);
                            if (!claimMessage.Success)
                            {
                                modalActions.Error(claimMessage.Error);
                                return;
                            }

                            var messageText = string.IsNullOrEmpty(claimMessage.Message) ? claimMessage.Error : claimMessage.Message;
                            modalActions.YesNo(messageText, (result) =>
                            {
                                if (result == PromptResult.Success)
                                {
                                    RequestKCAL("SOUL", (feeResult) =>
                                    {
                                        if (feeResult == PromptResult.Success)
                                        {
                                            var planResult = stakeService.BuildClaimKcalDraft(balance.Claimable);

                                            SendTransactionDraft(planResult, (hash, txResult, error) =>
                                                {
                                            TxResultMessage(hash, txResult, error, "Your KCAL tokens were claimed!\n\nThe transaction has successfully completed, but it may take up to 30 seconds until the change is reflected in your wallet balance\n");
                                        });
                                        }
                                        else
                                            if (feeResult == PromptResult.Failure)
                                        {
                                            modalActions.Error("KCAL is required to make transactions!");
                                        }
                                    });
                                }
                            });
                        };
                    }
                    break;

                default:
                    {
                        var hasTokenInfo = Tokens.GetToken(balance.Symbol, accountManager.CurrentPlatform, out var token) && token != null;
                        var isFungible = balance.Fungible;
                        if (hasTokenInfo)
                        {
                            try
                            {
                                if (token.Flags != null)
                                {
                                    isFungible = token.IsFungible();
                                }
                            }
                            catch
                            {
                                // fall back to balance data when token metadata not ready yet
                                isFungible = balance.Fungible;
                            }
                        }

                        if (!isFungible)
                        {
                            // It's an NFT. We add additional button to get to NFTs view mode.
                            secondaryAction = "View";
                            secondaryEnabled = balance.Available > 0;
                            secondaryCallback = () =>
                            {
                                transferSymbol = balance.Symbol;

                                // We should do this initialization here and not in PushState,
                                // to allow "Back" button to work properly.
                                nftScroll = Vector2.zero;
                                nftViewPresenter.ClearSelection();
                                nftViewPresenter.ResetFiltersAndPagination();
                                nftViewPresenter.ResetSorting();
                                nftViewPresenter.Refresh(transferSymbol, false);
                                MarkNftDirty(transferSymbol);

                                PushState(GUIState.NftView);
                                return;
                            };
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
                Input.GetKey(KeyCode.LeftShift) &&
                accountManager.Settings.devMode // TODO remove later
                )
            {
                mainAction = "Burn";
            }
            else if (accountManager.CurrentPlatform == PlatformKind.Phantasma &&
                balance.Symbol.ToUpper() == "SOUL" &&
                balance.Staked >= 50000 &&
                Input.GetKey(KeyCode.LeftShift) &&
                accountManager.Settings.devMode // TODO remove later
                )
            {
                mainAction = "SM reward";
                mainActionEnabled = true; // This one should be always enabled
            }
            else
            {
                mainAction = "Send";
            }

            // TODO remove NFT check later
            // NFT transfers are currently unavailable
            Tokens.GetToken(balance.Symbol, accountManager.CurrentPlatform, out var transferToken0);
            DoButton(mainActionEnabled && !(!transferToken0.IsFungible() && !accountManager.Settings.devMode), new Rect(rect.x + rect.width - (Units(6) + 8), curY + btnY, Units(4) + 8, Units(2)), mainAction, () =>
            {
                if (mainAction == "Send")
                {
                    transferSymbol = balance.Symbol;
                    var transferName = $"{transferSymbol} transfer";
                    TokenResult transferToken;

                    Tokens.GetToken(transferSymbol, accountManager.CurrentPlatform, out transferToken);

                    if (string.IsNullOrEmpty(transferToken.Flags))
                    {
                        modalActions.Error($"Operations with token {transferSymbol} are not supported yet in this version.");
                        return;
                    }

                    if (transferToken.IsTransferable() && !transferToken.IsFungible())
                    {
                        // We should do this initialization here and not in PushState,
                        // to allow "Back" button to work properly.
                        nftScroll = Vector2.zero;
                        nftViewPresenter.ClearSelection();
                        nftViewPresenter.ResetFiltersAndPagination();
                        nftViewPresenter.ResetSorting();
                        nftViewPresenter.Refresh(transferSymbol, false);
                        MarkNftDirty(transferSymbol);

                        PushState(GUIState.Nft);
                        return;
                    }

                    if (!transferToken.IsTransferable())
                    {
                        modalActions.Error($"Transfers of {transferSymbol} tokens are not allowed.");
                        return;
                    }

                    ShowModal(transferName, "Enter destination address", ModalState.Input, 3, 64, modalActions.ConfirmCancelOptions, 1, (result, destAddress) =>
                    {
                        if (result == PromptResult.Failure)
                        {
                            return; // user canceled
                        }

                        var ethereumAddressUtil = new PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Util.AddressUtil();

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
                                    modalActions.Error("No account with such name exists.");
                                }
                            });
                        }
                        else
                        {
                            modalActions.Error("Invalid destination address.");
                        }
                    });

                    var hints = accountHintsService.BuildAccountHints(accountManager.CurrentPlatform.GetTransferTargets(transferToken));
                    hints["Scan QR"] = $"|{GUIState.ScanQR}";
                    modalContext.Hints = hints;
                }
                else if (mainAction == "SM reward")
                {
                    var planResult = stakeService.BuildClaimSmRewardDraft();
                    SendTransactionDraft(planResult, (hash, txResult, error) =>
                    {
                        TxResultMessage(hash, txResult, error, "You claimed SM reward!");
                    });
                }
                else if (mainAction == "Burn")
                {
                    modalActions.RequireAmount($"Burn {balance.Symbol} tokens", null, balance.Symbol, 0.1m, balance.Available, (amountToBurn) =>
                    {
                        var burnPrep = burnService.PrepareFungibleBurn(balance.Symbol, balance.Available, amountToBurn);
                        if (!burnPrep.Success)
                        {
                            modalActions.Error(burnPrep.Error);
                            return;
                        }

                        var confirmMessage = string.IsNullOrEmpty(burnPrep.Message) ? $"Are you sure you want to burn {amountToBurn} {balance.Symbol} tokens?" : burnPrep.Message;

                        modalActions.ConfirmCancel(confirmMessage, (result) =>
                        {
                            if (result == PromptResult.Success)
                            {
                                SendTransactionDraft(burnPrep.Data, (hash, txResult, error) =>
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
            nftRenderer.Render(GetNftViewSnapshot(transferSymbol));
        }

        // Used for both NFT list and transfer NFT list.
        private void DoNftEntry(string entryId, int index, int curY, Rect rect)
        {
            if (string.IsNullOrEmpty(entryId))
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

                nftDescription = item.mint == 0 ? "" : (VerticalLayout ? "#" : "Mint #") + item.mint + " " + (VerticalLayout ? DateTimeOffset.FromUnixTimeSeconds((long)item.timestamp).ToLocalTime().ToString("dd.MM.yy") : DateTimeOffset.FromUnixTimeSeconds((long)item.timestamp).ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")) + (VerticalLayout ? " " : " / ") + nftType + rarity;
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
                var rom = accountManager.GetNftRom(entryId);

                imageUrl = item.GetPropertyValue("ImageURL");

                DateTime nftDate = new DateTime();
                if (rom != null)
                {
                    nftDate = rom.GetDate();
                }

                nftName = item.GetPropertyValue("Name");
                nftDescription = item.GetPropertyValue("Description");

                nftDescription = item.Mint == "0" ? "" : (VerticalLayout ? "#" : "Mint #") + item.Mint + " " +
                    (nftDate == DateTime.MinValue ? "" : (VerticalLayout ? nftDate.ToString("dd.MM.yy") : nftDate.ToString("dd.MM.yyyy HH:mm:ss"))) +
                    (String.IsNullOrEmpty(nftDescription) ? "" : ((VerticalLayout ? " " : " / ") + nftDescription));

                if (item.Infusion != null && item.Infusion.Length > 0)
                {
                    infusionDescription = VerticalLayout ? "" : "Infusions: ";

                    var fungibleInfusions = new Dictionary<string, decimal>();
                    var nftInfusions = new Dictionary<string, int>();
                    for (var i = 0; i < item.Infusion.Length; i++)
                    {
                        var symbol = item.Infusion[i].Key;
                        var amountOrId = item.Infusion[i].Value;

                        if (Tokens.GetToken(symbol, accountManager.CurrentPlatform, out var token))
                        {
                            if (token.IsFungible())
                                fungibleInfusions.Add(symbol, UnitConversion.ToDecimal(amountOrId, token.Decimals));
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
                        if (nftInfusedCount > 0)
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
                nameYPosition += VerticalLayout ? -2 : -8;
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
            if (CurrentState != GUIState.NftView)
            {
                var nftIsSelected = nftViewPresenter.IsSelected(entryId);
                var toggleResult = GUI.Toggle(btnRectToggle, nftIsSelected, "");
                if (toggleResult != nftIsSelected)
                {
                    nftViewPresenter.ToggleSelection(entryId);
                    MarkNftDirty(transferSymbol);
                }
            }
            GUI.enabled = true;

            var hasNftExplorerUrl = !string.IsNullOrWhiteSpace(accountManager.Settings.phantasmaNftExplorer);
            var canShowViewButton = transferSymbol == "TTRS" || hasNftExplorerUrl;
            if (canShowViewButton)
            {
                DoButton(!DrawNftToolsAreActive(), btnRect, "View", () =>
                {
                    if (transferSymbol == "TTRS")
                    {
                        Application.OpenURL("https://www.22series.com/part_info?id=" + entryId);
                    }
                    else
                    {
                        var explorerUrl = accountManager.GetPhantasmaNftURL(transferSymbol, entryId);
                        if (!string.IsNullOrEmpty(explorerUrl))
                        {
                            Application.OpenURL(explorerUrl);
                        }
                    }
                });
            }
        }

        private void DoNftTransferListScreen()
        {
            nftTransferRenderer.Render();
        }

        private void DoHistoryScreen()
        {
            var accountManager = AccountManager.Instance;

            var startY = DrawPlatformTopMenu(() =>
            {
                historyPresenter.Refresh(false);
                MarkHistoryDirty();
            });

            var endY = DoBottomMenu();

            var historyModel = GetHistorySnapshot();

            historyRenderer.Render(historyModel, ref historyPresenter.State.Scroll, startY, endY);
        }

        private void DoHistoryEntry(WalletHistoryItem entry, int index, int curY, Rect rect)
        {
            var accountManager = AccountManager.Instance;

            var date = String.Format("{0:g}", entry.Date);

            GUI.Label(new Rect(Units(2), curY + 4, Units(20), Units(2)), VerticalLayout ? entry.Hash.Substring(0, 16) + "..." : entry.Hash);

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

            DoButton(!string.IsNullOrEmpty(entry.Url), btnRect, "View", () =>
            {
                Application.OpenURL(entry.Url);
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
                            modalActions.HexOrWif("Private key export", $"Show private key in WIF format (recommended) or in HEX format." +
                                "\n\nNEVER SHARE YOUR PRIVATE KEY with ANYONE, including TEAM, SUPPORT or COMMUNITY ADMINS." +
                                "\n\nFollowing screen will reveal your private key. It provides full access to your wallet and funds." +
                                " Press 'WIF format' or 'HEX format' buttons to expose private key in corresponding format." +
                                "\n\nMake sure NO ONE IS LOOKING AT YOUR SCREEN.", (result, input) =>
                            {
                                if (result == PromptResult.Custom_1)
                                {
                                    RequestPassword("Export private key (HEX)", accountManager.CurrentPlatform, true, false, (auth) =>
                                    {
                                        if (auth == PromptResult.Success)
                                        {
                                            var keys = PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.FromWIF(accountManager.CurrentWif);
                                            var hexKey = PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Hex.HexConvertors.Extensions.HexByteConvertorExtensions.ToHex(keys.PrivateKey);

                                            modalActions.CopyableMessage("Your private key (HEX)", KeyPrepareForMessageBox(hexKey), (result1, _) =>
                                            {
                                                if (result1 != PromptResult.Success)
                                                {
                                                    modalActions.Info("Your private key was copied to the clipboard.");
                                                }
                                            }, copyValue: hexKey);
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
                                            modalActions.CopyableMessage("Your private key (WIF)", KeyPrepareForMessageBox(accountManager.CurrentWif), (result1, _) =>
                                            {
                                                if (result1 != PromptResult.Success)
                                                {
                                                    modalActions.Info("Your private key was copied to the clipboard.");
                                                }
                                            }, copyValue: accountManager.CurrentWif);
                                        }
                                    },
                                    ignoreStoredPassword: true);
                                }
                            });
                            break;
                        }

                    case 1:
                        {
                            ShowModal("Account migration", "Insert WIF of the target account", ModalState.Input, 32, 64, modalActions.ConfirmCancelOptions, 1, (wifResult, wif) =>
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
                                        modalActions.YesNo("Are you sure you want to migrate this account?\n\nBefore doing migration, make sure that both old and new private keys (WIFs or seed phrases) are safely stored.\n\nCheck your Eth/Neo/BSC balances for current wallet, if they have funds, move them to a new wallet before doing migration.\n\nBy doing a migration, any existing Phantasma rewards will be transferred without penalizations.\nTarget address: " + newKeys.Address.Text, (result) =>
                                        {
                                            if (result == PromptResult.Success)
                                            {
                                                var address = Address.Parse(accountManager.CurrentState.address);

                                                var planResult = accountAdminService.BuildMigrateDraft(newKeys.Address);

                                                SendTransactionDraft(planResult, (hash, txResult, error) =>
                                                {
                                                    if (string.IsNullOrEmpty(error) && hash != Hash.Null)
                                                    {
                                                        accountManager.ReplaceAccountWIF(accountManager.CurrentIndex, wif, accountManager.CurrentPasswordHash, out var deletedDuplicateWallet);
                                                        CloseCurrentStack();

                                                        modalActions.CopyableMessage("Message",
                                                            $"The account was migrated.\n{(string.IsNullOrEmpty(deletedDuplicateWallet) ? "" : $"\nDuplicate account '{deletedDuplicateWallet}' was deleted.\n")}If you haven't stored old account's WIF yet, please do it now.\n\nOld WIF: {oldWif}",
                                                            closeOnCopy: false,
                                                            copyValue: oldWif);
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
                                        modalActions.Error("You need to provide a different WIF.");
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
                                ShowModal("Setup Name", $"Enter a name for the chain address.\nOther users will be able to transfer assets directly to this name.", ModalState.Input, AccountManager.MinAccountNameLength, AccountManager.MaxAccountNameLength, modalActions.ConfirmCancelOptions, 1, (result, name) =>
                                {
                                    if (result == PromptResult.Success)
                                    {
                                        if (ValidationUtils.IsValidIdentifier(name))
                                        {
                                            RequestKCAL(null, (kcalResult) =>
                                                {
                                                    if (kcalResult == PromptResult.Success)
                                                    {
                                                        var planResult = accountAdminService.BuildRegisterNameDraft(name, accountManager.CurrentState.address);

                                                        SendTransactionDraft(planResult, (hash, txResult, error) =>
                                                        {
                                                            if (string.IsNullOrEmpty(error) && hash != Hash.Null)
                                                            {
                                                                SetState(CurrentState); // force updating the current UI

                                                                if (AccountManager.Instance.CurrentAccount.name != name)
                                                                {
                                                                    modalActions.YesNo("The address name was set successfully.\nDo you also want to change the local name for the account?\nThe local name is only visible in this device.", (localChange) =>
                                                                {
                                                                    if (localChange == PromptResult.Success)
                                                                    {
                                                                        if (accountManager.RenameAccount(name))
                                                                        {
                                                                            modalActions.Info($"The local account name was renamed '{name}'.");
                                                                        }
                                                                        else
                                                                        {
                                                                            modalActions.Error("Was not possible to rename the local account.\nHowever the public address was renamed with success.");
                                                                        }
                                                                    }
                                                                });
                                                                }
                                                            }
                                                            else
                                                            {
                                                                modalActions.Error("An error occured when trying to setup the address name.");
                                                            }
                                                        });

                                                    }
                                                });
                                        }
                                        else
                                        {
                                            modalActions.Error("That name is not a valid Phantasma address name.\nNo spaces allowed, only lowercase letters and numbers.\nMust be between 3 and 15 characters in length.");
                                        }
                                    }
                                });
                            }
                            else
                            {
                                modalActions.Error("To register an address name you will need at least some SOUL staked.");
                            }
                            break;
                        }
                    case 3:
                        {
                            if (Input.GetKey(KeyCode.LeftShift))
                            {
                                ShowModal("", "Select chain", ModalState.Input, 1, 10, modalActions.ConfirmCancelOptions, 1, (result, chain) =>
                                {
                                    if (result == PromptResult.Failure)
                                    {
                                        return; // user cancelled
                                    }

                                    ShowModal("", "Enter message", ModalState.Input, 1, -1, modalActions.ConfirmCancelOptions, 4, (result2, message) =>
                                    {
                                        if (result2 == PromptResult.Failure)
                                        {
                                            return; // user cancelled
                                        }

                                        var messageBytes = System.Text.Encoding.ASCII.GetBytes(message);

                                        var hash = Base16.Encode(messageBytes.Sha256());
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
                                            var keys = PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.FromWIF(wif);
                                            signatureBytes = ECDsa.SignDeterministic(messageBytes, keys.PrivateKey, ECDsaCurve.Secp256k1);
                                        }
                                        else if (chain == "Neo Legacy")
                                        {
                                            var keys = PhantasmaPhoenix.InteropChains.Legacy.Neo2.NeoKeys.FromWIF(wif);
                                            signatureBytes = ECDsa.SignDeterministic(messageBytes, keys.PrivateKey, ECDsaCurve.Secp256r1);
                                        }
                                        else
                                        {
                                            modalActions.Error("Unsupported chain");
                                            return;
                                        }

                                        var signature = Base16.Encode(signatureBytes);

                                        modalActions.CopyableMessage("Signature", signature, (result3, input) =>
                                        {
                                            if (result3 != PromptResult.Success)
                                            {
                                                if (accountManager.Settings.devMode)
                                                {
                                                    Log.Write($"Signature: '{signature}'");
                                                }

                                                modalActions.Info("Signature copied to the clipboard.");
                                            }
                                        }, copyValue: signature);
                                    });
                                });

                                modalContext.Hints = new Dictionary<string, string>() { { "Phantasma", "Phantasma" }, { "Ethereum", "Ethereum" }, { "Neo Legacy", "Neo Legacy" } };
                            }
                            else if (Input.GetKey(KeyCode.LeftControl))
                            {
                                ShowModal("", "Select chain", ModalState.Input, 1, 10, modalActions.ConfirmCancelOptions, 1, (result, chain) =>
                                {
                                    if (result == PromptResult.Failure)
                                    {
                                        return; // user cancelled
                                    }

                                    ShowModal("", "Enter message", ModalState.Input, 1, -1, modalActions.ConfirmCancelOptions, 4, (result2, message) =>
                                    {
                                        if (result2 == PromptResult.Failure)
                                        {
                                            return; // user cancelled
                                        }

                                        ShowModal("", "Enter signature", ModalState.Input, 1, -1, modalActions.ConfirmCancelOptions, 4, (result3, signature) =>
                                        {
                                            if (result3 == PromptResult.Failure)
                                            {
                                                return; // user cancelled
                                            }

                                            var messageBytes = System.Text.Encoding.ASCII.GetBytes(message);
                                            var signatureBytes = Base16.Decode(signature);

                                            if (accountManager.Settings.devMode)
                                            {
                                                var hash = Base16.Encode(messageBytes.Sha256());
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
                                                var keys = PhantasmaPhoenix.InteropChains.Legacy.Ethereum.EthereumKey.FromWIF(wif);
                                                verificationResult = ECDsa.Verify(messageBytes, signatureBytes, keys.PublicKey, ECDsaCurve.Secp256k1);
                                            }
                                            else if (chain == "Neo Legacy")
                                            {
                                                var keys = PhantasmaPhoenix.InteropChains.Legacy.Neo2.NeoKeys.FromWIF(wif);
                                                verificationResult = ECDsa.Verify(messageBytes, signatureBytes, keys.PublicKey, ECDsaCurve.Secp256r1);
                                            }
                                            else
                                            {
                                                modalActions.Error("Unsupported chain");
                                                return;
                                            }

                                            if (verificationResult)
                                            {
                                                modalActions.Success("Signature is correct");
                                            }
                                            else
                                            {
                                                modalActions.Error("Signature is incorrect");
                                            }
                                        });
                                    });
                                });

                                modalContext.Hints = new Dictionary<string, string>() { { "Phantasma", "Phantasma" }, { "Ethereum", "Ethereum" }, { "Neo Legacy", "Neo Legacy" } };
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

                                                async Task SendPoaMessageAsync()
                                                {
                                                    try
                                                    {
                                                        await WebClientAsync.PostAsync<string>(url, jsonMessage, CancellationToken.None);
                                                        modalActions.Info("Message sent.");
                                                    }
                                                    catch (Exception)
                                                    {
                                                        modalActions.Error("Error occured. Please try later.");
                                                    }
                                                }

                                                SendPoaMessageAsync().Forget(ex => Log.WriteWarning(ex.ToString()));

                                                if (accountManager.Settings.devMode)
                                                {
                                                    GUIUtility.systemCopyBuffer = message;
                                                    modalActions.Info("Message copied to the clipboard.");
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

        private void StakeSOUL(decimal selectedAmount, string msg, Action<Hash, TransactionResult, string> callback)
        {
            modalActions.YesNo(msg, (result) =>
            {
                if (result == PromptResult.Success)
                {
                    RequestKCAL("SOUL", (kcal) =>
                    {
                        if (kcal == PromptResult.Success)
                        {
                            var planResult = stakeService.BuildStakeDraft(selectedAmount);
                            SendTransactionDraft(planResult, (hash, txResult, error) =>
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
                return new MenuEntry(btnKind, btnKind.ToString(), btnKind != this.CurrentState);
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
            var selectedCount = nftViewPresenter.State.SelectedCount;

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
            DoButton(nftViewPresenter.State.PageNumber > 0, new Rect(halfWidth - pageLabelWidth / 2 - (pageButtonWidth + pageButtonSpacing) * 2,
                                                 VerticalLayout ? (int)rect.y + border : (int)rect.y + border,
                                                 pageButtonWidth, Units(2)), "<<", () =>
            {
                nftViewPresenter.State.GoToFirstPage();
                MarkNftDirty(transferSymbol);
            });

            // <
            DoButton(nftViewPresenter.State.PageNumber > 0, new Rect(halfWidth - pageLabelWidth / 2 - (pageButtonWidth + pageButtonSpacing),
                                                 VerticalLayout ? (int)rect.y + border : (int)rect.y + border,
                                                 pageButtonWidth, Units(2)), "<", () =>
            {
                nftViewPresenter.State.GoToPreviousPage();
                MarkNftDirty(transferSymbol);
            });

            // Current page number
            var style = GUI.skin.GetStyle("Label");
            var prevAlignment = style.alignment;
            style.alignment = TextAnchor.MiddleCenter;

            GUI.Label(new Rect(halfWidth - pageLabelWidth / 2 - 6,
                               (int)rect.y + 12,
                               pageLabelWidth, Units(2)), (nftViewPresenter.State.PageNumber + 1).ToString(), style);

            style.alignment = prevAlignment;

            // >
            DoButton(nftViewPresenter.State.PageNumber < nftViewPresenter.State.PageCount - 1, new Rect(halfWidth + pageLabelWidth / 2 + pageButtonSpacing,
                                                                VerticalLayout ? (int)rect.y + border : (int)rect.y + border,
                                                                pageButtonWidth, Units(2)), ">", () =>
            {
                nftViewPresenter.State.GoToNextPage();
                MarkNftDirty(transferSymbol);
            });

            // >>
            DoButton(nftViewPresenter.State.PageNumber < nftViewPresenter.State.PageCount - 1, new Rect(halfWidth + pageLabelWidth / 2 + pageButtonWidth + pageButtonSpacing * 2,
                                                                VerticalLayout ? (int)rect.y + border : (int)rect.y + border,
                                                                pageButtonWidth, Units(2)), ">>", () =>
            {
                nftViewPresenter.State.GoToLastPage();
                MarkNftDirty(transferSymbol);
            });

            if (CurrentState != GUIState.NftView)
            {
                // To transfer list
                DoButton(selectedCount > 0, new Rect(VerticalLayout ? rect.x + border * 2 : halfWidth + (halfWidth - btnWidth) / 2,
                                        VerticalLayout ? (int)rect.y + border + (Units(2) + 4) : (int)rect.y + border,
                                        VerticalLayout ? rect.width - border * 4 : btnWidth, Units(2)), "To transfer list", () =>
                {
                    PushState(GUIState.NftTransferList);
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
            var selectedCount = nftViewPresenter.State.SelectedCount;

            // Back
            DoButton(true, new Rect(VerticalLayout ? rect.x + border * 2 : (halfWidth - btnWidth) / 2, VerticalLayout ? (int)rect.y + border + (Units(2) + 4) : (int)rect.y + border, VerticalLayout ? rect.width - border * 4 : btnWidth, Units(2)), "Back", () =>
            {
                PushState(GUIState.Nft);
            });

            // Burn
            DoButton(selectedCount > 0, new Rect(VerticalLayout ? rect.x + border * 2 : halfWidth - btnWidth / 2, VerticalLayout ? (int)rect.y + border : (int)rect.y + border, VerticalLayout ? rect.width - border * 4 : btnWidth, Units(2)), "Burn", () =>
            {
                var selectedIds = nftViewPresenter.SelectionSnapshot();
                if (selectedIds.Count == 0)
                {
                    return;
                }

                var burnPrep = burnService.PrepareNftBurn(transferSymbol, selectedIds);
                if (!burnPrep.Success)
                {
                    modalActions.Error(burnPrep.Error);
                    return;
                }

                var confirmMessage = string.IsNullOrEmpty(burnPrep.Message) ? $"Are you sure you want to burn (destroy) {selectedIds.Count} {transferSymbol} NFTs?" : burnPrep.Message;

                modalActions.ConfirmCancel(confirmMessage, (result) =>
                {
                    if (result == PromptResult.Success)
                    {
                        SendTransactionDraft(burnPrep.Data, (hash, txResult, error) =>
                        {
                            TxResultMessage(hash, txResult, error, $"You burned {selectedIds.Count} NFTs!");
                        });
                    }
                }, 10);
            });

            // Send
            DoButton(selectedCount > 0, new Rect(VerticalLayout ? rect.x + border * 2 : halfWidth + (halfWidth - btnWidth) / 2, VerticalLayout ? (int)rect.y + border - (Units(2) + 4) : (int)rect.y + border, VerticalLayout ? rect.width - border * 4 : btnWidth, Units(2)), "Send", () =>
            {
                var selectedIds = nftViewPresenter.SelectionSnapshot();
                if (selectedIds.Count == 0)
                {
                    return;
                }

                var accountManager = AccountManager.Instance;
                var state = accountManager.CurrentState;
                var transferName = $"{transferSymbol} transfer";
                TokenResult transferToken;

                Tokens.GetToken(transferSymbol, accountManager.CurrentPlatform, out transferToken);

                if (string.IsNullOrEmpty(transferToken.Flags))
                {
                    modalActions.Error($"Operations with token {transferSymbol} are not supported yet in this version.");
                    return;
                }

                if (!transferToken.IsTransferable())
                {
                    modalActions.Error($"Transfers of {transferSymbol} tokens are not allowed.");
                    return;
                }

                ShowModal(transferName, "Enter destination address", ModalState.Input, 3, 64, modalActions.ConfirmCancelOptions, 1, (result, destAddress) =>
                {
                    if (result == PromptResult.Failure)
                    {
                        return; // user canceled
                    }

                    var ethereumAddressUtil = new PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Util.AddressUtil();

                    if (Address.IsValidAddress(destAddress) && accountManager.CurrentPlatform.ValidateTransferTarget(transferToken, PlatformKind.Phantasma))
                    {
                        if (accountManager.CurrentPlatform == PlatformKind.Phantasma)
                        {
                            ContinuePhantasmaNftTransfer(transferName, transferSymbol, destAddress);
                        }
                        else
                        {
                            modalActions.Error($"Direct transfers from {accountManager.CurrentPlatform} to this type of address not supported.");
                        }
                    }
                    else
                    if (PhantasmaPhoenix.InteropChains.Legacy.Neo2.NeoUtils.IsValidAddress(destAddress))
                    {
                        modalActions.Error($"Direct transfers from {accountManager.CurrentPlatform} to Neo address not supported.");
                    }
                    else
                    if (ethereumAddressUtil.IsValidEthereumAddressHexFormat(destAddress) && ethereumAddressUtil.IsChecksumAddress(destAddress))
                    {
                        modalActions.Error($"Direct transfers from {accountManager.CurrentPlatform} to Ethereum/BSC address not supported.");
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
                                modalActions.Error("No account with such name exists.");
                            }
                        });
                    }
                    else
                    {
                        modalActions.Error("Invalid destination address.");
                    }
                });

                var hints = accountHintsService.BuildAccountHints(accountManager.CurrentPlatform.GetTransferTargets(transferToken));
                hints["Scan QR"] = $"|{GUIState.ScanQR}";
                modalContext.Hints = hints;
            });

            return posY;
        }

        private Action<Hash, TransactionResult, string> transactionCallback;

        public void SendTransactionDraft(WalletTransactionDraftResult draftResult, Action<Hash, TransactionResult, string> callback, bool refreshBalanceAfterConfirmation = true)
        {
            if (draftResult == null || !draftResult.Success || draftResult.Draft == null)
            {
                var errorMessage = draftResult?.Error ?? "Invalid transaction draft.";
                modalActions.Error(errorMessage);
                callback?.Invoke(Hash.Null, null, errorMessage);
                return;
            }

            SendTransactionDraft(draftResult.Draft, callback, refreshBalanceAfterConfirmation);
        }

        public void SendTransactionDraft(WalletTransactionDraft draft, Action<Hash, TransactionResult, string> callback, bool refreshBalanceAfterConfirmation = true)
        {
            transactionOrchestrator.SendTransactionDraft(draft, refreshBalanceAfterConfirmation, callback);
        }

        private void InvokeTransactionCallback(Hash hash, TransactionResult txResult, string error)
        {
            var temp = transactionCallback;
            transactionCallback = null;
            temp?.Invoke(hash, txResult, error);
        }

        private void ShowConfirmationScreen(Hash hash, bool refreshBalanceAfterConfirmation, Action<Hash, TransactionResult, string> callback)
        {
            transactionCallback = callback;
            transactionStillPending = true;
            transactionCheckCount = 0;
            transactionHash = hash;
            transactionLastCheck = DateTime.UtcNow;
            this.refreshBalanceAfterConfirmation = refreshBalanceAfterConfirmation;

            if (CurrentState == GUIState.Sending)
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
            var availability = transferService.GetFungibleAvailability(symbol);
            if (!availability.Success)
            {
                modalActions.Error(availability.Error);
                return;
            }

            modalActions.RequireAmount(transferName, destAddress, symbol, availability.Data1, availability.Data2, (amount) =>
            {
                var planResult = transferService.BuildFungibleTransferDraft(symbol, amount, destAddress);
                if (!planResult.Success)
                {
                    modalActions.Error(planResult.Error);
                    return;
                }

                var plan = planResult.Draft;
                var amountSent = planResult.Amount;

                SendTransactionDraft(plan, (hash, txResult, error) =>
                {
                    TxResultMessage(hash, txResult, error, $"You transferred {WalletAmountFormatter.Format(amountSent, MoneyFormatType.Long)} {symbol}!\n\nThe transaction has successfully completed, but it may take up to 30 seconds until the change is reflected in your wallet balance\n");
                });
            });
        }

        private void ContinuePhantasmaNftTransfer(string transferName, string symbol, string destAddress)
        {
            var accountManager = AccountManager.Instance;
            var selectedIds = nftViewPresenter.SelectionSnapshot();
            var planResult = nftTransferService.BuildNftTransferDraft(symbol, destAddress, selectedIds);
            if (!planResult.Success)
            {
                modalActions.Error(planResult.Error);
                return;
            }

            SendTransactionDraft(planResult, (hash, txResult, error) =>
            {
                if (string.IsNullOrEmpty(error) && hash != Hash.Null)
                {
                    TxResultMessage(hash, txResult, error, $"You transferred {WalletAmountFormatter.Format(planResult.Amount, MoneyFormatType.Long)} {symbol}!\n\nThe transaction has successfully completed, but it may take up to 30 seconds until the change is reflected in your wallet balance\n");

                    // Removing sent NFTs from current NFT list.
                    var nfts = accountManager.CurrentNfts;
                    foreach (var nft in selectedIds)
                    {
                        nfts.Remove(nfts.Find(x => x.Id == nft));
                    }

                    // Returning to NFT's first screen.
                    nftScroll = Vector2.zero;
                    nftViewPresenter.ClearSelection();
                    PushState(GUIState.Nft);
                }
                else
                {
                    TxResultMessage(hash, txResult, error, null, "Some or all transactions failed.");
                }
            });
        }

        private void RequestKCAL(string forSymbol, Action<PromptResult> callback)
        {
            feeRequirement.EnsureKcal(0.1m, (result, error) =>
            {
                if (result == PromptResult.Failure && !string.IsNullOrEmpty(error))
                {
                    modalActions.Error(error);
                }

                callback?.Invoke(result);
            });
        }

        #endregion


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

        #region Transaction UI bridge
        public void RequestPassword(string description, PlatformKind platform, Action<PromptResult> callback)
        {
            RequestPassword(description, platform, false, false, callback, false);
        }

        public void ShowSendProgress(string description, int txCount, Action<PromptResult> callback)
        {
            modalActions.SendCancel(description, callback);
        }

        public void PushSendingState()
        {
            PushState(GUIState.Sending);
        }

        public void PopSendingState()
        {
            PopState();
        }

        public void ShowConfirmation(Hash hash, bool refreshBalanceAfterConfirmation, Action<Hash, TransactionResult, string> callback)
        {
            ShowConfirmationScreen(hash, refreshBalanceAfterConfirmation, callback);
        }

        public void ShowError(string message)
        {
            modalActions.Error(message);
        }
        #endregion
        #endregion

        #region DAPP Interface
        public Address GetAddress()
        {
            return Address.Parse(AccountManager.Instance.CurrentState.address);
        }

        public Dictionary<string, decimal> GetBalances(string chain)
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
