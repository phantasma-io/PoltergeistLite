#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_WIN || UNITY_EDITOR_LINUX
#define UITK_SCREEN_TOOLS_SUPPORTED
#endif

using System;
using UnityEngine;
using Poltergeist.Wallet;
using Poltergeist.UiToolkit.Balances;
using Poltergeist.UiToolkit.Accounts;
using UnityEngine.UIElements;
using Poltergeist;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist.UiToolkit.History;
using Poltergeist.UiToolkit.Settings;
using UnityEngine.EventSystems;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Protocol;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Poltergeist.UiToolkit
{
    /// <summary>
    /// Runtime entry point for the new UI Toolkit wallet experience.
    /// </summary>
    public sealed class WalletUiToolkitRoot : MonoBehaviour
    {
        private const string LogPrefix = "[UITK] ";
        private const string FatalPrefix = "[UITK] ";

        private static WalletUiToolkitRoot instance;
        public static bool IsActive { get; private set; }

        private WalletAccountsView accountsView;
        private WalletBalancesView balancesView;
        private WalletTokenDashboardView tokenView;
        private WalletNftDashboardView nftView;
        private WalletHistoryView historyView;
        private WalletAccountView accountView;
        private WalletSettingsView settingsView;
        private UIDocument document;
        private PanelSettings panelSettings;
        private WalletUiPreviewHelper previewHelper;
#if UITK_SCREEN_TOOLS_SUPPORTED
        private WalletUiScreenshotHelper screenshotHelper;
#endif
        private VisualElement accountsRoot;
        private VisualElement balancesRoot;
        private VisualElement tokenRoot;
        private VisualElement nftRoot;
        private VisualElement historyRoot;
        private VisualElement accountRoot;
        private VisualElement settingsRoot;
        private WalletUiModalHost modalHost;
        private WalletUiToolkitBridge uiBridge;
        private bool initializationFailed;
        private static bool cacheInitialized;
        private CancellationTokenSource accountsReadyCts;
        private float nextMessageCheckTime;
        private bool settingsForcedDueToRpcFailure;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var existing = UnityEngine.Object.FindFirstObjectByType<WalletUiToolkitRoot>();
            if (existing != null)
            {
                instance = existing;
                DontDestroyOnLoad(existing.gameObject);
                Log.Write($"{LogPrefix}Bootstrap skipped, root already exists (id={existing.GetInstanceID()}).");
                return;
            }

            var root = new GameObject("WalletUiToolkitRoot");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<WalletUiToolkitRoot>();
            Log.Write($"{LogPrefix}Bootstrap complete, root created.");
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Log.Write($"{LogPrefix}Duplicate UITK root detected, destroying new instance (id={GetInstanceID()}, existing={instance.GetInstanceID()}).");
                Destroy(gameObject);
                return;
            }

            instance = this;
            IsActive = true;
            DontDestroyOnLoad(gameObject);
            Log.Write($"{LogPrefix}Awake");
            previewHelper = new WalletUiPreviewHelper();
#if UITK_SCREEN_TOOLS_SUPPORTED
            screenshotHelper = new WalletUiScreenshotHelper(this, () => AccountManager.Instance?.Settings, previewHelper);
#endif

            try
            {
                EnsureAccountManagerHost();
                EnsureCacheReady();
                EnsureEventSystem();
                EnsurePanelSettings();
                EnsureDocument();
                if (!InitializeViewsSafe())
                {
                    return;
                }
                accountsReadyCts = new CancellationTokenSource();
                WaitForAccountsReadyAsync(accountsReadyCts.Token).Forget(ex => Log.WriteWarning($"{FatalPrefix}WaitForAccountsReady failed: {ex}"));
            }
            catch (Exception e)
            {
                initializationFailed = true;
                Log.WriteWarning($"{FatalPrefix}Failed to initialize UITK root: {e}");

                if (document == null)
                {
                    try
                    {
                        EnsurePanelSettings();
                        EnsureDocument();
                    }
                    catch (Exception ensureEx)
                    {
                        Log.WriteWarning($"{FatalPrefix}Unable to ensure UIDocument after failure: {ensureEx}");
                    }
                }

                ShowFatal($"UITK failed to start:\n{e.Message}");
            }
        }

        private void OnEnable()
        {
            if (initializationFailed)
            {
                return;
            }

            Log.Write($"{LogPrefix}OnEnable");
            Application.logMessageReceived += OnLogMessageReceived;
            WalletApplicationContext.Instance?.UiSignals?.EnsureSubscribed();
        }

        private void OnDestroy()
        {
            if (initializationFailed)
            {
                return;
            }

            Log.Write($"{LogPrefix}OnDestroy");
            Application.logMessageReceived -= OnLogMessageReceived;
            previewHelper?.RestorePreviewWindowSize();
            accountsView?.Dispose();
            balancesView?.Dispose();
            tokenView?.Dispose();
            historyView?.Dispose();
            accountView?.Dispose();
            settingsView?.Dispose();
            // Avoid leaving a stale WalletLink bridge when this root is destroyed (e.g., scene reload or duplicate cleanup).
            if (uiBridge != null)
            {
                WalletUiBridge.Unregister(uiBridge);
            }
            uiBridge?.Dispose();
            if (accountsReadyCts != null && !accountsReadyCts.IsCancellationRequested)
            {
                accountsReadyCts.Cancel();
            }
            accountsReadyCts?.Dispose();
            accountsReadyCts = null;
            accountsView = null;
            balancesView = null;
            tokenView = null;
            historyView = null;
            accountView = null;
            settingsView = null;
            uiBridge = null;
            if (instance == this)
            {
                instance = null;
                IsActive = false;
            }
        }

        private void EnsureAccountManagerHost()
        {
            var instance = AccountManager.Instance;
            if (instance != null)
            {
                if (!instance.enabled)
                {
                    instance.enabled = true;
                    Log.Write($"{LogPrefix}AccountManager found but disabled; enabling.");
                }

                if (!instance.gameObject.activeSelf)
                {
                    instance.gameObject.SetActive(true);
                    Log.Write($"{LogPrefix}AccountManager GameObject was inactive; reactivating.");
                }

                Log.Write($"{LogPrefix}AccountManager already present.");
                return;
            }

            var host = new GameObject("AccountManagerHost_UITK");
            DontDestroyOnLoad(host);
            host.AddComponent<AccountManager>();
            Log.Write($"{LogPrefix}Spawned AccountManagerHost_UITK.");
        }

        private void EnsureCacheReady()
        {
            if (cacheInitialized)
            {
                return;
            }

            try
            {
                Cache.Init("cache");
                cacheInitialized = true;
                Log.Write($"{LogPrefix}Cache initialized for UITK mode.");
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{FatalPrefix}Failed to initialize cache: {e}");
            }
        }

        private bool InitializeViewsSafe()
        {
            try
            {
                InitializeViews();
                WalletApplicationContext.Instance.UiSignals?.EnsureSubscribed();
                EnsureInitialScreen();
                Log.Write($"{LogPrefix}UI initialized (no wait). Accounts ready: {AccountManager.Instance?.AccountsAreReadyToBeUsed}");
                return true;
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{FatalPrefix}InitializeViews failed: {e}");
                ShowFatal($"UITK failed to start:\n{e.Message}");
                initializationFailed = true;
                return false;
            }
        }

        private async Task WaitForAccountsReadyAsync(CancellationToken token)
        {
            const int maxAttempts = 150;
            const float delaySeconds = 0.1f;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var am = AccountManager.Instance;
                if (am != null && am.AccountsAreReadyToBeUsed)
                {
                    Log.Write($"{LogPrefix}AccountManager became ready after wait ({attempt + 1} ticks). accounts={am.Accounts?.Count ?? 0}");
                    accountsView?.Refresh();
                    if (ShouldForceSettings(am))
                    {
                        ShowSettings();
                        settingsView?.OnAccountsReady();
                        return;
                    }
                    if (am.HasSelection && (!am.CurrentAccount.passwordProtected || !string.IsNullOrEmpty(am.CurrentPasswordHash)))
                    {
                        ShowBalances();
                        balancesView?.OnAccountsReady();
                        historyView?.OnAccountsReady();
                        accountView?.OnAccountsReady();
                        tokenView?.OnAccountsReady();
                        settingsView?.OnAccountsReady();
                    }
                    else
                    {
                        ShowAccounts();
                    }
                    return;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                }
                catch (OperationCanceledException)
                {
                    Log.Write($"{LogPrefix}WaitForAccountsReady cancelled.");
                    return;
                }
            }

            Log.WriteWarning($"{LogPrefix}AccountManager did not become ready in time; balances view may stay empty.");
        }

        private void EnsurePanelSettings()
        {
            if (panelSettings != null)
            {
                return;
            }

            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "WalletUiToolkitPanelSettings";
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            previewHelper?.ApplyPanelScale(panelSettings, AccountManager.Instance?.Settings, Application.platform);
            panelSettings.sortingOrder = 2000;
            panelSettings.targetDisplay = 0;
        }

        internal static void RefreshPanelScale()
        {
            var root = instance;
            if (root == null || root.panelSettings == null || root.previewHelper == null)
            {
                return;
            }

            var settings = AccountManager.Instance?.Settings;
            root.previewHelper.ApplyPanelScale(root.panelSettings, settings, Application.platform);
            root.previewHelper.ApplyPreviewViewport(root.document, settings, Application.platform);
        }

        private void EnsureDocument()
        {
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var go = new GameObject("EventSystem_UITK");
            DontDestroyOnLoad(go);
            var es = go.AddComponent<EventSystem>();
            // Prefer standalone input to avoid Input System dependency surprises; add if missing.
            if (go.GetComponent<StandaloneInputModule>() == null)
            {
                go.AddComponent<StandaloneInputModule>();
            }

            Log.Write($"{LogPrefix}EventSystem created for UITK (id={es.GetInstanceID()}).");
        }

        private void InitializeViews()
        {
            var context = WalletApplicationContext.Instance;
            if (context == null)
            {
                throw new InvalidOperationException("WalletApplicationContext is not ready.");
            }

            var root = document.rootVisualElement;
            root.Clear();
            root.style.position = Position.Relative;
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1;
            root.style.flexShrink = 1;
            root.style.flexBasis = 0;
            root.style.width = new Length(100, LengthUnit.Percent);
            root.style.height = new Length(100, LengthUnit.Percent);
            root.style.minHeight = 0;
            root.style.minWidth = 0;
            root.style.alignItems = Align.Stretch;
            root.style.overflow = Overflow.Hidden;
            previewHelper?.ApplyPreviewViewport(document, AccountManager.Instance?.Settings, Application.platform);
            WalletUiCommon.ApplyCardStyle(root, WalletUiTheme.GetScreenGradientTexture(), 0f, WalletUiTheme.ScreenBackground, WalletUiTheme.ScreenBackground, WalletUiTheme.ScreenBackground, 0f);

            accountsRoot = new VisualElement { style = { flexGrow = 1, display = DisplayStyle.Flex, backgroundColor = Color.clear } };
            balancesRoot = new VisualElement { style = { flexGrow = 1, display = DisplayStyle.None, backgroundColor = Color.clear } };
            tokenRoot = new VisualElement { style = { flexGrow = 1, display = DisplayStyle.None, backgroundColor = Color.clear } };
            nftRoot = new VisualElement { style = { flexGrow = 1, display = DisplayStyle.None, backgroundColor = Color.clear } };
            historyRoot = new VisualElement { style = { flexGrow = 1, display = DisplayStyle.None, backgroundColor = Color.clear } };
            accountRoot = new VisualElement { style = { flexGrow = 1, display = DisplayStyle.None, backgroundColor = Color.clear } };
            settingsRoot = new VisualElement { style = { flexGrow = 1, display = DisplayStyle.None, backgroundColor = Color.clear } };

            modalHost = new WalletUiModalHost(root, WalletUiCommon.ApplyDefaultFont);
            uiBridge = new WalletUiToolkitBridge(context, modalHost);
            WalletUiBridge.Register(uiBridge);
            Log.Write($"{LogPrefix}WalletLink UI bridge registered for UITK.");

            accountsView = new WalletAccountsView(accountsRoot, context, modalHost, ShowBalances, ShowSettings);
            balancesView = new WalletBalancesView(balancesRoot, context, ShowBalances, ShowHistory, ShowAccount, ShowSettings, ExitToWallets, ShowToken);
            tokenView = new WalletTokenDashboardView(tokenRoot, context, modalHost, ShowBalances, ShowHistory, ShowAccount, ShowSettings, ExitToWallets, accountsView);
            nftView = new WalletNftDashboardView(nftRoot, context, modalHost, ShowBalances, ShowHistory, ShowAccount, ShowSettings, ExitToWallets, accountsView);
            historyView = new WalletHistoryView(historyRoot, context, ShowBalances, ShowHistory, ShowAccount, ShowSettings, ExitToWallets);
            accountView = new WalletAccountView(accountRoot, context, modalHost, accountsView, ShowBalances, ShowHistory, ShowAccount, ShowSettings, ExitToWallets);
            settingsView = new WalletSettingsView(settingsRoot, context, modalHost, ExitToWallets, OpenDebugNftAsync);
            EnsureUiBridgeRegistered();

            root.Add(accountsRoot);
            root.Add(balancesRoot);
            root.Add(tokenRoot);
            root.Add(nftRoot);
            root.Add(historyRoot);
            root.Add(accountRoot);
            root.Add(settingsRoot);

            modalHost.BringToFront(root);

            Log.Write($"{LogPrefix}Views initialized (accounts + balances + token + nft + history + account + settings).");
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                Log.WriteWarning($"{LogPrefix}Unity {type}: {condition}\n{stackTrace}");
            }
        }

        private void Update()
        {
            if (initializationFailed || modalHost == null)
            {
                return;
            }

#if UITK_SCREEN_TOOLS_SUPPORTED
            screenshotHelper?.HandleHotkey();
#endif

            // Poll pending messages on a short interval to keep modal traffic responsive without doing work every frame.
            if (Time.unscaledTime < nextMessageCheckTime)
            {
                return;
            }

            nextMessageCheckTime = Time.unscaledTime + 0.35f;
            ProcessMessagesAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Message pump failed: {ex}"));
        }

        private async Task ProcessMessagesAsync()
        {
            if (modalHost == null || modalHost.IsBusy)
            {
                return;
            }

            // Surface RPC connectivity failures first.
            var accountManager = AccountManager.Instance;
            var settings = accountManager?.Settings;

            // Reset guard once the user fixes settings.
            if (settingsForcedDueToRpcFailure && settings != null && !settings.settingRequireReconfiguration)
            {
                settingsForcedDueToRpcFailure = false;
            }

            if (accountManager != null)
            {
                // If tokens/bootstrap failed and settings require reconfiguration, force the Settings screen even if the failure happened after initial init.
                if (settings != null && settings.settingRequireReconfiguration && !settingsForcedDueToRpcFailure)
                {
                    settingsForcedDueToRpcFailure = true;
                    Log.Write($"{LogPrefix}Forcing settings view due to RPC configuration failure.");
                    ShowSettings();
                    await WalletUiModalHelper.ShowErrorAsync(modalHost, "Connection failed", "Cannot reach the configured RPC endpoint. Please review your settings.");
                    return;
                }

                if (accountManager.ReportGetPeersFailure)
                {
                    accountManager.ReportGetPeersFailure = false;
                    Log.Write($"{LogPrefix}Showing RPC list failure warning.");
                    await WalletUiModalHelper.ShowErrorAsync(modalHost, "Warning", "Couldn't load RPCs list.\nWallet might malfunction.");
                    return;
                }

                if (accountManager.ReportAllRpcsUnavailabe)
                {
                    accountManager.ReportAllRpcsUnavailabe = false;
                    Log.Write($"{LogPrefix}Showing all RPCs unavailable warning.");
                    await WalletUiModalHelper.ShowErrorAsync(modalHost, "Warning", "All Phantasma RPC servers are unavailable.\nPlease check your network connection.");
                    return;
                }
            }

            var queue = WalletApplicationContext.Instance?.Messages;
            if (queue == null)
            {
                return;
            }

            if (!queue.TryDequeue(out var message))
            {
                return;
            }

            var title = string.IsNullOrWhiteSpace(message.Title) ? "Message" : message.Title;
            var body = string.IsNullOrWhiteSpace(message.Body) ? string.Empty : message.Body;
            switch (message.Kind)
            {
                case MessageKind.Error:
                    Log.Write($"{LogPrefix}Showing queued error: {title} | {body}");
                    await WalletUiModalHelper.ShowErrorAsync(modalHost, title, body);
                    break;
                case MessageKind.Success:
                    Log.Write($"{LogPrefix}Showing queued success: {title} | {body}");
                    await WalletUiModalHelper.ShowInfoAsync(modalHost, string.IsNullOrWhiteSpace(message.Title) ? "Success" : message.Title, body);
                    break;
                default:
                    Log.Write($"{LogPrefix}Showing queued message: {title} | {body}");
                    await WalletUiModalHelper.ShowInfoAsync(modalHost, title, body);
                    break;
            }
        }

        private void EnsureInitialScreen()
        {
            var am = AccountManager.Instance;
            if (ShouldForceSettings(am) && am.AccountsAreReadyToBeUsed)
            {
                ShowSettings();
                settingsView?.OnAccountsReady();
                return;
            }

            if (am != null && am.HasSelection && (!am.CurrentAccount.passwordProtected || !string.IsNullOrEmpty(am.CurrentPasswordHash)))
            {
                ShowBalances();
                balancesView?.ForceRefresh();
                accountView?.OnAccountsReady();
                settingsView?.OnAccountsReady();
            }
            else
            {
                ShowAccounts();
                accountsView?.Refresh();
            }
        }

        private void ShowAccounts()
        {
            WalletUiBridge.Unregister(uiBridge);
            if (accountsRoot != null)
            {
                accountsRoot.style.display = DisplayStyle.Flex;
            }
            accountsView?.ClearStatus();

            if (balancesRoot != null)
            {
                balancesRoot.style.display = DisplayStyle.None;
            }

            if (tokenRoot != null)
            {
                tokenRoot.style.display = DisplayStyle.None;
            }

            if (nftRoot != null)
            {
                nftRoot.style.display = DisplayStyle.None;
            }

            if (historyRoot != null)
            {
                historyRoot.style.display = DisplayStyle.None;
            }

            if (accountRoot != null)
            {
                accountRoot.style.display = DisplayStyle.None;
            }
            if (settingsRoot != null)
            {
                settingsRoot.style.display = DisplayStyle.None;
            }

            Log.Write($"{LogPrefix}ShowAccounts done. accountsVisible={accountsRoot?.style.display}");
        }

        private void ShowBalances()
        {
            EnsureUiBridgeRegistered();
            if (accountsRoot != null)
            {
                accountsRoot.style.display = DisplayStyle.None;
            }

            if (balancesRoot != null)
            {
                balancesRoot.style.display = DisplayStyle.Flex;
            }

            if (tokenRoot != null)
            {
                tokenRoot.style.display = DisplayStyle.None;
            }

            if (nftRoot != null)
            {
                nftRoot.style.display = DisplayStyle.None;
            }

            if (historyRoot != null)
            {
                historyRoot.style.display = DisplayStyle.None;
            }

            if (accountRoot != null)
            {
                accountRoot.style.display = DisplayStyle.None;
            }
            if (settingsRoot != null)
            {
                settingsRoot.style.display = DisplayStyle.None;
            }

            Log.Write($"{LogPrefix}ShowBalances invoked; refreshing balances view.");
            balancesView?.MarkAsActive();
            balancesView?.OnAccountsReady();
            balancesView?.ForceRefresh();
            accountView?.OnAccountsReady();
            settingsView?.OnAccountsReady();
            Log.Write($"{LogPrefix}ShowBalances done. balancesVisible={balancesRoot?.style.display} accountsVisible={accountsRoot?.style.display}");
        }

        private void ShowToken(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                Log.WriteWarning($"{LogPrefix}ShowToken called with empty symbol.");
                ShowBalances();
                return;
            }

            EnsureUiBridgeRegistered();
            var isFungible = IsFungibleSymbol(symbol);

            if (accountsRoot != null)
            {
                accountsRoot.style.display = DisplayStyle.None;
            }

            if (balancesRoot != null)
            {
                balancesRoot.style.display = DisplayStyle.None;
            }

            if (tokenRoot != null)
            {
                tokenRoot.style.display = isFungible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (nftRoot != null)
            {
                nftRoot.style.display = isFungible ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (historyRoot != null)
            {
                historyRoot.style.display = DisplayStyle.None;
            }

            if (accountRoot != null)
            {
                accountRoot.style.display = DisplayStyle.None;
            }

            if (settingsRoot != null)
            {
                settingsRoot.style.display = DisplayStyle.None;
            }

            if (isFungible)
            {
                tokenView?.ShowToken(symbol);
                tokenView?.MarkAsActive();
                tokenView?.OnAccountsReady();
            }
            else
            {
                nftView?.ShowToken(symbol);
                nftView?.MarkAsActive();
                nftView?.OnAccountsReady();
            }

            Log.Write($"{LogPrefix}ShowToken done. symbol={symbol} fungible={isFungible} tokenVisible={tokenRoot?.style.display} nftVisible={nftRoot?.style.display}");
        }

        private void ShowNftDashboard()
        {
            EnsureUiBridgeRegistered();
            if (accountsRoot != null)
            {
                accountsRoot.style.display = DisplayStyle.None;
            }

            if (balancesRoot != null)
            {
                balancesRoot.style.display = DisplayStyle.None;
            }

            if (tokenRoot != null)
            {
                tokenRoot.style.display = DisplayStyle.None;
            }

            if (nftRoot != null)
            {
                nftRoot.style.display = DisplayStyle.Flex;
            }

            if (historyRoot != null)
            {
                historyRoot.style.display = DisplayStyle.None;
            }

            if (accountRoot != null)
            {
                accountRoot.style.display = DisplayStyle.None;
            }

            if (settingsRoot != null)
            {
                settingsRoot.style.display = DisplayStyle.None;
            }

            nftView?.MarkAsActive();
            nftView?.OnAccountsReady();
            Log.Write($"{LogPrefix}ShowNftDashboard done. nftVisible={nftRoot?.style.display}");
        }

        private bool IsFungibleSymbol(string symbol)
        {
            try
            {
                var ctx = WalletApplicationContext.Instance;
                var snapshot = ctx?.ViewState?.GetBalancesSnapshot(() => ctx.BalancePresenter.BuildSnapshot());
                var entry = snapshot?.Balances?.FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                {
                    return entry.Fungible;
                }

                var platform = AccountManager.Instance?.CurrentPlatform ?? PlatformKind.None;
                var token = Tokens.GetToken(symbol, platform);
                return token?.IsFungible() ?? true;
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{LogPrefix}Failed to detect asset type for {symbol}: {e}");
                return true;
            }
        }

        private async Task<ValidationResult> OpenDebugNftAsync(string symbol, string tokenId)
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                return ValidationResult.Fail("Account manager is not available.");
            }

            // Debug NFT loading is RPC-only, so it can run without selecting/unlocking a wallet.
            if (accountManager.CurrentPlatform == PlatformKind.None)
            {
                // Ensure a deterministic platform key for NFT cache/ROM lookups.
                accountManager.CurrentPlatform = PlatformKind.Phantasma;
            }

            var result = await accountManager.LoadDebugNftAsync(symbol, tokenId);
            if (!result.Success)
            {
                return ValidationResult.Fail(result.Error);
            }

            var viewState = WalletApplicationContext.Instance?.ViewState;
            if (viewState != null)
            {
                // Keep the NFT dashboard in debug mode so it can render without a wallet selection.
                viewState.IsDebugNftActive = true;
            }
            ShowNftDashboard();
            nftView?.ShowDebugNft(symbol, tokenId);
            return ValidationResult.Ok("Debug NFT opened.");
        }

        private void ShowHistory()
        {
            EnsureUiBridgeRegistered();
            if (accountsRoot != null)
            {
                accountsRoot.style.display = DisplayStyle.None;
            }

            if (balancesRoot != null)
            {
                balancesRoot.style.display = DisplayStyle.None;
            }

            if (historyRoot != null)
            {
                historyRoot.style.display = DisplayStyle.Flex;
            }

            if (tokenRoot != null)
            {
                tokenRoot.style.display = DisplayStyle.None;
            }

            if (nftRoot != null)
            {
                nftRoot.style.display = DisplayStyle.None;
            }

            if (accountRoot != null)
            {
                accountRoot.style.display = DisplayStyle.None;
            }
            if (settingsRoot != null)
            {
                settingsRoot.style.display = DisplayStyle.None;
            }

            historyView?.MarkAsActive();
            historyView?.ForceRefresh();
            historyView?.OnAccountsReady();
            accountView?.OnAccountsReady();
            settingsView?.OnAccountsReady();
            Log.Write($"{LogPrefix}ShowHistory done. historyVisible={historyRoot?.style.display} balancesVisible={balancesRoot?.style.display}");
        }

        private void ShowAccount()
        {
            EnsureUiBridgeRegistered();
            if (accountsRoot != null)
            {
                accountsRoot.style.display = DisplayStyle.None;
            }

            if (balancesRoot != null)
            {
                balancesRoot.style.display = DisplayStyle.None;
            }

            if (tokenRoot != null)
            {
                tokenRoot.style.display = DisplayStyle.None;
            }

            if (nftRoot != null)
            {
                nftRoot.style.display = DisplayStyle.None;
            }

            if (historyRoot != null)
            {
                historyRoot.style.display = DisplayStyle.None;
            }

            if (accountRoot != null)
            {
                accountRoot.style.display = DisplayStyle.Flex;
            }
            if (settingsRoot != null)
            {
                settingsRoot.style.display = DisplayStyle.None;
            }

            accountView?.OnAccountsReady();
            accountView?.MarkAsActive();
            Log.Write($"{LogPrefix}ShowAccount done. accountVisible={accountRoot?.style.display}");
        }

        private void ShowSettings()
        {
            EnsureUiBridgeRegistered();
            if (accountsRoot != null)
            {
                accountsRoot.style.display = DisplayStyle.None;
            }

            if (balancesRoot != null)
            {
                balancesRoot.style.display = DisplayStyle.None;
            }

            if (historyRoot != null)
            {
                historyRoot.style.display = DisplayStyle.None;
            }

            if (tokenRoot != null)
            {
                tokenRoot.style.display = DisplayStyle.None;
            }

            if (nftRoot != null)
            {
                nftRoot.style.display = DisplayStyle.None;
            }

            if (accountRoot != null)
            {
                accountRoot.style.display = DisplayStyle.None;
            }

            if (settingsRoot != null)
            {
                settingsRoot.style.display = DisplayStyle.Flex;
            }

            settingsView?.OnAccountsReady();
            settingsView?.MarkAsActive();
            Log.Write($"{LogPrefix}ShowSettings done. settingsVisible={settingsRoot?.style.display}");
        }

        private static bool ShouldForceSettings(AccountManager accountManager)
        {
            var settings = accountManager?.Settings;
            if (settings == null)
            {
                return false;
            }

            return settings.nexusKind == NexusKind.Unknown || settings.settingRequireReconfiguration;
        }

        private void ExitToWallets()
        {
            var am = AccountManager.Instance;
            am?.UnselectAcount();
            WalletApplicationContext.Instance.ViewState.ResetSnapshots();
            ShowAccounts();
        }

        private void ShowFatal(string message)
        {
            if (document == null)
            {
                return;
            }

            var root = document.rootVisualElement;
            if (root == null)
            {
                return;
            }

            root.Clear();
            var label = new Label(message ?? "UITK failed to start.")
            {
                style =
                {
                    unityTextAlign = TextAnchor.MiddleCenter,
                    fontSize = 16,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = Color.white,
                    paddingTop = 12,
                    paddingBottom = 12
                }
            };
            root.Add(label);
        }

        private void EnsureUiBridgeRegistered()
        {
            // WalletLink should only be active while a wallet view is shown; re-register on entry to those screens.
            if (uiBridge != null && !ReferenceEquals(WalletUiBridge.Current, uiBridge))
            {
                WalletUiBridge.Register(uiBridge);
            }
        }
    }
}
