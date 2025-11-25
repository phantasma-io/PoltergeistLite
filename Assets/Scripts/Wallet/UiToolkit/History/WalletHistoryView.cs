using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Protocol;
using Poltergeist.UiToolkit;

namespace Poltergeist.UiToolkit.History
{
    /// <summary>
    /// Transaction history screen implemented with UI Toolkit.
    /// </summary>
    public sealed class WalletHistoryView : IDisposable
    {
        private const string LogPrefix = "[UITK] ";

        private readonly WalletApplicationContext context;
        private readonly WalletHistoryPresenter presenter;
        private readonly WalletHistoryViewState viewState;
        private readonly WalletUiSignals uiSignals;
        private readonly Action onShowBalances;
        private readonly Action onShowHistory;
        private readonly Action onShowAccount;
        private readonly Action onExit;

        private VisualElement root;
        private ScrollView listView;
        private Label statusLabel;
        private VisualElement content;
        private HeaderElements header;
        private SubHeaderElements subHeader;
        private Button refreshButton;
        private Label summaryLabel;
        private Label subtitleLabel;
        private Label subtitleNetworkLabel;
        private Label headerAddressLabel;
        private Button navBalances;
        private Button navHistory;
        private Button navAccount;
        private Button navExit;

        public WalletHistoryView(VisualElement host, WalletApplicationContext context, Action onShowBalances, Action onShowHistory, Action onShowAccount, Action onExit)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            presenter = context.HistoryPresenter ?? throw new ArgumentNullException(nameof(context.HistoryPresenter));
            viewState = presenter.State ?? throw new ArgumentNullException(nameof(presenter.State));
            uiSignals = context.UiSignals ?? throw new ArgumentNullException(nameof(context.UiSignals));
            this.onShowBalances = onShowBalances ?? throw new ArgumentNullException(nameof(onShowBalances));
            this.onShowHistory = onShowHistory ?? throw new ArgumentNullException(nameof(onShowHistory));
            this.onShowAccount = onShowAccount ?? throw new ArgumentNullException(nameof(onShowAccount));
            this.onExit = onExit ?? throw new ArgumentNullException(nameof(onExit));

            BuildLayout(host ?? throw new ArgumentNullException(nameof(host)));
            Subscribe();
            RequestInitialRefresh();
            RefreshView();
        }

        public void Dispose()
        {
            Unsubscribe();
        }

        public void ForceRefresh()
        {
            RefreshView();
        }

        public void OnAccountsReady()
        {
            context.ViewState.MarkHistoryDirty();
            RefreshView();
            UpdateNavSelection(NavTarget.History);
        }

        public void MarkAsActive()
        {
            context.ViewState.MarkHistoryDirty();
            UpdateNavSelection(NavTarget.History);
        }

        private void Subscribe()
        {
            uiSignals.EnsureSubscribed();
            uiSignals.HistoryUpdated += OnHistoryUpdated;
            uiSignals.HistoryRefreshStarted += OnHistoryRefreshStarted;
        }

        private void Unsubscribe()
        {
            uiSignals.HistoryUpdated -= OnHistoryUpdated;
            uiSignals.HistoryRefreshStarted -= OnHistoryRefreshStarted;
        }

        private void OnHistoryRefreshStarted(PlatformKind platform)
        {
            context.ViewState.MarkHistoryDirty();
            RefreshView();
        }

        private void OnHistoryUpdated(PlatformKind platform)
        {
            context.ViewState.MarkHistoryDirty();
            RefreshView();
        }

        private void RequestInitialRefresh()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager != null && accountManager.HasSelection && (!accountManager.CurrentAccount.passwordProtected || !string.IsNullOrEmpty(accountManager.CurrentPasswordHash)))
            {
                context.ViewState.MarkHistoryDirty();
                presenter.Refresh(true);
            }
        }

        private void OnRefreshClicked()
        {
            presenter.Refresh(false);
            context.ViewState.MarkHistoryDirty();
            RefreshView();
        }

        private void BuildLayout(VisualElement host)
        {
            root = host;
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1;
            root.style.flexBasis = 0;
            root.style.width = new Length(100, LengthUnit.Percent);
            root.style.height = new Length(100, LengthUnit.Percent);
            root.style.minHeight = 0;
            root.style.backgroundColor = WalletUiTheme.ScreenBackground;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;
            root.style.paddingTop = 16;
            root.style.paddingBottom = 16;
            root.style.color = WalletUiTheme.TextPrimary;
            root.style.alignItems = Align.Stretch;
            root.style.overflow = Overflow.Hidden;
            ApplyDefaultFont(root);

            content = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    alignSelf = Align.Center,
                    flexGrow = 1,
                    width = new Length(100, LengthUnit.Percent),
                    maxWidth = 1680,
                    paddingLeft = 8,
                    paddingRight = 8,
                    flexShrink = 1,
                    flexBasis = 0,
                    minHeight = 0
                }
            };
            ApplyDefaultFont(content);

            refreshButton = WalletUiCommon.CreateSecondaryButton("Refresh", OnRefreshClicked, 14, 32);
            refreshButton.style.minWidth = 120;

            header = WalletUiCommon.BuildHeader("History", refreshButton);
            header.Root.style.marginBottom = 10;
            content.Add(header.Root);

            subHeader = WalletUiCommon.BuildSubHeader("History");
            subtitleLabel = subHeader.SubtitleLabel;
            subtitleNetworkLabel = subHeader.NetworkLabel;
            summaryLabel = subHeader.LeftLabel;
            subHeader.Root.style.marginBottom = 6;
            content.Add(subHeader.Root);

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
            var copyHeaderBtn = WalletUiCommon.CreatePrimaryButton("Copy Address", CopyAddress, 14, 32);
            copyHeaderBtn.style.minWidth = 140;
            var explorerHeaderBtn = WalletUiCommon.CreatePrimaryButton("Explorer", OpenExplorer, 14, 32);
            explorerHeaderBtn.style.minWidth = 140;
            explorerHeaderBtn.style.marginLeft = 10;
            headerButtons.Add(copyHeaderBtn);
            headerButtons.Add(explorerHeaderBtn);
            content.Add(headerButtons);

            statusLabel = new Label(string.Empty)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 14,
                    marginTop = 0,
                    marginBottom = 0,
                    color = WalletUiTheme.TextSecondary,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    alignSelf = Align.Stretch,
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
            content.Add(statusLabel);

            listView = new ScrollView(ScrollViewMode.Vertical)
            {
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = 0,
                    minHeight = 0,
                    backgroundColor = WalletUiTheme.PanelBackground,
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
                    borderTopColor = WalletUiTheme.CardBorder,
                    borderBottomColor = WalletUiTheme.CardBorder,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 8,
                    paddingBottom = 80,
                    marginTop = 6,
                    marginBottom = 10,
                    alignSelf = Align.Center,
                    width = new Length(100, LengthUnit.Percent),
                    maxWidth = 1680,
                    overflow = Overflow.Hidden
                }
            };
            listView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            // Manual wheel handling avoids UITK ScrollView.ReadSingleLineHeight null refs and keeps the list above the footer.
            listView.RegisterCallback<WheelEvent>(evt =>
            {
                var scroller = listView.verticalScroller;
                if (scroller == null || listView.contentContainer == null)
                {
                    evt.StopImmediatePropagation();
                    evt.PreventDefault();
                    return;
                }

                const float scrollStep = 120f;
                var delta = Mathf.Clamp(evt.delta.y, -1f, 1f);
                var low = scroller.lowValue;
                var high = scroller.highValue;
                if ((double)high <= (double)low)
                {
                    var viewportHeight = listView.contentViewport?.worldBound.height ?? 0f;
                    var contentHeight = listView.contentContainer.worldBound.height;
                    if (viewportHeight > 0f && contentHeight > viewportHeight)
                    {
                        high = contentHeight - viewportHeight;
                    }
                }

                if (high < low)
                {
                    high = low;
                }

                var target = Mathf.Clamp(scroller.value + delta * scrollStep, low, high);
                scroller.value = target;
                var offset = listView.scrollOffset;
                offset.y = target;
                listView.scrollOffset = offset;
                evt.StopImmediatePropagation();
                evt.PreventDefault();
            }, TrickleDown.TrickleDown);
            ApplyDefaultFont(listView);
            if (listView.verticalScroller != null)
            {
                listView.verticalScroller.valueChanged += v => viewState.ScrollY = v;
            }
            // Keep the scroll view from pushing the footer off-screen (same fix as accounts list).
            var listWrapper = new VisualElement
            {
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = 0,
                    minHeight = 0,
                    flexDirection = FlexDirection.Column,
                    overflow = Overflow.Hidden
                }
            };
            listWrapper.Add(listView);
            content.Add(listWrapper);

            var footer = WalletUiCommon.BuildNavBar(out navBalances, out navHistory, out navAccount, out navExit, () => onShowBalances?.Invoke(), () => onShowHistory?.Invoke(), () => onShowAccount?.Invoke(), () => onExit?.Invoke());
            navAccount.SetEnabled(false);
            navAccount.style.backgroundColor = WalletUiTheme.SecondaryButton;
            navAccount.style.color = WalletUiTheme.TextPrimary;
            content.Add(footer);

            root.Add(content);
        }

        private void UpdateNavSelection(NavTarget target)
        {
            WalletUiCommon.SetNavState(navBalances, target == NavTarget.Balances);
            WalletUiCommon.SetNavState(navHistory, target == NavTarget.History);
            WalletUiCommon.SetNavState(navExit, false);
        }

        private void SetStatus(string text)
        {
            // Keep a tiny reserved strip and toggle visibility instead of display so the list top stays aligned with balances.
            statusLabel.text = text ?? string.Empty;
            var hasText = !string.IsNullOrEmpty(text);
            statusLabel.style.marginTop = hasText ? 6 : 0;
            statusLabel.style.marginBottom = hasText ? 10 : 0;
            statusLabel.style.minHeight = hasText ? 24 : 0;
            statusLabel.style.visibility = hasText ? Visibility.Visible : Visibility.Hidden;
            statusLabel.style.display = DisplayStyle.Flex;
        }

        private void RefreshView()
        {
            try
            {
                UpdateNavSelection(NavTarget.History);
                subtitleLabel.text = "History";
                subtitleNetworkLabel.text = string.Empty;
                summaryLabel.text = string.Empty;
                SetStatus("Loading history...");

                var accountManager = AccountManager.Instance;
                var settings = accountManager?.Settings;
                if (accountManager == null || accountManager.Accounts == null || !accountManager.AccountsAreReadyToBeUsed)
                {
                    SetStatus("Loading accounts...");
                    listView.Clear();
                    summaryLabel.text = "0 transactions";
                    subtitleLabel.text = "History";
                    subtitleNetworkLabel.text = string.Empty;
                    return;
                }

                if (accountManager.Settings == null)
                {
                    SetStatus("Settings are not loaded yet.");
                    listView.Clear();
                    summaryLabel.text = string.Empty;
                    subtitleLabel.text = "History";
                    subtitleNetworkLabel.text = string.Empty;
                    return;
                }

                if (!accountManager.HasSelection)
                {
                    SetStatus("Select a wallet to see history.");
                    listView.Clear();
                    summaryLabel.text = string.Empty;
                    subtitleLabel.text = "History";
                    subtitleNetworkLabel.text = string.Empty;
                    return;
                }

                if (accountManager.CurrentAccount.passwordProtected && string.IsNullOrEmpty(accountManager.CurrentPasswordHash))
                {
                    SetStatus("Wallet is locked. Open it from the wallet list.");
                    listView.Clear();
                    summaryLabel.text = string.Empty;
                    subtitleLabel.text = "History";
                    subtitleNetworkLabel.text = string.Empty;
                    return;
                }

                var historyMissing = accountManager.CurrentHistory == null || accountManager.CurrentHistory.Length == 0;
                if (!accountManager.HistoryRefreshing && historyMissing)
                {
                    presenter.Refresh(true);
                    SetStatus("Fetching history...");
                    summaryLabel.text = string.Empty;
                    return;
                }

                var snapshot = context.ViewState.GetHistorySnapshot(() => presenter.BuildSnapshot());
                UpdateContextLabels(accountManager, snapshot);

                // If we have no history yet and nothing is refreshing, kick off a fetch.
                if (!snapshot.IsRefreshing && (snapshot.Entries == null || snapshot.Entries.Count == 0) && string.IsNullOrEmpty(snapshot.ErrorMessage))
                {
                    context.ViewState.MarkHistoryDirty();
                    presenter.Refresh(true);
                    SetStatus("Fetching history...");
                    summaryLabel.text = string.Empty;
                    return;
                }

                listView.Clear();

                if (snapshot.IsRefreshing)
                {
                    SetStatus("Fetching history...");
                    summaryLabel.text = string.Empty;
                    return;
                }

                if (snapshot.HasError)
                {
                    SetStatus(snapshot.ErrorMessage);
                    summaryLabel.text = string.Empty;
                    return;
                }

                if (snapshot.Entries == null || snapshot.Entries.Count == 0)
                {
                    SetStatus($"No transactions found for this {snapshot.Platform} account.");
                    summaryLabel.text = "0 transactions";
                    listView.Add(new Label(statusLabel.text)
                    {
                        style =
                        {
                            unityTextAlign = TextAnchor.MiddleCenter,
                            paddingTop = 10,
                            paddingBottom = 10,
                            color = WalletUiTheme.TextSecondary
                        }
                    });
                    return;
                }

                SetStatus(string.Empty);
                summaryLabel.text = snapshot.Entries.Count == 1 ? "1 transaction" : $"{snapshot.Entries.Count} transactions";

                foreach (var entry in snapshot.Entries)
                {
                    listView.Add(CreateHistoryRow(entry));
                }

                if (listView.verticalScroller != null)
                {
                    listView.verticalScroller.value = Mathf.Max(0f, viewState.ScrollY);
                }
            }
            catch (Exception e)
            {
                SetStatus($"Error: {e.Message}");
                summaryLabel.text = string.Empty;
                Log.WriteWarning($"{LogPrefix}History refresh failed: {e}\n{e.StackTrace}");
            }
        }

        private void UpdateContextLabels(AccountManager accountManager, WalletHistoryViewSnapshot snapshot)
        {
            if (accountManager == null || snapshot == null || !accountManager.HasSelection)
            {
                return;
            }

            var accountName = string.IsNullOrWhiteSpace(snapshot.AccountName) ? "Wallet" : snapshot.AccountName;
            var subtitle = $"History for {accountName} @ {snapshot.Platform}";
            var settings = accountManager.Settings;
            var nexusName = settings?.nexusName;
            var nexusKind = settings?.nexusKind ?? NexusKind.Main_Net;

            subtitleLabel.text = subtitle;
            WalletUiCommon.ApplyNetworkBadge(subtitleNetworkLabel, nexusName, nexusKind);
            headerAddressLabel.text = accountManager.CurrentAccount.phaAddress ?? string.Empty;
        }

        private VisualElement CreateHistoryRow(WalletHistoryItem entry)
        {
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
                    borderTopColor = WalletUiTheme.CardBorder,
                    borderBottomColor = WalletUiTheme.CardBorder
                }
            };
            ApplyDefaultFont(row);

            var hash = new Label(entry.Hash)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 16,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    flexGrow = 1
                }
            };
            ApplyDefaultFont(hash);
            row.Add(hash);

            var date = new Label(entry.Date.ToString("dd.MM.yyyy HH:mm"))
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 15,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    minWidth = 170
                }
            };
            ApplyDefaultFont(date);
            row.Add(date);

            var view = WalletUiCommon.CreateListActionButton("View", () => OpenHistoryUrl(entry), 110, 40);
            view.style.marginLeft = 12;
            view.style.marginRight = 4;
            view.style.alignSelf = Align.Center;
            row.Add(view);

            return row;
        }

        private void OpenHistoryUrl(WalletHistoryItem entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Url))
            {
                SetStatus("No explorer URL available.");
                return;
            }

            Application.OpenURL(entry.Url);
            SetStatus("Opening transaction...");
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













