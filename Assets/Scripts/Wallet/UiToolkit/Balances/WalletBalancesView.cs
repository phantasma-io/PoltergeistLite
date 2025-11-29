using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist.UiToolkit;

namespace Poltergeist.UiToolkit.Balances
{
    /// <summary>
    /// Renders the balances screen using UI Toolkit.
    /// </summary>
    public sealed class WalletBalancesView : IDisposable
    {
        private const string LogPrefix = "[UITK] ";

        private readonly WalletApplicationContext context;
        private readonly WalletBalancePresenter presenter;
        private readonly WalletBalanceViewState viewState;
        private readonly WalletUiSignals uiSignals;
        private readonly Action onReady;
        private readonly Action onShowBalances;
        private readonly Action onShowHistory;
        private readonly Action onShowAccount;
        private readonly Action onShowSettings;
        private readonly Action onExit;
        private HeaderElements header;
        private SubHeaderElements subHeader;

        private Label summaryLabel;
        private Label statusLabel;
        private Button refreshButton;
        private Label subtitleLabel;
        private Label subtitleNetworkLabel;
        private Label headerAddressLabel;
        private ScrollView listView;
        private VisualElement root;
        private bool readyNotified;
        private Button navBalances;
        private Button navHistory;
        private Button navAccount;
        private Button navExit;

        public WalletBalancesView(VisualElement host, WalletApplicationContext context, Action onReady, Action onShowBalances, Action onShowHistory, Action onShowAccount, Action onShowSettings, Action onExit)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            presenter = context.BalancePresenter ?? throw new ArgumentNullException(nameof(context.BalancePresenter));
            viewState = presenter.State ?? throw new ArgumentNullException(nameof(presenter.State));
            uiSignals = context.UiSignals ?? throw new ArgumentNullException(nameof(context.UiSignals));
            this.onReady = onReady;
            this.onShowBalances = onShowBalances ?? throw new ArgumentNullException(nameof(onShowBalances));
            this.onShowHistory = onShowHistory ?? throw new ArgumentNullException(nameof(onShowHistory));
            this.onShowAccount = onShowAccount ?? throw new ArgumentNullException(nameof(onShowAccount));
            this.onShowSettings = onShowSettings ?? throw new ArgumentNullException(nameof(onShowSettings));
            this.onExit = onExit ?? throw new ArgumentNullException(nameof(onExit));

            BuildLayout(host);
            Subscribe();
            RequestInitialRefresh();
            RefreshView();
            UpdateNavSelection(NavTarget.Balances);
        }

        public void Dispose()
        {
            Unsubscribe();
        }

        private void BuildLayout(VisualElement host)
        {
            root = host ?? throw new ArgumentNullException(nameof(host));
            root.Clear();
            WalletUiCommon.ConfigureScreenRoot(root);
            root.style.backgroundColor = Color.clear;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;
            root.style.paddingTop = 16;
            root.style.paddingBottom = 16;
            root.style.color = WalletUiTheme.TextPrimary;
            ApplyDefaultFont(root);

            var content = WalletUiCommon.CreateScreenContent(paddingLeft: 8, paddingRight: 8);

            refreshButton = WalletUiCommon.CreateSecondaryButton("Refresh", OnRefreshClicked, 14, 32);
            refreshButton.style.minWidth = 120;

            var headerBlock = WalletUiCommon.BuildHeaderBlock("Balances", "Balances", string.Empty, refreshButton, showHeaderSubtitle: false);
            header = headerBlock.Header;
            subHeader = headerBlock.SubHeader;
            summaryLabel = subHeader.LeftLabel;
            subtitleLabel = subHeader.SubtitleLabel;
            subtitleNetworkLabel = subHeader.NetworkLabel;
            headerBlock.Root.style.flexShrink = 0;
            content.Add(headerBlock.Root);

            headerAddressLabel = new Label(string.Empty)
            {
                style =
                {
                    fontSize = 14,
                    color = WalletUiTheme.TextSecondary,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    alignSelf = Align.Center,
                    marginBottom = 6
                }
            };
            ApplyDefaultFont(headerAddressLabel);
            content.Add(headerAddressLabel);

            var headerButtons = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    alignSelf = Align.Center,
                    marginBottom = 6
                }
            };
            var copyHeaderBtn = WalletUiCommon.CreateSecondaryButton("Copy Address", CopyAddress, 14, 32);
            copyHeaderBtn.style.minWidth = 140;
            var explorerHeaderBtn = WalletUiCommon.CreateSecondaryButton("Explorer", OpenExplorer, 14, 32);
            explorerHeaderBtn.style.minWidth = 140;
            explorerHeaderBtn.style.marginLeft = 10;
            headerButtons.Add(copyHeaderBtn);
            headerButtons.Add(explorerHeaderBtn);
            content.Add(headerButtons);

            statusLabel = new Label
            {
                text = "Initializing wallet UI...",
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 14,
                    marginTop = 0,
                    marginBottom = 0,
                    color = WalletUiTheme.TextSecondary,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    alignSelf = Align.Center,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 4,
                    paddingBottom = 4,
                    backgroundColor = WalletUiTheme.PanelBackground,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    minHeight = 24
                }
            };
            ApplyDefaultFont(statusLabel);
            statusLabel.style.visibility = Visibility.Hidden;
            statusLabel.style.flexShrink = 0;
            content.Add(statusLabel);

            var listWrapper = WalletUiCommon.BuildScrollContainer(
                out listView,
                v => viewState.ScrollY = v,
                shouldBlockWheel: null,
                paddingLeft: 8f,
                paddingRight: 8f,
                paddingTop: 8f,
                paddingBottom: 80f,
                marginTop: 6f,
                marginBottom: 10f,
                maxWidth: 1680f,
                alignSelf: Align.Center);
            content.Add(listWrapper);

            var footer = WalletUiCommon.BuildWalletNavBar(out navBalances, out navHistory, out navAccount, out navExit, () => onShowBalances?.Invoke(), () => onShowHistory?.Invoke(), () => onShowAccount?.Invoke(), () => onExit?.Invoke());
            footer.style.flexShrink = 0;
            content.Add(footer);

            root.Add(content);
        }

        private void Subscribe()
        {
            uiSignals.EnsureSubscribed();
            uiSignals.BalancesUpdated += OnBalancesUpdated;
            uiSignals.BalancesRefreshStarted += OnBalancesRefreshStarted;
        }

        private void Unsubscribe()
        {
            uiSignals.BalancesUpdated -= OnBalancesUpdated;
            uiSignals.BalancesRefreshStarted -= OnBalancesRefreshStarted;
            refreshButton.clicked -= OnRefreshClicked;
        }

        private void RequestInitialRefresh()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager != null && accountManager.HasSelection && (!accountManager.CurrentAccount.passwordProtected || !string.IsNullOrEmpty(accountManager.CurrentPasswordHash)))
            {
                if (!EnsureTokensReady())
                {
                    return;
                }

                Log.Write($"{LogPrefix}Requesting initial balances refresh for {accountManager.CurrentPlatform}");
                presenter.Refresh(true);
                return;
            }

            Log.Write($"{LogPrefix}Waiting for account selection before refreshing balances.");
        }

        private void OnRefreshClicked()
        {
            if (!EnsureTokensReady())
            {
                return;
            }

            presenter.Refresh(false);
            context.ViewState.MarkBalancesDirty();
            RefreshView();
        }

        private void OnBalancesRefreshStarted(PlatformKind platform)
        {
            context.ViewState.MarkBalancesDirty();
            RefreshView();
        }

        private void OnBalancesUpdated(PlatformKind platform)
        {
            context.ViewState.MarkBalancesDirty();
            RefreshView();
        }

        private bool EnsureTokensReady()
        {
            // UITK screens can request balance refresh before token metadata arrives; guard to avoid missing-decimal exceptions.
            var tokensReady = Tokens.GetTokens().Length > 0;
            if (tokensReady)
            {
                return true;
            }

            Log.Write($"{LogPrefix}Token metadata not loaded yet; requesting reload before balance refresh.");
            AccountManager.Instance?.RequestTokensReload();
            SetStatus("Refreshing tokens...");
            return false;
        }

        private void SetStatus(string text)
        {
            // Keep a tiny reserved strip and toggle visibility instead of display so the list top never jumps when switching tabs.
            var hasText = !string.IsNullOrEmpty(text);
            statusLabel.style.marginTop = hasText ? 6 : 0;
            statusLabel.style.marginBottom = hasText ? 10 : 0;
            statusLabel.style.minHeight = hasText ? 24 : 0;
            statusLabel.text = text ?? string.Empty;
            statusLabel.style.visibility = hasText ? Visibility.Visible : Visibility.Hidden;
            statusLabel.style.display = DisplayStyle.Flex;
        }

        private void RefreshView()
        {
            try
            {
                UpdateNavSelection(NavTarget.Balances);
                subtitleLabel.text = "Balances";
                subtitleNetworkLabel.text = string.Empty;
                summaryLabel.text = string.Empty;

                var snapshot = context.ViewState.GetBalancesSnapshot(() => presenter.BuildSnapshot());
                SetStatus("Loading balances...");
                var accountManager = AccountManager.Instance;
                var settings = accountManager?.Settings;
                Log.Write($"{LogPrefix}RefreshView snapshot built. accountsReady={accountManager?.AccountsAreReadyToBeUsed} accounts={accountManager?.Accounts?.Count} selection={accountManager?.CurrentIndex}");
                if (accountManager == null || accountManager.Accounts == null || accountManager.Accounts.Count == 0)
                {
                    SetStatus("No accounts loaded yet...");
                    summaryLabel.text = "0 assets";
                    listView.Clear();
                    subtitleLabel.text = "Balances";
                    subtitleNetworkLabel.text = string.Empty;
                    NotifyReady("no accounts");
                    return;
                }

                if (settings == null)
                {
                    SetStatus("Settings are not loaded yet.");
                    listView.Clear();
                    subtitleLabel.text = "Balances";
                    subtitleNetworkLabel.text = string.Empty;
                    NotifyReady("settings missing");
                    return;
                }

                if (!accountManager.HasSelection)
                {
                    SetStatus("Select a wallet to see balances.");
                    listView.Clear();
                    subtitleLabel.text = "Balances";
                    subtitleNetworkLabel.text = string.Empty;
                    NotifyReady("no selection");
                    return;
                }

                var headerSubtitle = WalletUiCommon.BuildContextSubtitle("Balances", snapshot.AccountName, snapshot.Platform);
                subtitleLabel.text = headerSubtitle;
                WalletUiCommon.ApplyNetworkBadge(subtitleNetworkLabel, settings.nexusName, settings.nexusKind);
                headerAddressLabel.text = accountManager.CurrentAccount.phaAddress ?? string.Empty;

                if (accountManager.CurrentAccount.passwordProtected && string.IsNullOrEmpty(accountManager.CurrentPasswordHash))
                {
                    SetStatus("Wallet is locked. Open it from the wallet list.");
                    listView.Clear();
                    subtitleLabel.text = "Balances";
                    subtitleNetworkLabel.text = string.Empty;
                    summaryLabel.text = string.Empty;
                    NotifyReady("locked");
                    return;
                }

                var balances = snapshot.Balances ?? Array.Empty<WalletBalanceEntry>();
                var filteredBalances = FilterBalances(balances, accountManager.Settings.balanceDisplayThreshold);
                Log.Write($"{LogPrefix}Balances snapshot stats: raw={balances.Count()} filtered={filteredBalances.Count} refreshing={snapshot.IsRefreshing} error={snapshot.ErrorMessage}");

                listView.Clear();

                if (snapshot.IsRefreshing)
                {
                    SetStatus("Fetching balances...");
                    subtitleLabel.text = "Balances";
                    subtitleNetworkLabel.text = string.Empty;
                    summaryLabel.text = string.Empty;
                    NotifyReady("refreshing");
                    return;
                }

                if (snapshot.HasError)
                {
                    SetStatus(snapshot.ErrorMessage);
                    subtitleLabel.text = "Balances";
                    subtitleNetworkLabel.text = string.Empty;
                    summaryLabel.text = string.Empty;
                    NotifyReady("error");
                    return;
                }

                SetStatus(string.Empty);
                summaryLabel.text = filteredBalances.Count == 1 ? "1 asset" : $"{filteredBalances.Count} assets";

                foreach (var entry in filteredBalances)
                {
                    listView.Add(CreateBalanceRow(entry, accountManager.CurrentPlatform));
                }

                if (filteredBalances.Count == 0)
                {
                    listView.Add(new Label($"No assets found in this {snapshot.Platform} account.")
                    {
                        style =
                        {
                            unityTextAlign = TextAnchor.MiddleCenter,
                            paddingTop = 12,
                            paddingBottom = 12
                        }
                    });
                }

                if (listView.verticalScroller != null)
                {
                    listView.verticalScroller.value = Mathf.Max(0f, viewState.ScrollY);
                }

                NotifyReady("snapshot ready");
            }
            catch (Exception e)
            {
                SetStatus($"Error: {e.Message}");
                Log.WriteWarning($"[UITK] Balances refresh failed: {e}\n{e.StackTrace}");
                NotifyReady("exception");
            }
        }

        private void NotifyReady(string reason)
        {
            if (readyNotified)
            {
                return;
            }

            readyNotified = true;
            Log.Write($"{LogPrefix}Balances view ready ({reason}).");
            onReady?.Invoke();
        }

        public void ForceRefresh()
        {
            RefreshView();
        }

        public void MarkAsActive()
        {
            UpdateNavSelection(NavTarget.Balances);
        }

        public void OnAccountsReady()
        {
            RefreshView();
        }

        private List<WalletBalanceEntry> FilterBalances(IEnumerable<WalletBalanceEntry> balances, decimal minBalanceSetting)
        {
            var result = new List<WalletBalanceEntry>();
            foreach (var entry in balances)
            {
                if (entry == null)
                {
                    Log.WriteWarning($"{LogPrefix}Null balance entry encountered, skipping.");
                    continue;
                }

                var threshold = System.Numerics.BigInteger.Zero;
                if (minBalanceSetting > 0)
                {
                    var thresholdText = minBalanceSetting.ToString(CultureInfo.InvariantCulture);
                    if (!WalletAmountParser.TryParse(thresholdText, entry.Decimals, out threshold))
                    {
                        threshold = System.Numerics.BigInteger.Zero;
                    }
                }

                var positive = entry.Total > System.Numerics.BigInteger.Zero;
                var meetsThreshold = threshold == System.Numerics.BigInteger.Zero ? positive : entry.Total >= threshold;
                if (meetsThreshold)
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        private VisualElement CreateBalanceRow(WalletBalanceEntry entry, PlatformKind platform)
        {
            // Keep row sizing in sync with history entries to avoid vertical misalignment when switching tabs.
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 18,
                    paddingRight = 18,
                    paddingTop = 16,
                    paddingBottom = 16,
                    marginBottom = 10,
                    minHeight = 90,
                    backgroundColor = WalletUiTheme.CardBackground,
                    backgroundImage = new StyleBackground(WalletUiTheme.GetCardGradientTexture()),
                    unityBackgroundScaleMode = ScaleMode.StretchToFill,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.CardBorder,
                    borderRightColor = WalletUiTheme.CardBorder,
                    borderTopColor = WalletUiTheme.HighlightEdge,
                    borderBottomColor = WalletUiTheme.CardBorder
                }
            };
            ApplyDefaultFont(row);

            var iconTexture = ResourceManager.Instance?.GetToken(entry.Symbol, platform) as Texture2D;
            if (iconTexture != null)
            {
                var icon = new Image
                {
                    image = iconTexture,
                    scaleMode = ScaleMode.ScaleToFit,
                    style =
                    {
                        width = 42,
                        height = 42,
                        marginRight = 12
                    }
                };
                row.Add(icon);
            }

            var textBlock = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1,
                    marginLeft = 2
                }
            };

            var fiat = string.IsNullOrEmpty(entry.FiatWorth) ? string.Empty : $" ({entry.FiatWorth})";
            var title = new Label($"{entry.AvailableText} {entry.Symbol}{fiat}")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 16,
                    color = WalletUiTheme.TextPrimary
                }
            };
            ApplyDefaultFont(title);
            textBlock.Add(title);

            var secondaryText = BuildSecondaryLine(entry);
            if (!string.IsNullOrEmpty(secondaryText))
            {
                var secondary = new Label(secondaryText)
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        fontSize = 13,
                        unityFontStyleAndWeight = FontStyle.Bold
                    }
                };
                ApplyDefaultFont(secondary);
                textBlock.Add(secondary);
            }

            row.Add(textBlock);

            return row;
        }

        private string BuildSecondaryLine(WalletBalanceEntry entry)
        {
            var parts = new List<string>();
            var accountManager = AccountManager.Instance;

            if (entry.Staked > System.Numerics.BigInteger.Zero)
            {
            var fiat = string.IsNullOrEmpty(entry.StakedFiatWorth) ? string.Empty : $" ({entry.StakedFiatWorth})";
            parts.Add($"Staked {entry.StakedText}{fiat}");
            }

            if (entry.Claimable > System.Numerics.BigInteger.Zero)
            {
                parts.Add($"Claimable {entry.ClaimableText}");
            }

            return parts.Count == 0 ? string.Empty : string.Join(" | ", parts);
        }

        private void CopyAddress()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null || !accountManager.HasSelection)
            {
                SetStatus("No address to copy.");
                return;
            }

            var address = accountManager.CurrentAccount.phaAddress;
            if (string.IsNullOrWhiteSpace(address))
            {
                SetStatus("No address to copy.");
                return;
            }

            GUIUtility.systemCopyBuffer = address;
            SetStatus("Address copied.");
        }

        private void OpenExplorer()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null || !accountManager.HasSelection)
            {
                SetStatus("No address to open.");
                return;
            }

            var address = accountManager.CurrentAccount.phaAddress;
            if (string.IsNullOrWhiteSpace(address))
            {
                SetStatus("No address to open.");
                return;
            }

            var url = accountManager.GetPhantasmaAddressURL(address);
            if (string.IsNullOrWhiteSpace(url))
            {
                SetStatus("Explorer URL is not configured.");
                return;
            }

            Application.OpenURL(url);
            SetStatus("Opening explorer...");
        }

        private void UpdateNavSelection(NavTarget target)
        {
            WalletUiCommon.SetNavState(navBalances, target == NavTarget.Balances);
            WalletUiCommon.SetNavState(navHistory, target == NavTarget.History);
            WalletUiCommon.SetNavState(navAccount, target == NavTarget.Account);
            WalletUiCommon.SetNavState(navExit, false);
        }

        private void ApplyDefaultFont(VisualElement element)
        {
            WalletUiCommon.ApplyDefaultFont(element);
        }

        private enum NavTarget
        {
            Balances,
            History,
            Account
        }
    }
}

