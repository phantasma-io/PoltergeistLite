using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Protocol;
using Poltergeist;
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
        private readonly Font defaultFont;

        private VisualElement root;
        private ScrollView listView;
        private Label statusLabel;
        private Label accountLabel;
        private Label addressLabel;
        private VisualElement content;
        private Label networkLabel;
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
            defaultFont = WalletUiTheme.DefaultFont;

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

        private void BuildLayout(VisualElement host)
        {
            root = host;
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1;
            root.style.width = new Length(100, LengthUnit.Percent);
            root.style.height = new Length(100, LengthUnit.Percent);
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
                    maxWidth = 1680
                }
            };
            ApplyDefaultFont(content);

            var header = BuildHeader();
            content.Add(header);

            var info = BuildAccountInfo();
            content.Add(info);

            statusLabel = new Label("Loading history...")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 14,
                    marginTop = 12,
                    marginBottom = 12,
                    color = WalletUiTheme.TextSecondary,
                    unityTextAlign = TextAnchor.MiddleCenter
                }
            };
            ApplyDefaultFont(statusLabel);
            content.Add(statusLabel);

            listView = new ScrollView(ScrollViewMode.Vertical)
            {
                style =
                {
                    flexGrow = 1,
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
                    marginTop = 8
                }
            };
            listView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            ApplyDefaultFont(listView);
            if (listView.verticalScroller != null)
            {
                listView.verticalScroller.valueChanged += v => viewState.ScrollY = v;
            }
            content.Add(listView);

            var footer = BuildFooterNav();
            root.Add(content);
            root.Add(footer);
        }

        private VisualElement BuildHeader()
        {
            var bar = new VisualElement
            {
                style =
                {
                    minHeight = 72,
                    backgroundColor = WalletUiTheme.HeaderBackground,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.HeaderBorder,
                    borderRightColor = WalletUiTheme.HeaderBorder,
                    borderTopColor = WalletUiTheme.HeaderBorder,
                    borderBottomColor = WalletUiTheme.HeaderBorder,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium,
                    paddingLeft = 16,
                    paddingRight = 16,
                    paddingTop = 10,
                    paddingBottom = 10,
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.SpaceBetween,
                    marginBottom = 12
                }
            };

            var titleRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center
                }
            };

            var title = new Label("Poltergeist Lite")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 26,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            ApplyDefaultFont(title);

            var version = new Label(BuildVersionLabel())
            {
                style =
                {
                    fontSize = 14,
                    color = WalletUiTheme.TextSecondary,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginLeft = 10
                }
            };
            ApplyDefaultFont(version);

            titleRow.Add(title);
            titleRow.Add(version);

            var refresh = new Button
            {
                text = "Refresh",
                style =
                {
                    backgroundColor = WalletUiTheme.SecondaryButton,
                    color = WalletUiTheme.TextPrimary,
                    minWidth = 120,
                    minHeight = 32,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.SecondaryButtonBorder,
                    borderRightColor = WalletUiTheme.SecondaryButtonBorder,
                    borderTopColor = WalletUiTheme.SecondaryButtonBorder,
                    borderBottomColor = WalletUiTheme.SecondaryButtonBorder,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall,
                    paddingLeft = 12,
                    paddingRight = 12
                }
            };
            ApplyDefaultFont(refresh);
            refresh.clicked += () =>
            {
                presenter.Refresh(false);
                context.ViewState.MarkHistoryDirty();
                RefreshView();
            };

            bar.Add(titleRow);
            bar.Add(refresh);

            return bar;
        }

        private VisualElement BuildAccountInfo()
        {
            var container = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    marginBottom = 6
                }
            };

            var titleRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center
                }
            };

            accountLabel = new Label("History")
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 16,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    unityFontStyleAndWeight = FontStyle.Bold
                }
            };
            ApplyDefaultFont(accountLabel);
            titleRow.Add(accountLabel);

            networkLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 14,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginLeft = 8
                }
            };
            ApplyDefaultFont(networkLabel);
            titleRow.Add(networkLabel);

            container.Add(titleRow);

            addressLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 14,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginTop = 2
                }
            };
            ApplyDefaultFont(addressLabel);
            container.Add(addressLabel);

            var buttons = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    marginTop = 10,
                    marginBottom = 6
                }
            };

            var copy = MakeActionButton("Copy Address", CopyAddress);
            buttons.Add(copy);

            var explorer = MakeActionButton("Explorer", OpenExplorer);
            explorer.style.marginLeft = 10;
            buttons.Add(explorer);

            container.Add(buttons);

            return container;
        }

        private Button MakeActionButton(string text, Action onClick)
        {
            var btn = new Button
            {
                text = text,
                style =
                {
                    backgroundColor = WalletUiTheme.ActionButton,
                    color = WalletUiTheme.ActionButtonText,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 14,
                    minWidth = 140,
                    minHeight = 36,
                    paddingLeft = 14,
                    paddingRight = 14,
                    paddingTop = 8,
                    paddingBottom = 8,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.ActionButtonBorder,
                    borderRightColor = WalletUiTheme.ActionButtonBorder,
                    borderTopColor = WalletUiTheme.ActionButtonBorder,
                    borderBottomColor = WalletUiTheme.ActionButtonBorder
                }
            };
            ApplyDefaultFont(btn);
            btn.clicked += () => onClick?.Invoke();
            return btn;
        }

        private VisualElement BuildFooterNav()
        {
            var bar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.SpaceBetween,
                    alignItems = Align.Center,
                    paddingTop = 12,
                    paddingBottom = 12,
                    paddingLeft = 10,
                    paddingRight = 10,
                    marginTop = 10,
                    minHeight = 68,
                    backgroundColor = WalletUiTheme.HeaderBackground,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopColor = WalletUiTheme.HeaderBorder,
                    borderBottomColor = WalletUiTheme.HeaderBorder,
                    borderLeftColor = WalletUiTheme.HeaderBorder,
                    borderRightColor = WalletUiTheme.HeaderBorder,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium
                }
            };

            navBalances = MakeNavButton("Balances", () => onShowBalances?.Invoke());
            navHistory = MakeNavButton("History", () => onShowHistory?.Invoke());
            navAccount = MakeNavButton("Account", () => onShowAccount?.Invoke());
            navExit = MakeNavButton("Exit", () => onExit?.Invoke());

            // Account view not implemented yet in UITK; keep button visible but disabled to mirror legacy layout.
            navAccount.SetEnabled(false);
            navAccount.style.backgroundColor = WalletUiTheme.SecondaryButton;
            navAccount.style.color = WalletUiTheme.TextPrimary;

            var buttons = new[] { navBalances, navHistory, navAccount, navExit };
            for (var i = 0; i < buttons.Length; i++)
            {
                var btn = buttons[i];
                btn.style.flexGrow = 1;
                if (i > 0)
                {
                    btn.style.marginLeft = 8;
                }

                bar.Add(btn);
            }

            UpdateNavSelection(NavTarget.History);
            return bar;
        }

        private Button MakeNavButton(string text, Action onClick)
        {
            var btn = new Button
            {
                text = text,
                style =
                {
                    backgroundColor = WalletUiTheme.ActionButton,
                    color = WalletUiTheme.ActionButtonText,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 16,
                    minHeight = 44,
                    paddingLeft = 18,
                    paddingRight = 18,
                    paddingTop = 12,
                    paddingBottom = 12,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.ActionButtonBorder,
                    borderRightColor = WalletUiTheme.ActionButtonBorder,
                    borderTopColor = WalletUiTheme.ActionButtonBorder,
                    borderBottomColor = WalletUiTheme.ActionButtonBorder
                }
            };
            ApplyDefaultFont(btn);
            btn.clicked += () => onClick?.Invoke();
            return btn;
        }

        private void UpdateNavSelection(NavTarget target)
        {
            SetNavState(navBalances, target == NavTarget.Balances);
            SetNavState(navHistory, target == NavTarget.History);
            SetNavState(navExit, false);
        }

        private void SetNavState(Button btn, bool isActive)
        {
            if (btn == null)
            {
                return;
            }

            btn.SetEnabled(!isActive);
            btn.style.backgroundColor = isActive ? WalletUiTheme.SecondaryButton : WalletUiTheme.ActionButton;
            btn.style.color = isActive ? WalletUiTheme.TextPrimary : WalletUiTheme.ActionButtonText;
        }

        private void RefreshView()
        {
            try
            {
                var accountManager = AccountManager.Instance;
                if (accountManager == null || accountManager.Accounts == null || !accountManager.AccountsAreReadyToBeUsed)
                {
                    statusLabel.text = "Loading accounts...";
                    listView.Clear();
                    return;
                }

                if (!accountManager.HasSelection)
                {
                    statusLabel.text = "Select a wallet to see history.";
                    listView.Clear();
                    return;
                }

                if (accountManager.CurrentAccount.passwordProtected && string.IsNullOrEmpty(accountManager.CurrentPasswordHash))
                {
                    statusLabel.text = "Wallet is locked. Open it from the wallet list.";
                    listView.Clear();
                    return;
                }

                var historyMissing = accountManager.CurrentHistory == null || accountManager.CurrentHistory.Length == 0;
                if (!accountManager.HistoryRefreshing && historyMissing)
                {
                    presenter.Refresh(true);
                    statusLabel.text = "Fetching history...";
                    return;
                }

                var snapshot = context.ViewState.GetHistorySnapshot(() => presenter.BuildSnapshot());
                UpdateContextLabels(accountManager, snapshot);

                // If we have no history yet and nothing is refreshing, kick off a fetch.
                if (!snapshot.IsRefreshing && (snapshot.Entries == null || snapshot.Entries.Count == 0) && string.IsNullOrEmpty(snapshot.ErrorMessage))
                {
                    context.ViewState.MarkHistoryDirty();
                    presenter.Refresh(true);
                    statusLabel.text = "Fetching history...";
                    return;
                }

                listView.Clear();

                if (snapshot.IsRefreshing)
                {
                    statusLabel.text = "Fetching history...";
                    return;
                }

                if (snapshot.HasError)
                {
                    statusLabel.text = snapshot.ErrorMessage;
                    return;
                }

                if (snapshot.Entries == null || snapshot.Entries.Count == 0)
                {
                    statusLabel.text = $"No transactions found for this {snapshot.Platform} account.";
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

                statusLabel.text = $"{snapshot.Entries.Count} transactions";

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
                statusLabel.text = $"Error: {e.Message}";
                Log.WriteWarning($"{LogPrefix}History refresh failed: {e}\n{e.StackTrace}");
            }
        }

        private void UpdateContextLabels(AccountManager accountManager, WalletHistoryViewSnapshot snapshot)
        {
            if (accountManager == null || !accountManager.HasSelection)
            {
                accountLabel.text = "History";
                addressLabel.text = string.Empty;
                networkLabel.text = string.Empty;
                return;
            }

            var accountName = string.IsNullOrWhiteSpace(snapshot.AccountName) ? "Wallet" : snapshot.AccountName;
            var platform = snapshot.Platform;
            var networkKind = accountManager.Settings?.nexusKind ?? NexusKind.Main_Net;
            var networkName = BuildNetworkLabel(accountManager.Settings?.nexusName, networkKind);
            accountLabel.text = $"History for {accountName} [{accountName} @ {platform}]";

            var address = accountManager.CurrentAccount.phaAddress ?? string.Empty;
            addressLabel.text = address;
            networkLabel.text = $"[{networkName}]";
            ApplyNetworkColor(networkKind);
        }

        private VisualElement CreateHistoryRow(WalletHistoryItem entry)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 16,
                    paddingRight = 12,
                    paddingTop = 12,
                    paddingBottom = 12,
                    marginBottom = 8,
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

            var view = new Button
            {
                text = "View",
                style =
                {
                    minWidth = 110,
                    minHeight = 40,
                    backgroundColor = WalletUiTheme.ActionButton,
                    color = WalletUiTheme.ActionButtonText,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.ActionButtonBorder,
                    borderRightColor = WalletUiTheme.ActionButtonBorder,
                    borderTopColor = WalletUiTheme.ActionButtonBorder,
                    borderBottomColor = WalletUiTheme.ActionButtonBorder,
                    marginLeft = 12,
                    marginRight = 4
                }
            };
            ApplyDefaultFont(view);
            view.clicked += () => OpenHistoryUrl(entry);
            row.Add(view);

            return row;
        }

        private void OpenHistoryUrl(WalletHistoryItem entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Url))
            {
                statusLabel.text = "No explorer URL available.";
                return;
            }

            Application.OpenURL(entry.Url);
            statusLabel.text = "Opening transaction...";
        }

        private void CopyAddress()
        {
            var accountManager = AccountManager.Instance;
            var address = accountManager?.CurrentAccount.phaAddress;
            if (string.IsNullOrWhiteSpace(address))
            {
                statusLabel.text = "No address to copy.";
                return;
            }

            GUIUtility.systemCopyBuffer = address;
            statusLabel.text = "Address copied.";
        }

        private void OpenExplorer()
        {
            var accountManager = AccountManager.Instance;
            var address = accountManager?.CurrentAccount.phaAddress;
            if (string.IsNullOrWhiteSpace(address))
            {
                statusLabel.text = "No address to open.";
                return;
            }

            var url = accountManager?.GetPhantasmaAddressURL(address);
            if (string.IsNullOrWhiteSpace(url))
            {
                statusLabel.text = "Explorer URL is not configured.";
                return;
            }

            Application.OpenURL(url);
            statusLabel.text = "Opening explorer...";
        }

        private void ApplyDefaultFont(VisualElement element)
        {
            if (element == null || defaultFont == null)
            {
                return;
            }

            element.style.unityFont = defaultFont;
            element.style.unityFontDefinition = FontDefinition.FromFont(defaultFont);
        }

        private string BuildVersionLabel()
        {
            return $"{Application.version} - Built {Build.Info.Instance.BuildTime} UTC";
        }

        private string BuildNetworkLabel(string name, NexusKind kind)
        {
            var source = string.IsNullOrWhiteSpace(name) ? kind.ToString() : name;
            source = source.Replace("_", string.Empty).Replace(" ", string.Empty);
            return source.ToUpperInvariant();
        }

        private void ApplyNetworkColor(NexusKind kind)
        {
            Color c = new Color(0.7f, 0.75f, 0.8f);
            switch (kind)
            {
                case NexusKind.Test_Net:
                    ColorUtility.TryParseHtmlString("#FF8A00", out c);
                    break;
                case NexusKind.Dev_Net:
                    ColorUtility.TryParseHtmlString("#FFD247", out c);
                    break;
                case NexusKind.Local_Net:
                    ColorUtility.TryParseHtmlString("#4CAF50", out c);
                    break;
                case NexusKind.Custom:
                    ColorUtility.TryParseHtmlString("#FF6F6F", out c);
                    break;
            }

            if (networkLabel != null)
            {
                networkLabel.style.color = c;
            }
        }

        private enum NavTarget
        {
            Balances,
            History,
            Account
        }
    }
}
