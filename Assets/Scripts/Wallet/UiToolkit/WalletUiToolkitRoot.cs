using System;
using UnityEngine;
using System.Collections;
using Poltergeist.Wallet;
using Poltergeist.UiToolkit.Balances;
using Poltergeist.UiToolkit.Accounts;
using UnityEngine.UIElements;
using Poltergeist;
using PhantasmaPhoenix.Unity.Core.Logging;

namespace Poltergeist.UiToolkit
{
    /// <summary>
    /// Runtime entry point for the new UI Toolkit wallet experience.
    /// </summary>
    public sealed class WalletUiToolkitRoot : MonoBehaviour
    {
        private const string LogPrefix = "[UITK] ";
        private const string FatalPrefix = "[UITK] ";

        private WalletAccountsView accountsView;
        private WalletBalancesView balancesView;
        private UIDocument document;
        private PanelSettings panelSettings;
        private VisualElement accountsRoot;
        private VisualElement balancesRoot;
        private bool initializationFailed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var root = new GameObject("WalletUiToolkitRoot");
            DontDestroyOnLoad(root);
            root.AddComponent<WalletUiToolkitRoot>();
            Log.Write($"{LogPrefix}Bootstrap complete, root created (UITK forced on, legacy UI will be disabled).");
        }

        private void Awake()
        {
            Log.Write($"{LogPrefix}Awake");

            try
            {
                EnsureAccountManagerHost();
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
        }

        private void OnDestroy()
        {
            if (initializationFailed)
            {
                return;
            }

            Log.Write($"{LogPrefix}OnDestroy");
            Application.logMessageReceived -= OnLogMessageReceived;
            accountsView?.Dispose();
            balancesView?.Dispose();
            accountsView = null;
            balancesView = null;
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
                    if (am.HasSelection && (!am.CurrentAccount.passwordProtected || !string.IsNullOrEmpty(am.CurrentPasswordHash)))
                    {
                        ShowBalances();
                        balancesView?.OnAccountsReady();
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

        private void InitializeViews()
        {
            var context = WalletApplicationContext.Instance;
            if (context == null)
            {
                throw new InvalidOperationException("WalletApplicationContext is not ready.");
            }

            var root = document.rootVisualElement;
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1;
            root.style.backgroundColor = WalletUiTheme.ScreenBackground;

            accountsRoot = new VisualElement { style = { flexGrow = 1, display = DisplayStyle.Flex, backgroundColor = WalletUiTheme.ScreenBackground } };
            balancesRoot = new VisualElement { style = { flexGrow = 1, display = DisplayStyle.None, backgroundColor = WalletUiTheme.ScreenBackground } };

            accountsView = new WalletAccountsView(accountsRoot, context, ShowBalances);
            balancesView = new WalletBalancesView(balancesRoot, context, DisableLegacyUi);

            root.Add(accountsRoot);
            root.Add(balancesRoot);

            Log.Write($"{LogPrefix}Views initialized (accounts + balances).");
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
            var legacy = GameObject.FindObjectsOfType<WalletGUI>(true);
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
            if (am != null && am.HasSelection && (!am.CurrentAccount.passwordProtected || !string.IsNullOrEmpty(am.CurrentPasswordHash)))
            {
                ShowBalances();
                balancesView?.ForceRefresh();
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

            Log.Write($"{LogPrefix}ShowBalances invoked; refreshing balances view.");
            balancesView?.OnAccountsReady();
            balancesView?.ForceRefresh();
            Log.Write($"{LogPrefix}ShowBalances done. balancesVisible={balancesRoot?.style.display} accountsVisible={accountsRoot?.style.display}");
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
