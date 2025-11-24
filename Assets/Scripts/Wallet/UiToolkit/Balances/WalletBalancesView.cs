using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Unity.Core.Logging;
using Font = UnityEngine.Font;

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
        private readonly Font defaultFont;
        private DropdownField accountDropdown;

        private Label statusLabel;
        private Label platformLabel;
        private Button refreshButton;
        private ScrollView listView;
        private VisualElement root;
        private bool readyNotified;

        public WalletBalancesView(VisualElement host, WalletApplicationContext context, Action onReady)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            presenter = context.BalancePresenter ?? throw new ArgumentNullException(nameof(context.BalancePresenter));
            viewState = presenter.State ?? throw new ArgumentNullException(nameof(presenter.State));
            uiSignals = context.UiSignals ?? throw new ArgumentNullException(nameof(context.UiSignals));
            this.onReady = onReady;
            defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

            BuildLayout(host);
            Subscribe();
            RequestInitialRefresh();
            RefreshView();
        }

        public void Dispose()
        {
            Unsubscribe();
        }

        private void BuildLayout(VisualElement host)
        {
            root = host ?? throw new ArgumentNullException(nameof(host));
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1;
            root.style.width = new Length(100, LengthUnit.Percent);
            root.style.height = new Length(100, LengthUnit.Percent);
            root.style.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;
            root.style.paddingTop = 16;
            root.style.paddingBottom = 16;
            root.style.color = Color.white;
            root.style.alignItems = Align.Stretch;
            ApplyDefaultFont(root);

            var header = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.SpaceBetween,
                    alignItems = Align.Center,
                    marginBottom = 12
                },
                focusable = false
            };

            var titleBlock = BuildAccountBlock();

            refreshButton = new Button
            {
                text = "Refresh",
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    paddingLeft = 12,
                    paddingRight = 12,
                    backgroundColor = new Color(0.22f, 0.27f, 0.32f),
                    color = Color.white,
                    borderBottomWidth = 1,
                    borderTopWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderBottomColor = new Color(0.32f, 0.37f, 0.42f),
                    borderTopColor = new Color(0.32f, 0.37f, 0.42f),
                    borderLeftColor = new Color(0.32f, 0.37f, 0.42f),
                    borderRightColor = new Color(0.32f, 0.37f, 0.42f),
                    minHeight = 30
                }
            };
            ApplyDefaultFont(refreshButton);
            refreshButton.clicked += OnRefreshClicked;

            header.Add(titleBlock);
            header.Add(refreshButton);

            root.Add(header);

            statusLabel = new Label
            {
                text = "Initializing wallet UI...",
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 14,
                    marginBottom = 10,
                    color = Color.white,
                    paddingLeft = 6,
                    paddingRight = 6,
                    paddingTop = 4,
                    paddingBottom = 4,
                    backgroundColor = new Color(0.12f, 0.13f, 0.16f),
                    borderBottomLeftRadius = 4,
                    borderBottomRightRadius = 4,
                    borderTopLeftRadius = 4,
                    borderTopRightRadius = 4
                }
            };
            ApplyDefaultFont(statusLabel);
            root.Add(statusLabel);

            listView = new ScrollView(ScrollViewMode.Vertical)
            {
                style =
                {
                    flexGrow = 1,
                    backgroundColor = new Color(0.08f, 0.09f, 0.11f),
                    borderTopLeftRadius = 6,
                    borderTopRightRadius = 6,
                    borderBottomLeftRadius = 6,
                    borderBottomRightRadius = 6,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = new Color(0.12f, 0.14f, 0.17f),
                    borderRightColor = new Color(0.12f, 0.14f, 0.17f),
                    borderTopColor = new Color(0.12f, 0.14f, 0.17f),
                    borderBottomColor = new Color(0.12f, 0.14f, 0.17f),
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 8,
                    paddingBottom = 8
                }
            };
            listView.verticalScroller.valueChanged += v => viewState.ScrollY = v;

            root.Add(listView);
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
                Log.Write($"{LogPrefix}Requesting initial balances refresh for {accountManager.CurrentPlatform}");
                presenter.Refresh(true);
                return;
            }

            Log.Write($"{LogPrefix}Waiting for account selection before refreshing balances.");
        }

        private void OnRefreshClicked()
        {
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

        private void RefreshView()
        {
            try
            {
                var snapshot = context.ViewState.GetBalancesSnapshot(() => presenter.BuildSnapshot());
                statusLabel.text = "Loading balances...";
                var accountManager = AccountManager.Instance;
                UpdateAccountDropdown(accountManager);
                Log.Write($"{LogPrefix}RefreshView snapshot built. accountsReady={accountManager?.AccountsAreReadyToBeUsed} accounts={accountManager?.Accounts?.Count} selection={accountManager?.CurrentIndex}");
                if (accountManager == null || accountManager.Accounts == null || accountManager.Accounts.Count == 0)
                {
                    statusLabel.text = "No accounts loaded yet...";
                    listView.Clear();
                    NotifyReady("no accounts");
                    return;
                }

                if (accountManager.Settings == null)
                {
                    statusLabel.text = "Settings are not loaded yet.";
                    listView.Clear();
                    NotifyReady("settings missing");
                    return;
                }

                var displayName = string.IsNullOrEmpty(snapshot.AccountName) ? "Wallet" : snapshot.AccountName;
                if (accountDropdown != null && accountDropdown.choices != null && accountDropdown.choices.Count > 0)
                {
                    accountDropdown.SetValueWithoutNotify(displayName);
                }

                platformLabel.text = snapshot.Platform.ToString();

                if (accountManager.CurrentAccount.passwordProtected && string.IsNullOrEmpty(accountManager.CurrentPasswordHash))
                {
                    statusLabel.text = "Wallet is locked. Open it from the wallet list.";
                    listView.Clear();
                    NotifyReady("locked");
                    return;
                }

                var balances = snapshot.Balances ?? Array.Empty<WalletBalanceEntry>();
                var filteredBalances = FilterBalances(balances, accountManager.Settings.balanceDisplayThreshold);
                Log.Write($"{LogPrefix}Balances snapshot stats: raw={balances.Count()} filtered={filteredBalances.Count} refreshing={snapshot.IsRefreshing} error={snapshot.ErrorMessage}");

                listView.Clear();

                if (snapshot.IsRefreshing)
                {
                    statusLabel.text = "Fetching balances...";
                    NotifyReady("refreshing");
                    return;
                }

                if (snapshot.HasError)
                {
                    statusLabel.text = snapshot.ErrorMessage;
                    NotifyReady("error");
                    return;
                }

                statusLabel.text = $"{filteredBalances.Count} assets";

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

                listView.verticalScroller.value = Mathf.Max(0f, viewState.ScrollY);

                NotifyReady("snapshot ready");
            }
            catch (Exception e)
            {
                statusLabel.text = $"Error: {e.Message}";
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

        public void OnAccountsReady()
        {
            var accountManager = AccountManager.Instance;
            UpdateAccountDropdown(accountManager);
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
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 6,
                    paddingBottom = 6,
                    marginBottom = 6,
                    backgroundColor = new Color(0.12f, 0.13f, 0.16f),
                    borderTopLeftRadius = 4,
                    borderTopRightRadius = 4,
                    borderBottomLeftRadius = 4,
                    borderBottomRightRadius = 4
                }
            };

            var iconTexture = ResourceManager.Instance?.GetToken(entry.Symbol, platform) as Texture2D;
            if (iconTexture != null)
            {
                var icon = new Image
                {
                    image = iconTexture,
                    scaleMode = ScaleMode.ScaleToFit,
                    style =
                    {
                        width = 32,
                        height = 32,
                        marginRight = 10
                    }
                };
                row.Add(icon);
            }

            var textBlock = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1
                }
            };

            var title = new Label($"{entry.AvailableText} {entry.Symbol}")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 14
                }
            };
            textBlock.Add(title);

            var secondary = new Label(BuildSecondaryLine(entry))
            {
                style =
                {
                    color = new Color(0.7f, 0.75f, 0.8f),
                    fontSize = 11
                }
            };
            textBlock.Add(secondary);

            if (!string.IsNullOrEmpty(entry.FiatWorth))
            {
                var fiat = new Label(entry.FiatWorth)
                {
                    style =
                    {
                        color = new Color(0.55f, 0.8f, 0.6f),
                        fontSize = 11
                    }
                };
                textBlock.Add(fiat);
            }

            row.Add(textBlock);

            return row;
        }

        private string BuildSecondaryLine(WalletBalanceEntry entry)
        {
            var parts = new List<string>();
            if (entry.Staked > System.Numerics.BigInteger.Zero)
            {
                parts.Add($"Staked {entry.StakedText}");
            }

            if (entry.Claimable > System.Numerics.BigInteger.Zero)
            {
                parts.Add($"Claimable {entry.ClaimableText}");
            }

            return parts.Count == 0 ? entry.Chain : $"{entry.Chain} | {string.Join(" | ", parts)}";
        }

        private VisualElement BuildAccountBlock()
        {
            var block = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1
                }
            };

            accountDropdown = new DropdownField
            {
                label = string.Empty,
                style =
                {
                    flexGrow = 0,
                    width = 240,
                    maxWidth = 260,
                    marginRight = 10
                }
            };
            accountDropdown.choices = new List<string>();
            accountDropdown.SetValueWithoutNotify(string.Empty);
            ApplyDefaultFont(accountDropdown);
            accountDropdown.RegisterValueChangedCallback(OnAccountChanged);
            block.Add(accountDropdown);

            platformLabel = new Label
            {
                text = "Platform",
                style =
                {
                    color = new Color(0.7f, 0.75f, 0.8f),
                    fontSize = 12,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginTop = -4
                }
            };
            ApplyDefaultFont(platformLabel);
            block.Add(platformLabel);

            return block;
        }

        private void OnAccountChanged(ChangeEvent<string> evt)
        {
            var newValue = evt.newValue;
            var accountManager = AccountManager.Instance;
            if (accountManager == null || accountManager.Accounts == null || accountManager.Accounts.Count == 0)
            {
                return;
            }

            if (accountDropdown == null || accountDropdown.choices == null || accountDropdown.choices.Count == 0)
            {
                return;
            }

            var index = accountDropdown.choices.IndexOf(newValue);
            if (index < 0 || index >= accountManager.Accounts.Count)
            {
                return;
            }

            var alreadySelected = accountManager.HasSelection && accountManager.CurrentIndex == index;
            if (alreadySelected)
            {
                return;
            }

            accountManager.SelectAccount(index);
            context.ViewState.ResetSnapshots();
            context.ViewState.MarkBalancesDirty();
            presenter.Refresh(true);
            RefreshView();
        }

        private void UpdateAccountDropdown(AccountManager accountManager)
        {
            if (accountDropdown == null)
            {
                return;
            }

            if (accountManager == null || accountManager.Accounts == null || accountManager.Accounts.Count == 0)
            {
                accountDropdown.choices = new List<string>();
                accountDropdown.SetValueWithoutNotify("No accounts");
                accountDropdown.SetEnabled(false);
                return;
            }

            var names = accountManager.Accounts.Select((a, i) =>
            {
                var name = string.IsNullOrWhiteSpace(a.name) ? $"Account {i + 1}" : a.name.Trim();
                return name;
            }).ToList();

            accountDropdown.choices = names;
            accountDropdown.SetEnabled(true);

            if (accountManager.HasSelection && accountManager.CurrentIndex >= 0 && accountManager.CurrentIndex < names.Count)
            {
                accountDropdown.SetValueWithoutNotify(names[accountManager.CurrentIndex]);
            }
            else
            {
                accountDropdown.SetValueWithoutNotify(string.Empty);
            }
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
    }
}
