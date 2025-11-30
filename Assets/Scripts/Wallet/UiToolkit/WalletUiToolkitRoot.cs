using System;
using UnityEngine;
using System.Collections;
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
using UnityEngine.SceneManagement;

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
            Log.Write($"{LogPrefix}Bootstrap complete, root created (UITK forced on, legacy UI will be disabled).");
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

            try
            {
                EnsureAccountManagerHost();
                EnsureCacheReady();
                EnsureEventSystem();
                DisableLegacyUi("UITK bootstrap");
                EnsurePanelSettings();
                EnsureDocument();
                if (!InitializeViewsSafe())
                {
                    return;
                }
                StartCoroutine(WaitForAccountsReady());
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
            SceneManager.sceneLoaded += OnSceneLoaded;
            WalletApplicationContext.Instance?.UiSignals?.EnsureSubscribed();
            TryDisableLegacyUi("UITK root enabled");
        }

        private void OnDestroy()
        {
            if (initializationFailed)
            {
                return;
            }

            Log.Write($"{LogPrefix}OnDestroy");
            Application.logMessageReceived -= OnLogMessageReceived;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            accountsView?.Dispose();
            balancesView?.Dispose();
            tokenView?.Dispose();
            historyView?.Dispose();
            accountView?.Dispose();
            settingsView?.Dispose();
            uiBridge?.Dispose();
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

        private IEnumerator WaitForAccountsReady()
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
                        yield break;
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
                    yield break;
                }

                yield return new WaitForSeconds(delaySeconds);
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
            // Legacy UI was landscape-first; use a matching reference resolution so sizing lines up.
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.match = 0.5f;
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.sortingOrder = 2000;
            panelSettings.targetDisplay = 0;
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
            balancesView = new WalletBalancesView(balancesRoot, context, DisableLegacyUi, ShowBalances, ShowHistory, ShowAccount, ShowSettings, ExitToWallets, ShowToken);
            tokenView = new WalletTokenDashboardView(tokenRoot, context, modalHost, ShowBalances, ShowHistory, ShowAccount, ShowSettings, ExitToWallets, accountsView);
            nftView = new WalletNftDashboardView(nftRoot, context, modalHost, ShowBalances, ShowHistory, ShowAccount, ShowSettings, ExitToWallets, accountsView);
            historyView = new WalletHistoryView(historyRoot, context, ShowBalances, ShowHistory, ShowAccount, ShowSettings, ExitToWallets);
            accountView = new WalletAccountView(accountRoot, context, modalHost, accountsView, ShowBalances, ShowHistory, ShowAccount, ShowSettings, ExitToWallets);
            settingsView = new WalletSettingsView(settingsRoot, context, modalHost, DisableLegacyUi, ExitToWallets);

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

        private void DisableLegacyUi()
        {
            TryDisableLegacyUi("UITK view ready");
        }

        private void DisableLegacyUi(string reason)
        {
            TryDisableLegacyUi(reason);
        }

        private void TryDisableLegacyUi(string reason)
        {
            var legacy = UnityEngine.Object.FindObjectsByType<WalletGUI>(FindObjectsSortMode.None);
            if (legacy == null || legacy.Length == 0)
            {
                Debug.Log($"{LogPrefix}No legacy WalletGUI instances found to disable ({reason}).");
                return;
            }

            foreach (var gui in legacy)
            {
                var go = gui.gameObject;
                var accountManager = go.GetComponentInChildren<AccountManager>(true);
                gui.enabled = false; // disable legacy visuals/logic

                if (accountManager != null)
                {
                    if (!accountManager.enabled)
                    {
                        accountManager.enabled = true;
                    }

                    if (!go.activeSelf)
                    {
                        go.SetActive(true);
                    }

                    Log.Write($"{LogPrefix}Disabled WalletGUI component; AccountManager kept active (found in hierarchy). reason={reason}");
                }
                else
                {
                    // If no AccountManager on this GO, we can safely deactivate it.
                    go.SetActive(false);
                    Log.Write($"{LogPrefix}Disabled legacy WalletGUI GameObject (no AccountManager). reason={reason}");
                }
            }

            Debug.Log($"{LogPrefix}Legacy UI processed ({legacy.Length} instance(s)); reason: {reason}");
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                Log.WriteWarning($"{LogPrefix}Unity {type}: {condition}\n{stackTrace}");
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
            if (accountsRoot != null)
            {
                accountsRoot.style.display = DisplayStyle.Flex;
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

        private void ShowHistory()
        {
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

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryDisableLegacyUi($"Scene loaded ({scene.name}, {mode})");
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
    }
}
