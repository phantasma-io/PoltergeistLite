using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.NFT.Extensions;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Unity.Core.Logging;
using PhantasmaPhoenix.Cryptography;
using Poltergeist;
using Poltergeist.Wallet;
using Poltergeist.UiToolkit;
using WalletSortDirection = Poltergeist.Wallet.SortDirection;

namespace Poltergeist.UiToolkit.Balances
{
    /// <summary>
    /// NFT-centric dashboard: filtering, selection, transfer and burn flows in a single UITK screen.
    /// </summary>
    public sealed partial class WalletNftDashboardView : IDisposable
    {
        private const string LogPrefix = "[UITK] ";
        private const float CompactNftWidth = 1100f;

        private readonly WalletApplicationContext context;
        private readonly WalletUiModalHost modalHost;
        private readonly WalletUiSignals uiSignals;
        private readonly WalletNftPresenter nftPresenter;
        private readonly WalletNftSource nftSource;
        private readonly WalletNftTransferService nftTransferService;
        private readonly WalletBurnService burnService;
        private readonly WalletAuthService authService;
        private readonly WalletFeeRequirement feeRequirement;
        private readonly WalletAmountValidator amountValidator;
        private readonly WalletTransactionOrchestrator transactionOrchestrator;
        private readonly WalletUiTransactionDialogs transactionDialogs;
        private readonly WalletUiTransactionAdapter transactionUi;
        private readonly Action onShowBalances;
        private readonly Action onShowHistory;
        private readonly Action onShowAccount;
        private readonly Action onShowSettings;
        private readonly Action onExit;
        private EventCallback<KeyDownEvent> tabBlockHandler;

        private VisualElement root;
        private ScrollView listView;
        private SubHeaderElements subHeader;
        private Label subtitleLabel;
        private Label subtitleNetworkLabel;
        private Label summaryLabel;
        private Label statusLabel;
        private Label totalCountLabel;
        private Label selectedCountLabel;
        private Label supplyInlineLabel;
        private Label pageInfoLabel;
        private Label symbolLabel;
        private Label filterHintLabel;
        private Image tokenIcon;
        private VisualElement heroCard;
        private VisualElement filtersPanel;
        private VisualElement filtersRow;
        private VisualElement filtersButtonRow;
        private VisualElement filtersSelectionGroup;
        private VisualElement filtersContractGroup;
        private VisualElement filtersButtonSpacer;
        private Button toggleFiltersButton;
        private bool filtersExpanded = true;
        private bool filtersExpandedUserOverride;
        private VisualElement selectionActionsCloud;
        private VisualElement paginationRow;
        private Button firstPageButton;
        private Button prevPageButton;
        private Button nextPageButton;
        private Button lastPageButton;
        private VisualElement listContainer;
        private Button refreshButton;
        private Button sendButton;
        private Button burnButton;
        private Button clearSelectionButton;
        private Button selectAllButton;
        private Button invertSelectionButton;
        private Button sortDirectionButton;
        private Button navBalances;
        private Button navHistory;
        private Button navAccount;
        private Button navExit;
        private TextField nameFilterField;
        private PopupField<string> mintedFilterDropdown;
        private PopupField<string> typeFilterDropdown;
        private PopupField<string> rarityFilterDropdown;
        private PopupField<string> sortModeDropdown;
        private Button contractInfoButton;
        private string currentSymbol;
        private readonly Dictionary<Button, bool> actionEnableCache = new Dictionary<Button, bool>();
        // Refresh sequencing for the current NFT symbol.
        // InitialPass (warmup) = non-forced refresh used when opening a collection to show cached data quickly
        // and fill missing details without clearing caches; ForcePass = explicit refresh (manual/queued) that
        // runs after warmup or balance updates to ensure the latest ids and metadata are loaded.
        private enum NftRefreshPhase
        {
            Idle,
            InitialPass,
            ForcePass
        }

        // Current refresh phase for the symbol we are actively refreshing (used to prevent overlapping refreshes).
        private NftRefreshPhase refreshPhase = NftRefreshPhase.Idle;
        // Symbol currently being refreshed; prevents cross-symbol refresh overlap and drives retry scheduling.
        private string refreshSymbol;
        // UI-level latch: used to disable the Refresh button and show the status label while a refresh sequence is in flight.
        private bool isRefreshing;
        // Queued follow-up refresh when another refresh is requested while one is already running.
        // This ensures manual clicks or balance-triggered refreshes are not lost; it triggers exactly one extra ForcePass.
        private bool pendingForceRefresh;
        // Manual refresh requires balances first (NFT ids live in balances), then a follow-up NFT refresh.
        // This flag signals that we are waiting for BalancesUpdated to run the NFT refresh for the same symbol.
        private bool pendingBalanceRefresh;
        // Symbol to refresh once balances update; cached because currentSymbol can change while balances are loading.
        private string pendingBalanceRefreshSymbol;
        // Platform filter for BalancesUpdated; when set, ignore updates from other platforms.
        private PlatformKind pendingBalanceRefreshPlatform = PlatformKind.None;
        // Status text used during refresh sequences.
        private const string RefreshStatusMessage = "Refreshing NFTs...";

        private static readonly (nftMinted value, string label)[] MintedOptions =
        {
            (nftMinted.All, "Minted: All"),
            (nftMinted.Last_15_Mins, "Minted: Last 15 mins"),
            (nftMinted.Last_Hour, "Minted: Last hour"),
            (nftMinted.Last_24_Hours, "Minted: Last 24 hours"),
            (nftMinted.Last_Week, "Minted: Last week"),
            (nftMinted.Last_Month, "Minted: Last month")
        };

        public WalletNftDashboardView(
            VisualElement host,
            WalletApplicationContext context,
            WalletUiModalHost modalHost,
            Action onShowBalances,
            Action onShowHistory,
            Action onShowAccount,
            Action onShowSettings,
            Action onExit,
            IWalletAuthUi authUi)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.modalHost = modalHost ?? throw new ArgumentNullException(nameof(modalHost));
            uiSignals = context.UiSignals ?? throw new ArgumentNullException(nameof(context.UiSignals));
            nftPresenter = context.NftViewPresenter ?? throw new ArgumentNullException(nameof(context.NftViewPresenter));
            nftSource = context.NftSource ?? throw new ArgumentNullException(nameof(context.NftSource));
            nftTransferService = context.NftTransferService ?? throw new ArgumentNullException(nameof(context.NftTransferService));
            burnService = context.BurnService ?? throw new ArgumentNullException(nameof(context.BurnService));
            authService = context.AuthService ?? throw new ArgumentNullException(nameof(context.AuthService));
            feeRequirement = context.FeeRequirement ?? throw new ArgumentNullException(nameof(context.FeeRequirement));
            amountValidator = context.AmountValidator ?? throw new ArgumentNullException(nameof(context.AmountValidator));
            this.onShowBalances = onShowBalances ?? throw new ArgumentNullException(nameof(onShowBalances));
            this.onShowHistory = onShowHistory ?? throw new ArgumentNullException(nameof(onShowHistory));
            this.onShowAccount = onShowAccount ?? throw new ArgumentNullException(nameof(onShowAccount));
            this.onShowSettings = onShowSettings ?? throw new ArgumentNullException(nameof(onShowSettings));
            this.onExit = onExit ?? throw new ArgumentNullException(nameof(onExit));

            transactionDialogs = new WalletUiTransactionDialogs(this.modalHost, () => AccountManager.Instance, SetStatus);
            transactionUi = new WalletUiTransactionAdapter(authService, authUi ?? throw new ArgumentNullException(nameof(authUi)), SetStatus, SetActionsEnabled, ShowSendProgressAsync, StartConfirmationAsync);
            transactionDialogs.RegisterBlockingPanels(null, null, null, null);
            transactionOrchestrator = new WalletTransactionOrchestrator(() => AccountManager.Instance, transactionUi);

            BuildLayout(host ?? throw new ArgumentNullException(nameof(host)));
            Subscribe();
            RefreshView();
        }

        public void Dispose()
        {
            Unsubscribe();
            transactionDialogs?.Dispose();
            WalletUiCommon.UnblockTabNavigation(root, tabBlockHandler);
            tabBlockHandler = null;
        }

        public void ShowToken(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
            {
                currentSymbol = string.Empty;
                context.ViewState.TokenDashboardSymbol = string.Empty;
                context.ViewState.TransferSymbol = string.Empty;
                // Normal navigation: drop debug mode when leaving the debug NFT view.
                context.ViewState.IsDebugNftActive = false;
                RefreshView();
                return;
            }

            currentSymbol = symbol;
            context.ViewState.TokenDashboardSymbol = symbol;
            context.ViewState.TransferSymbol = symbol;
            // Normal navigation: drop debug mode when switching to a regular collection.
            context.ViewState.IsDebugNftActive = false;

            PrepareStateForSymbol(symbol);
            StartRefreshSequence(symbol, includeWarmup: true);

            RefreshView();
        }

        public void ShowDebugNft(string symbol, string tokenId)
        {
            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(tokenId))
            {
                SetStatus("NFT identifier is required.");
                return;
            }

            symbol = symbol.Trim();
            tokenId = tokenId.Trim();

            currentSymbol = symbol;
            context.ViewState.TokenDashboardSymbol = symbol;
            context.ViewState.TransferSymbol = symbol;
            PrepareStateForSymbol(symbol);
            // Debug mode allows rendering without a selected wallet; detail actions remain locked.
            context.ViewState.IsDebugNftActive = true;

            var current = context.ViewState.PeekNftInspect();
            if (!current.HasValue
                || !string.Equals(current.Value.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(current.Value.TokenId, tokenId, StringComparison.OrdinalIgnoreCase))
            {
                // Push a locked inspect entry so Send/Burn remain disabled in detail view.
                context.ViewState.PushNftInspect(new WalletNftInspectEntry(symbol, tokenId, locked: true));
            }
            RefreshView();
        }

        public void OnAccountsReady()
        {
            var accountManager = AccountManager.Instance;
            var isDebugView = context.ViewState?.IsDebugNftActive ?? false;
            var hasWalletSelection = accountManager != null && accountManager.HasSelection;
            // Debug view without a selected wallet would trigger a refresh that never completes.
            var skipAutoRefresh = isDebugView && !hasWalletSelection;

            if (!skipAutoRefresh && !string.IsNullOrWhiteSpace(currentSymbol) && refreshPhase == NftRefreshPhase.Idle && !nftSource.IsRefreshingForSymbol(currentSymbol))
            {
                StartRefreshSequence(currentSymbol, includeWarmup: true);
            }

            RefreshView();
        }

        public void MarkAsActive()
        {
            UpdateNavSelection();
        }

        // Keep collection transitions clean: avoid leaking filters/selection from the previous symbol before we trigger a refresh.
        private void PrepareStateForSymbol(string symbol)
        {
            context.ViewState.NftScrollY = 0f;
            context.ViewState.ClearNftInspectTrail();
            nftPresenter.ClearSelection();
            nftPresenter.ResetFiltersAndPagination();
            nftPresenter.ResetSorting();
            context.ViewState.MarkNftDirty(symbol);
        }

        private void StartRefreshSequence(string symbol, bool includeWarmup)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return;
            }

            // Refresh sequence overview:
            // - InitialPass (warmup) uses force=false to keep cached data and quickly populate the UI.
            // - ForcePass is an explicit refresh (manual or queued) that should run after warmup/balance updates.
            // Avoid stacking duplicate requests for the same symbol.
            if (refreshPhase != NftRefreshPhase.Idle && string.Equals(refreshSymbol, symbol, StringComparison.OrdinalIgnoreCase))
            {
                // Queue a follow-up refresh when a new request arrives during an in-flight pass.
                // includeWarmup=false -> manual/forced refresh; includeWarmup=true -> we still want a force pass after warmup.
                pendingForceRefresh |= !includeWarmup;
                return;
            }

            refreshSymbol = symbol;
            refreshPhase = includeWarmup ? NftRefreshPhase.InitialPass : NftRefreshPhase.ForcePass;
            // When opening a collection (warmup), arm a follow-up ForcePass to ensure fresh data after the initial pass.
            pendingForceRefresh = includeWarmup ? true : false;

            UpdateRefreshingState(true);
            TryKickoffRefresh();
        }

        private void Subscribe()
        {
            uiSignals.EnsureSubscribed();
            uiSignals.NftsUpdated += OnNftsUpdated;
            uiSignals.NftsRefreshStarted += OnNftsRefreshStarted;
            uiSignals.SettingsChanged += OnSettingsChanged;
            uiSignals.BalancesUpdated += OnBalancesUpdated;
        }

        private void Unsubscribe()
        {
            uiSignals.NftsUpdated -= OnNftsUpdated;
            uiSignals.NftsRefreshStarted -= OnNftsRefreshStarted;
            uiSignals.SettingsChanged -= OnSettingsChanged;
            uiSignals.BalancesUpdated -= OnBalancesUpdated;
        }

        private void OnNftsUpdated(PlatformKind platform, string symbol)
        {
            context.ViewState.MarkNftDirty(symbol);
            RefreshView();

            // NftsUpdated fires for any symbol; only the active refreshSymbol drives the refresh sequence state machine.
            if (!string.Equals(symbol, refreshSymbol, StringComparison.OrdinalIgnoreCase))
            {
                // If our target symbol is waiting for a refresh and is no longer refreshing, kick it off now.
                if (refreshPhase != NftRefreshPhase.Idle && !string.IsNullOrWhiteSpace(refreshSymbol) && !nftSource.IsRefreshingForSymbol(refreshSymbol))
                {
                    TryKickoffRefresh();
                }

                return;
            }

            if (pendingForceRefresh)
            {
                // A refresh was requested while another pass was already running.
                // Honor it regardless of the current phase (warmup or force) so queued refreshes always run.
                refreshPhase = NftRefreshPhase.ForcePass;
                pendingForceRefresh = false;
                TryKickoffRefresh();
                return;
            }

            // No queued refreshes remain; clear the state and re-enable the UI.
            refreshPhase = NftRefreshPhase.Idle;
            refreshSymbol = null;
            pendingForceRefresh = false;
            UpdateRefreshingState(false);
        }

        private void OnNftsRefreshStarted(PlatformKind platform, string symbol)
        {
            context.ViewState.MarkNftDirty(symbol);
            RefreshView();
            if (string.Equals(symbol, refreshSymbol, StringComparison.OrdinalIgnoreCase))
            {
                UpdateRefreshingState(true);
            }
        }

        private void OnBalancesUpdated(PlatformKind platform)
        {
            if (!pendingBalanceRefresh)
            {
                return;
            }

            var symbol = pendingBalanceRefreshSymbol;
            if (string.IsNullOrWhiteSpace(symbol))
            {
                pendingBalanceRefresh = false;
                pendingBalanceRefreshPlatform = PlatformKind.None;
                return;
            }

            // Ignore balance updates from other platforms; we only want the refresh we asked for.
            if (pendingBalanceRefreshPlatform != PlatformKind.None && pendingBalanceRefreshPlatform != platform)
            {
                return;
            }

            // Clear pending state before starting the follow-up refresh to avoid re-entrancy issues.
            pendingBalanceRefresh = false;
            pendingBalanceRefreshSymbol = null;
            pendingBalanceRefreshPlatform = PlatformKind.None;

            // NFT IDs come from balances; refresh NFTs again after balances update so new mints appear.
            StartRefreshSequence(symbol, includeWarmup: false);
        }

        private void OnSettingsChanged()
        {
            RefreshView();
        }

        private void TryKickoffRefresh()
        {
            if (string.IsNullOrWhiteSpace(refreshSymbol))
            {
                return;
            }

            if (nftSource.IsRefreshingForSymbol(refreshSymbol))
            {
                return; // Wait for the current refresh to finish; OnNftsUpdated will retry.
            }

            // ForcePass bypasses caches; InitialPass is a warmup that can use cached data.
            var force = refreshPhase == NftRefreshPhase.ForcePass;
            nftPresenter.Refresh(refreshSymbol, force);
            context.ViewState.MarkNftDirty(refreshSymbol);
            RefreshView();
        }

        private void BuildLayout(VisualElement host)
        {
            root = host;
            root.Clear();
            WalletUiCommon.ConfigureScreenRoot(root);
            root.style.backgroundColor = Color.clear;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;
            root.style.paddingTop = 16;
            root.style.paddingBottom = 16;
            root.style.color = WalletUiTheme.TextPrimary;
            WalletUiCommon.ApplyDefaultFont(root);
            tabBlockHandler = WalletUiCommon.BlockTabNavigation(root);

            var compactInitial = WalletUiCommon.IsCompactWidth(root, CompactNftWidth);
            filtersExpanded = !compactInitial;
            filtersExpandedUserOverride = false;

            var content = WalletUiCommon.CreateScreenContent(paddingLeft: 8, paddingRight: 8);

            refreshButton = WalletUiCommon.CreateSecondaryButton("Refresh", OnRefreshClicked, 14, 32);
            refreshButton.style.minWidth = 120;

            var headerBlock = WalletUiCommon.BuildHeaderBlock(
                subHeaderSubtitle: "NFTs",
                subHeaderLeft: string.Empty,
                rightContent: refreshButton,
                middleContent: null,
                headerMarginBottom: 10f,
                subHeaderMarginTop: 10f,
                subHeaderMarginBottom: 8f);
            subHeader = headerBlock.SubHeader;
            subtitleLabel = subHeader.SubtitleLabel;
            subtitleNetworkLabel = subHeader.NetworkLabel;
            summaryLabel = subHeader.LeftLabel;
            headerBlock.Root.style.flexShrink = 0;
            WalletUiCommon.EnableCompactHeaderActionRow(headerBlock, refreshButton);
            content.Add(headerBlock.Root);

            statusLabel = WalletUiCommon.CreateStatusLabel();
            content.Add(statusLabel);

            listContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1
                }
            };
            WalletUiCommon.ApplyDefaultFont(listContainer);

            heroCard = BuildHeroCard();
            heroCard.style.marginTop = 12;
            listContainer.Add(heroCard);

            if (!ShouldHideFilters())
            {
                filtersPanel = BuildFiltersPanel();
                listContainer.Add(filtersPanel);
            }

            var listWrapper = WalletUiCommon.BuildScrollContainer(
                out listView,
                v => context.ViewState.NftScrollY = v,
                shouldBlockWheel: () => modalHost?.Overlay != null && modalHost.Overlay.style.display == DisplayStyle.Flex,
                paddingLeft: 4f,
                paddingRight: 4f,
                paddingTop: 6f,
                paddingBottom: 90f,
                marginTop: 4f,
                marginBottom: 10f,
                maxWidth: 1680f,
                alignSelf: Align.Center);
            listView.style.flexGrow = 1;
            listView.style.flexShrink = 1;
            listWrapper.style.flexGrow = 1;
            listContainer.Add(listWrapper);

            selectionActionsCloud = BuildActionsRow();
            listContainer.Add(selectionActionsCloud);

            paginationRow = BuildPaginationRow();
            listContainer.Add(paginationRow);
            content.Add(listContainer);

            detailContainer = BuildDetailContainer();
            detailContainer.style.display = DisplayStyle.None;
            content.Add(detailContainer);

            var footer = WalletUiCommon.BuildWalletNavBar(
                out navBalances,
                out navHistory,
                out navAccount,
                out navExit,
                () => { ExitDebugView(); onShowBalances?.Invoke(); },
                () => { ExitDebugView(); onShowHistory?.Invoke(); },
                () => { ExitDebugView(); onShowAccount?.Invoke(); },
                HandleBackNavigation);
            if (navExit != null)
            {
                navExit.text = "Back";
            }
            footer.style.flexShrink = 0;
            content.Add(footer);

            root.Add(content);
        }

        private VisualElement BuildHeroCard()
        {
            var card = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 18,
                    paddingRight = 18,
                    paddingTop = 14,
                    paddingBottom = 14,
                    position = Position.Relative,
                    marginBottom = 12
                }
            };
            WalletUiCommon.ApplyDefaultFont(card);
            WalletUiCommon.ApplyCardStyle(card, WalletUiTheme.GetCardGradientTexture(), WalletUiTheme.RadiusMedium);

            tokenIcon = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
                style =
                {
                    width = 56,
                    height = 56,
                    marginRight = 14,
                    backgroundColor = Color.clear
                }
            };
            card.Add(tokenIcon);

            var titleBlock = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1
                }
            };
            symbolLabel = new Label("NFTs")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 22,
                    color = WalletUiTheme.TextPrimary
                }
            };
            WalletUiCommon.ApplyDefaultFont(symbolLabel);
            titleBlock.Add(symbolLabel);

            var infoRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginTop = 2
                }
            };
            WalletUiCommon.ApplyDefaultFont(infoRow);

            totalCountLabel = new Label("0 items")
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 14,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginRight = 10
                }
            };
            WalletUiCommon.ApplyDefaultFont(totalCountLabel);

            selectedCountLabel = new Label("0 selected")
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 14,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginRight = 10
                }
            };
            WalletUiCommon.ApplyDefaultFont(selectedCountLabel);

            supplyInlineLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 13,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            WalletUiCommon.ApplyDefaultFont(supplyInlineLabel);

            infoRow.Add(totalCountLabel);
            infoRow.Add(selectedCountLabel);
            infoRow.Add(supplyInlineLabel);
            titleBlock.Add(infoRow);

            var pageBlock = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    alignItems = Align.FlexEnd
                }
            };
            pageInfoLabel = new Label(string.Empty)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 16,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleRight
                }
            };
            WalletUiCommon.ApplyDefaultFont(pageInfoLabel);
            filterHintLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 13,
                    unityTextAlign = TextAnchor.MiddleRight,
                    marginTop = 2
                }
            };
            WalletUiCommon.ApplyDefaultFont(filterHintLabel);

            pageBlock.Add(pageInfoLabel);
            pageBlock.Add(filterHintLabel);

            card.Add(titleBlock);
            card.Add(pageBlock);
            return card;
        }

        private VisualElement BuildFiltersPanel()
        {
            var panel = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    paddingLeft = 14,
                    paddingRight = 14,
                    paddingTop = 12,
                    paddingBottom = 12,
                    backgroundColor = WalletUiTheme.PanelBackground,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.ModalBorder,
                    borderRightColor = WalletUiTheme.ModalBorder,
                    borderTopColor = WalletUiTheme.HighlightEdge,
                    borderBottomColor = WalletUiTheme.ModalBorder,
                    marginBottom = 12
                }
            };
            WalletUiCommon.ApplyDefaultFont(panel);

            toggleFiltersButton = WalletUiCommon.CreateSecondaryButton("Hide filters", ToggleFiltersVisibility, 14, 30);
            toggleFiltersButton.style.alignSelf = Align.FlexStart;
            toggleFiltersButton.style.marginBottom = 6;
            panel.Add(toggleFiltersButton);

            filtersRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    alignItems = Align.Stretch,
                    justifyContent = Justify.FlexStart,
                    flexGrow = 1,
                    width = new Length(100, LengthUnit.Percent)
                }
            };
            WalletUiCommon.ApplyDefaultFont(filtersRow);

            nameFilterField = WalletUiFormFactory.CreateTextField("Name", string.Empty, value => OnFiltersChanged(value, null, null, null));
            ConfigureFilterField(nameFilterField, 90f);
            filtersRow.Add(nameFilterField);

            mintedFilterDropdown = WalletUiFormFactory.CreateDropdown(string.Empty, MintedOptions.Select(x => x.label).ToList(), 0, idx => OnFiltersChanged(null, null, null, MintedOptions[Mathf.Clamp(idx, 0, MintedOptions.Length - 1)].value));
            ConfigureFilterField(mintedFilterDropdown, 90f);
            filtersRow.Add(mintedFilterDropdown);

            typeFilterDropdown = WalletUiFormFactory.CreateDropdown(string.Empty, new List<string> { "Type: All" }, 0, _ => { });
            ConfigureFilterField(typeFilterDropdown, 90f);
            typeFilterDropdown.RegisterValueChangedCallback(_ =>
            {
                var idx = Mathf.Clamp(typeFilterDropdown.index, 0, Enum.GetValues(typeof(ttrsNftType)).Length - 1);
                OnFiltersChanged(null, (ttrsNftType)idx, null, null);
            });
            filtersRow.Add(typeFilterDropdown);

            rarityFilterDropdown = WalletUiFormFactory.CreateDropdown(string.Empty, new List<string> { "Rarity: All" }, 0, _ => { });
            ConfigureFilterField(rarityFilterDropdown, 90f);
            rarityFilterDropdown.RegisterValueChangedCallback(_ =>
            {
                var idx = Mathf.Clamp(rarityFilterDropdown.index, 0, Enum.GetValues(typeof(ttrsNftRarity)).Length - 1);
                OnFiltersChanged(null, null, (ttrsNftRarity)idx, null);
            });
            filtersRow.Add(rarityFilterDropdown);

            sortModeDropdown = WalletUiFormFactory.CreateDropdown("Sort", new List<string>(), 0, idx => OnSortModeChanged(idx));
            ConfigureFilterField(sortModeDropdown, 90f);
            filtersRow.Add(sortModeDropdown);

            sortDirectionButton = WalletUiCommon.CreateSecondaryButton("Asc", ToggleSortDirection, 14, 34);
            ConfigureFilterField(sortDirectionButton, 90f);
            filtersRow.Add(sortDirectionButton);

            panel.Add(filtersRow);

            filtersButtonRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    alignItems = Align.Center,
                    justifyContent = Justify.SpaceBetween,
                    marginTop = 6
                }
            };
            WalletUiCommon.ApplyDefaultFont(filtersButtonRow);

            selectAllButton = WalletUiCommon.CreateSecondaryButton("Select filtered", () => RunSafeAsync(SelectFilteredAsync).Forget(ex => Log.WriteWarning($"{LogPrefix}Select filtered failed: {ex}")), 14, 32);
            selectAllButton.style.minWidth = 180;
            selectAllButton.style.marginRight = 8;
            invertSelectionButton = WalletUiCommon.CreateSecondaryButton("Invert selection", () => RunSafeAsync(InvertSelectionAsync).Forget(ex => Log.WriteWarning($"{LogPrefix}Invert selection failed: {ex}")), 14, 32);
            invertSelectionButton.style.minWidth = 180;
            invertSelectionButton.style.marginRight = 8;
            clearSelectionButton = WalletUiCommon.CreateSecondaryButton("Clear selection", () => RunSafeAsync(ClearSelectionAsync).Forget(ex => Log.WriteWarning($"{LogPrefix}Clear selection failed: {ex}")), 14, 32);
            clearSelectionButton.style.minWidth = 180;

            filtersButtonRow.Add(selectAllButton);
            filtersButtonRow.Add(invertSelectionButton);
            filtersButtonRow.Add(clearSelectionButton);

            contractInfoButton = WalletUiCommon.CreateSecondaryButton("Contract info", () => OpenContractInfo(), 14, 32);
            contractInfoButton.style.minWidth = 180;
            filtersButtonSpacer = new HSpacer();
            filtersButtonRow.Add(filtersButtonSpacer);
            filtersButtonRow.Add(contractInfoButton);

            panel.Add(filtersButtonRow);
            panel.RegisterCallback<GeometryChangedEvent>(_ => ApplyFiltersLayout());
            UpdateFiltersVisibility(true);
            return panel;
        }

        // Normalizes filter controls so they line up in rows instead of stretching to full width.
        private void ConfigureFilterField(VisualElement field, float minWidth, float maxWidth = 320f)
        {
            if (field == null)
            {
                return;
            }

            field.style.width = StyleKeyword.Auto;
            field.style.minWidth = minWidth;
            field.style.maxWidth = maxWidth;
            field.style.flexGrow = 1;
            field.style.flexShrink = 1;
            field.style.marginRight = 8;
            field.style.marginBottom = 8;
        }

        private VisualElement BuildActionsRow()
        {
            sendButton = WalletUiCommon.CreateSecondaryButton("Send", () => RunSafeAsync(SendAsync).Forget(ex => Log.WriteWarning($"{LogPrefix}Send failed: {ex}")), 16, 44);
            sendButton.style.minWidth = 160;

            burnButton = WalletUiCommon.CreateSecondaryButton("Burn", () => RunSafeAsync(BurnAsync).Forget(ex => Log.WriteWarning($"{LogPrefix}Burn failed: {ex}")), 16, 44);
            burnButton.style.minWidth = 140;

            var row = WalletUiCommon.CreateButtonRow(8f, sendButton, burnButton);
            return row;
        }

        private VisualElement BuildPaginationRow()
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    flexWrap = Wrap.Wrap,
                    marginBottom = 10
                }
            };
            WalletUiCommon.ApplyDefaultFont(row);

            firstPageButton = WalletUiCommon.CreateSecondaryButton("<<", () => OnPageChanged(PageChange.First), 14, 32);
            prevPageButton = WalletUiCommon.CreateSecondaryButton("<", () => OnPageChanged(PageChange.Previous), 14, 32);
            nextPageButton = WalletUiCommon.CreateSecondaryButton(">", () => OnPageChanged(PageChange.Next), 14, 32);
            lastPageButton = WalletUiCommon.CreateSecondaryButton(">>", () => OnPageChanged(PageChange.Last), 14, 32);

            row.Add(firstPageButton);
            row.Add(prevPageButton);
            row.Add(nextPageButton);
            row.Add(lastPageButton);

            return row;
        }

        private void RefreshView()
        {
            try
            {
                UpdateNavSelection();
                var accountManager = AccountManager.Instance;
                if (accountManager == null)
                {
                    SetStatus("Account is not ready.");
                    ClearUi();
                    return;
                }

                var settings = accountManager.Settings;
                if (settings == null)
                {
                    SetStatus("Settings are not loaded yet.");
                    ClearUi();
                    return;
                }

                var symbol = string.IsNullOrWhiteSpace(currentSymbol) ? context.ViewState.TokenDashboardSymbol : currentSymbol;
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    SetStatus("Pick an NFT collection from Balances to open its dashboard.");
                    ClearUi();
                    return;
                }

                context.ViewState.TransferSymbol = symbol;
                // Debug view can render without a selected/locked wallet; normal view requires it.
                var isDebugView = context.ViewState?.IsDebugNftActive ?? false;

                if (!accountManager.HasSelection && !isDebugView)
                {
                    SetStatus("Select a wallet first.");
                    ClearUi();
                    return;
                }

                if (accountManager.CurrentAccount.passwordProtected && string.IsNullOrEmpty(accountManager.CurrentPasswordHash) && !isDebugView)
                {
                    SetStatus("Wallet is locked. Open it from the wallet list.");
                    ClearUi();
                    return;
                }

                if (accountManager.CurrentState == null && !isDebugView)
                {
                    SetStatus("Account state is unavailable.");
                    ClearUi();
                    return;
                }

                var nftSnapshot = context.ViewState.GetNftSnapshot(symbol, s => nftPresenter.BuildSnapshot(s));
                UpdateFiltersUi(symbol);
                if (nftSnapshot != null)
                {
                    UpdateHero(symbol, nftSnapshot);
                    UpdatePaginationButtons(nftSnapshot);
                }
                if (nftSnapshot == null)
                {
                    SetStatus("No NFTs to show.");
                    ClearUi();
                    return;
                }

                if (nftSnapshot.IsRefreshing && (nftSnapshot.FilteredTokens == null || nftSnapshot.FilteredTokens.Count == 0))
                {
                    SetStatus($"Fetching {symbol} NFTs...");
                    ClearUi();
                    return;
                }

                if (nftSnapshot.HasError)
                {
                    SetStatus(nftSnapshot.ErrorMessage);
                    ClearUi();
                    return;
                }

                subtitleLabel.text = WalletUiCommon.BuildContextSubtitle("NFTs", accountManager.CurrentAccount.name, accountManager.CurrentPlatform);
                WalletUiCommon.ApplyNetworkBadge(subtitleNetworkLabel, settings.nexusName, settings.nexusKind);

                var refreshing = nftSnapshot.IsRefreshing || isRefreshing || nftSource.IsRefreshingForSymbol(symbol) || refreshPhase != NftRefreshPhase.Idle;
                var currentNfts = nftSource.GetNfts(symbol);
                nftPresenter.PruneSelection(currentNfts?.Select(x => x.Id));
                nftPresenter.State.ApplyPagination(nftSnapshot.TotalCount, nftSnapshot.PageCount, nftSnapshot.PageNumber);

                if (context.ViewState.HasNftInspect)
                {
                    ShowDetailMode();
                    UpdateHero(symbol, nftSnapshot);
                    RenderDetailView(accountManager);
                    SetStatus(refreshing ? RefreshStatusMessage : string.Empty);
                    return;
                }

                ShowListMode();
                UpdateHero(symbol, nftSnapshot);
                summaryLabel.text = BuildSummaryLine(nftSnapshot.TotalCount, nftPresenter.State.SelectedCount);
                RenderList(symbol, nftSnapshot, accountManager);
                UpdateActions(symbol, accountManager);
                SetStatus(refreshing ? RefreshStatusMessage : string.Empty);
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{LogPrefix}NFT dashboard refresh failed: {e}");
                SetStatus($"Error: {e.Message}");
            }
        }

        private void RefreshNetworkBadge()
        {
            var settings = AccountManager.Instance?.Settings;
            if (settings != null)
            {
                WalletUiCommon.ApplyNetworkBadge(subtitleNetworkLabel, settings.nexusName, settings.nexusKind);
            }
        }

        private void UpdateHero(string symbol, WalletNftViewSnapshot snapshot)
        {
            symbolLabel.text = $"{symbol} NFTs";
            totalCountLabel.text = string.Empty;
            selectedCountLabel.text = string.Empty;
            totalCountLabel.style.display = DisplayStyle.None;
            selectedCountLabel.style.display = DisplayStyle.None;
            pageInfoLabel.text = snapshot.PageCount > 0 ? $"Page {snapshot.PageNumber + 1} / {snapshot.PageCount}" : "Page 1 / 1";
            var totalLoaded = nftSource.GetNfts(symbol)?.Count ?? 0;
            filterHintLabel.text = totalLoaded == snapshot.TotalCount
                ? "No filters applied"
                : $"Filtered: {snapshot.TotalCount} of {totalLoaded}";

            UpdateSupply(symbol);

            if (ResourceManager.Instance != null)
            {
                var iconTexture = ResourceManager.Instance.GetToken(symbol, AccountManager.Instance?.CurrentPlatform ?? PlatformKind.None) as Texture2D;
                tokenIcon.image = iconTexture;
                tokenIcon.style.display = iconTexture != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            else
            {
                tokenIcon.style.display = DisplayStyle.None;
            }
        }

        private void UpdatePaginationButtons(WalletNftViewSnapshot snapshot)
        {
            if (firstPageButton == null && prevPageButton == null && nextPageButton == null && lastPageButton == null)
            {
                return;
            }

            // Disable pagination arrows when navigation is impossible (single page or already at edges).
            var hasPages = snapshot != null && snapshot.PageCount > 0;
            var canGoBack = hasPages && snapshot.PageNumber > 0;
            var canGoForward = hasPages && snapshot.PageNumber < snapshot.PageCount - 1;

            WalletUiCommon.SetButtonEnabledVisual(firstPageButton, canGoBack, WalletUiTheme.TextPrimary, WalletUiTheme.TextMuted);
            WalletUiCommon.SetButtonEnabledVisual(prevPageButton, canGoBack, WalletUiTheme.TextPrimary, WalletUiTheme.TextMuted);
            WalletUiCommon.SetButtonEnabledVisual(nextPageButton, canGoForward, WalletUiTheme.TextPrimary, WalletUiTheme.TextMuted);
            WalletUiCommon.SetButtonEnabledVisual(lastPageButton, canGoForward, WalletUiTheme.TextPrimary, WalletUiTheme.TextMuted);
        }

        private void UpdateSupply(string symbol)
        {
            var platform = AccountManager.Instance?.CurrentPlatform ?? PlatformKind.None;
            var token = Tokens.GetToken(symbol, platform);
            if (token == null)
            {
                supplyInlineLabel.text = string.Empty;
                return;
            }

            var supply = FormatSupplyLine("Supply", token.CurrentSupply, token.Decimals, symbol);
            var max = FormatSupplyLine("Max supply", token.MaxSupply, token.Decimals, symbol, treatZeroAsDash: true);
            var burned = FormatSupplyLine("Burned", token.BurnedSupply, token.Decimals, symbol);

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(supply)) parts.Add(supply);
            if (!string.IsNullOrEmpty(max)) parts.Add(max);
            if (!string.IsNullOrEmpty(burned)) parts.Add(burned);

            supplyInlineLabel.text = parts.Count == 0 ? string.Empty : string.Join(" | ", parts);
        }

        private string FormatSupplyLine(string label, string rawAmount, uint decimals, string symbol, bool treatZeroAsDash = false)
        {
            if (string.IsNullOrEmpty(rawAmount))
            {
                return string.Empty;
            }

            if (!BigInteger.TryParse(rawAmount, out var value))
            {
                return string.Empty;
            }

            if (treatZeroAsDash && value == BigInteger.Zero)
            {
                return $"{label} -";
            }

            return $"{label} {WalletAmountFormatter.Format(value, decimals)} {symbol}";
        }

        private void UpdateFiltersUi(string symbol)
        {
            if (ShouldHideFilters() || filtersPanel == null)
            {
                return;
            }

            var state = nftPresenter.State;

            nameFilterField?.SetValueWithoutNotify(state.FilterName ?? string.Empty);

            var mintedIndex = Array.FindIndex(MintedOptions, x => x.value == state.FilterMinted);
            mintedIndex = mintedIndex < 0 ? 0 : mintedIndex;
            mintedFilterDropdown?.SetValueWithoutNotify(MintedOptions[mintedIndex].label);

            var isTtrs = string.Equals(symbol, "TTRS", StringComparison.OrdinalIgnoreCase);

            typeFilterDropdown.style.display = isTtrs ? DisplayStyle.Flex : DisplayStyle.None;
            rarityFilterDropdown.style.display = isTtrs ? DisplayStyle.Flex : DisplayStyle.None;

            if (isTtrs)
            {
                var typeOptions = Enum.GetValues(typeof(ttrsNftType)).Cast<ttrsNftType>().Select(x => $"Type: {x}").ToList();
                var typeIndex = (int)state.FilterType;
                typeIndex = Mathf.Clamp(typeIndex, 0, typeOptions.Count - 1);
                typeFilterDropdown.choices = typeOptions;
                typeFilterDropdown.SetValueWithoutNotify(typeOptions[typeIndex]);

                var rarityOptions = Enum.GetValues(typeof(ttrsNftRarity)).Cast<ttrsNftRarity>().Select(x => $"Rarity: {x}").ToList();
                var rarityIndex = (int)state.FilterRarity;
                rarityIndex = Mathf.Clamp(rarityIndex, 0, rarityOptions.Count - 1);
                rarityFilterDropdown.choices = rarityOptions;
                rarityFilterDropdown.SetValueWithoutNotify(rarityOptions[rarityIndex]);

            }
            else
            {
                typeFilterDropdown.choices = new List<string> { "Type: All" };
                typeFilterDropdown.SetValueWithoutNotify("Type: All");
                rarityFilterDropdown.choices = new List<string> { "Rarity: All" };
                rarityFilterDropdown.SetValueWithoutNotify("Rarity: All");
            }

            contractInfoButton.text = isTtrs ? "Online inventory" : "Contract info";
            sortModeDropdown.style.display = DisplayStyle.Flex;

            var sortChoices = BuildSortChoices(symbol, isTtrs);
            var selectedSort = GetCurrentSortIndex(symbol, sortChoices.Count);
            sortModeDropdown.choices = sortChoices;
            sortModeDropdown.SetValueWithoutNotify(sortChoices.Count > 0 ? sortChoices[selectedSort] : string.Empty);

            var direction = (WalletSortDirection)AccountManager.Instance?.Settings?.nftSortDirection;
            sortDirectionButton.text = direction == WalletSortDirection.Descending ? "Desc" : "Asc";

            ApplyFiltersLayout();
        }

        private List<string> BuildSortChoices(string symbol, bool isTtrs)
        {
            if (isTtrs)
            {
                return Enum.GetValues(typeof(TtrsNftSortMode)).Cast<TtrsNftSortMode>().Select(x => FormatSortOption(x)).ToList();
            }

            return Enum.GetValues(typeof(NftSortMode)).Cast<NftSortMode>().Select(x => FormatSortOption(x)).ToList();
        }

        private int GetCurrentSortIndex(string symbol, int maxChoices)
        {
            var settings = AccountManager.Instance?.Settings;
            if (settings == null || maxChoices == 0)
            {
                return 0;
            }

            var isTtrs = string.Equals(symbol, "TTRS", StringComparison.OrdinalIgnoreCase);
            var idx = isTtrs ? settings.ttrsNftSortMode : settings.nftSortMode;
            return Mathf.Clamp(idx, 0, Math.Max(0, maxChoices - 1));
        }

        private string BuildSummaryLine(int total, int selected)
        {
            return WalletUiCommon.IsCompactWidth(root, CompactNftWidth)
                ? $"{total} NFTs / {selected} selected"
                : $"{total} item(s) • {selected} selected";
        }

        private void ToggleFiltersVisibility()
        {
            filtersExpanded = !filtersExpanded;
            filtersExpandedUserOverride = true;
            UpdateFiltersVisibility();
        }

        private bool ShouldHideFilters()
        {
            // Hide the filters section on device builds to free space for the list.
            var platform = Application.platform;
            return platform == RuntimePlatform.Android || platform == RuntimePlatform.IPhonePlayer;
        }

        private void UpdateFiltersVisibility(bool fromLayout = false)
        {
            if (filtersPanel == null)
            {
                return;
            }

            if (ShouldHideFilters())
            {
                filtersPanel.style.display = DisplayStyle.None;
                return;
            }

            filtersPanel.style.display = DisplayStyle.Flex;

            var compact = WalletUiCommon.IsCompactWidth(filtersPanel ?? root, CompactNftWidth);
            if (!filtersExpandedUserOverride)
            {
                filtersExpanded = !compact;
            }

            var display = filtersExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            if (filtersRow != null)
            {
                filtersRow.style.display = display;
            }

            if (filtersButtonRow != null)
            {
                filtersButtonRow.style.display = display;
            }

            if (toggleFiltersButton != null)
            {
                toggleFiltersButton.text = filtersExpanded ? "Hide filters" : "Show filters";
            }
        }

        private void ApplyFiltersLayout()
        {
            if (filtersButtonRow == null || filtersSelectionGroup == null || filtersContractGroup == null)
            {
                return;
            }

            var compact = WalletUiCommon.IsCompactWidth(filtersPanel ?? root, CompactNftWidth);

            if (filtersRow != null)
            {
                filtersRow.style.flexDirection = compact ? FlexDirection.Column : FlexDirection.Row;
                filtersRow.style.flexWrap = compact ? Wrap.NoWrap : Wrap.Wrap;
            }

            ApplyFilterFieldLayout(compact);

            filtersButtonRow.style.flexDirection = compact ? FlexDirection.Column : FlexDirection.Row;
            filtersButtonRow.style.alignItems = compact ? Align.Stretch : Align.Center;
            filtersButtonRow.style.justifyContent = compact ? Justify.FlexStart : Justify.SpaceBetween;

            filtersSelectionGroup.style.width = compact ? new Length(100, LengthUnit.Percent) : StyleKeyword.Auto;
            filtersSelectionGroup.style.justifyContent = Justify.FlexStart;

            filtersContractGroup.style.width = StyleKeyword.Auto;
            filtersContractGroup.style.justifyContent = Justify.FlexStart;
            filtersContractGroup.style.marginTop = 0;
            filtersContractGroup.style.flexGrow = 0;
            if (filtersButtonSpacer != null)
            {
                filtersButtonSpacer.style.display = compact ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (contractInfoButton != null)
            {
                if (compact)
                {
                    // On narrow layouts keep the button together with selection controls to avoid clipping.
                    if (contractInfoButton.parent != filtersSelectionGroup)
                    {
                        contractInfoButton.RemoveFromHierarchy();
                        filtersSelectionGroup.Add(contractInfoButton);
                    }
                    contractInfoButton.style.width = new Length(100, LengthUnit.Percent);
                    contractInfoButton.style.marginLeft = 0;
                }
                else
                {
                    if (contractInfoButton.parent != filtersContractGroup)
                    {
                        contractInfoButton.RemoveFromHierarchy();
                        filtersContractGroup.Add(contractInfoButton);
                    }
                    contractInfoButton.style.width = StyleKeyword.Auto;
                    contractInfoButton.style.marginLeft = 8;
                }
            }

            UpdateFiltersVisibility(true);
        }

        private void ApplyFilterFieldLayout(bool compact)
        {
            var fields = new VisualElement[]
            {
                nameFilterField,
                mintedFilterDropdown,
                typeFilterDropdown,
                rarityFilterDropdown,
                sortModeDropdown,
                sortDirectionButton
            };

            foreach (var field in fields)
            {
                if (field == null)
                {
                    continue;
                }

                field.style.width = compact ? new Length(100, LengthUnit.Percent) : StyleKeyword.Auto;
                field.style.marginRight = compact ? 0 : 8;
                field.style.alignSelf = compact ? Align.Stretch : Align.FlexStart;
            }
        }

        private void RenderList(string symbol, WalletNftViewSnapshot snapshot, AccountManager accountManager)
        {
            listView.Clear();

            if (snapshot.PageIds == null || snapshot.PageIds.Count == 0)
            {
                var label = new Label($"No {symbol} NFTs found for this account.")
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        fontSize = 14,
                        unityTextAlign = TextAnchor.MiddleCenter,
                        marginTop = 12,
                        marginBottom = 12
                    }
                };
                WalletUiCommon.ApplyDefaultFont(label);
                listView.Add(label);
                return;
            }

            var tokensById = snapshot.FilteredTokens?.ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase)
                               ?? new Dictionary<string, TokenDataResult>(StringComparer.OrdinalIgnoreCase);
            var platform = accountManager.CurrentPlatform;

            foreach (var id in snapshot.PageIds)
            {
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                tokensById.TryGetValue(id, out var token);
                var metadataAvailable = nftSource.TryGetMetadata(symbol, id, out var metadata);
                var meta = metadataAvailable ? metadata : NftMetadata.Empty;
                var row = BuildNftRow(symbol, token, meta, platform);
                listView.Add(row);
            }

            listView.schedule.Execute(() =>
            {
                if (listView.verticalScroller != null)
                {
                    listView.verticalScroller.value = context.ViewState.NftScrollY;
                }
            });
        }

        private VisualElement BuildNftRow(string symbol, TokenDataResult token, NftMetadata meta, PlatformKind platform)
        {
            var tokenId = token?.Id ?? string.Empty;
            var isSelected = !string.IsNullOrEmpty(tokenId) && nftPresenter.IsSelected(tokenId);

            var card = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    flexWrap = Wrap.Wrap,
                    paddingLeft = 12,
                    paddingRight = 12,
                    paddingTop = 10,
                    paddingBottom = 10,
                    marginBottom = 10
                }
            };
            WalletUiCommon.ApplyDefaultFont(card);
            WalletUiCommon.ApplyCardStyle(card, WalletUiTheme.GetCardGradientTexture(), WalletUiTheme.RadiusMedium);

            var toggle = WalletUiFormFactory.CreateToggle(string.Empty, isSelected, newValue =>
            {
                if (string.IsNullOrEmpty(tokenId))
                {
                    return;
                }

                nftPresenter.ToggleSelection(tokenId);
                context.ViewState.MarkNftDirty(currentSymbol);
                RefreshView();
            });
            toggle.style.marginLeft = 0;
            toggle.style.marginRight = 8;
            card.Add(toggle);

            var image = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
                style =
                {
                    width = 64,
                    height = 64,
                    marginRight = 12
                }
            };
            SetNftImageAsync(image, symbol, token);
            card.Add(image);

            var textBlock = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1,
                    alignItems = Align.Stretch
                }
            };
            WalletUiCommon.ApplyDefaultFont(textBlock);

            var title = new Label(BuildNftTitle(tokenId, meta))
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 16,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            WalletUiCommon.ApplyDefaultFont(title);
            textBlock.Add(title);

            var detailLine = BuildDetailLine(token, meta);
            if (!string.IsNullOrWhiteSpace(detailLine))
            {
                var detail = new Label(detailLine)
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        fontSize = 13,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        marginTop = 4
                    }
                };
                WalletUiCommon.ApplyDefaultFont(detail);
                textBlock.Add(detail);
            }

            var infusionLine = BuildInfusionDescription(token, platform);
            if (!string.IsNullOrWhiteSpace(infusionLine))
            {
                var infusionLabel = new Label(infusionLine)
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        fontSize = 12,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        marginTop = 2
                    }
                };
                WalletUiCommon.ApplyDefaultFont(infusionLabel);
                textBlock.Add(infusionLabel);
            }

            card.Add(textBlock);

            var actions = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    alignItems = Align.FlexEnd,
                    justifyContent = Justify.Center,
                    marginLeft = 10,
                    flexShrink = 0
                }
            };
            WalletUiCommon.ApplyDefaultFont(actions);

            var viewButton = WalletUiCommon.CreateOutlineButton("View", () => OpenNftDetails(symbol, tokenId, false, true), 14, 30);
            viewButton.style.minWidth = 90;
            actions.Add(viewButton);

            card.Add(actions);
            ApplyNftRowLayout(card, actions, viewButton);
            card.RegisterCallback<GeometryChangedEvent>(_ => ApplyNftRowLayout(card, actions, viewButton));
            return card;
        }

        private void ApplyNftRowLayout(VisualElement card, VisualElement actions, Button viewButton)
        {
            var compact = WalletUiCommon.IsCompactWidth(card, CompactNftWidth);

            if (actions != null)
            {
                actions.style.alignItems = compact ? Align.Stretch : Align.FlexEnd;
                actions.style.width = compact ? new Length(100, LengthUnit.Percent) : StyleKeyword.Auto;
                actions.style.marginLeft = compact ? 0 : 10;
                actions.style.marginTop = compact ? 8 : 0;
            }

            if (viewButton != null)
            {
                viewButton.style.alignSelf = compact ? Align.Stretch : Align.Center;
                viewButton.style.width = compact ? new Length(100, LengthUnit.Percent) : StyleKeyword.Auto;
            }
        }

        private string BuildNftTitle(string tokenId, NftMetadata meta)
        {
            if (!string.IsNullOrWhiteSpace(meta.Name))
            {
                return NormalizeNftName(meta.Name);
            }

            return $"#{FormatId(tokenId, 6)}";
        }

        private string BuildDetailLine(TokenDataResult token, NftMetadata meta)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(token?.Mint) && !string.Equals(token.Mint, "0", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"Mint #{token.Mint}");
            }

            if (meta.MintDate != DateTime.MinValue)
            {
                parts.Add(meta.MintDate.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(meta.Type))
            {
                parts.Add(meta.Type);
            }

            if (meta.Rarity > 0)
            {
                parts.Add($"Rarity {meta.Rarity}");
            }

            return parts.Count == 0 ? string.Empty : string.Join(" • ", parts);
        }

        private string BuildInfusionDescription(TokenDataResult token, PlatformKind platform)
        {
            if (token?.Infusion == null || token.Infusion.Length == 0)
            {
                return string.Empty;
            }

            var fungibleParts = new List<string>();
            var nftParts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < token.Infusion.Length; i++)
            {
                var symbol = token.Infusion[i].Key;
                var amountOrId = token.Infusion[i].Value;

                if (Tokens.GetToken(symbol, platform, out var tok))
                {
                    if (tok.IsFungible())
                    {
                        if (!BigInteger.TryParse(amountOrId, out var rawAmount))
                        {
                            rawAmount = BigInteger.Zero;
                        }

                        fungibleParts.Add($"{WalletAmountFormatter.Format(rawAmount, tok.Decimals)} {symbol}");
                    }
                    else
                    {
                        if (nftParts.ContainsKey(symbol))
                        {
                            nftParts[symbol] += 1;
                        }
                        else
                        {
                            nftParts[symbol] = 1;
                        }
                    }
                }
            }

            var parts = new List<string>();
            parts.AddRange(fungibleParts);
            parts.AddRange(nftParts.Select(x => $"{x.Value} {x.Key} NFT{(x.Value > 1 ? "s" : string.Empty)}"));
            return parts.Count == 0 ? string.Empty : $"Infusions: {string.Join(", ", parts)}";
        }

        private void SetNftImageAsync(Image target, string symbol, TokenDataResult token)
        {
            if (target == null)
            {
                return;
            }

            var media = NftMediaResolver.ResolvePreview(symbol, token);
            var placeholder = GetNftMediaPlaceholder(media.Kind);

            if (token == null || media.Kind == NftMediaKind.None)
            {
                target.image = placeholder;
                return;
            }

            // Videos and audio stay external in UITK for now.
            // We show a deterministic placeholder instead of attempting to decode arbitrary media content in-process.
            if (media.Kind != NftMediaKind.Image)
            {
                target.image = placeholder;
                return;
            }

            if (media.IsInlineImage)
            {
                if (NftImages.TryCacheInlineImage(symbol, media.Source, token.Id, out var inlineTexture))
                {
                    target.image = inlineTexture ?? placeholder;
                }
                else
                {
                    target.image = placeholder;
                }
                return;
            }

            var cached = NftImages.GetImage(media.Source);
            if (!string.IsNullOrEmpty(cached.Url))
            {
                target.image = cached.Texture ?? placeholder;
                return;
            }

            target.image = placeholder;
            RunSafeAsync(async () =>
            {
                await NftImages.DownloadImageAsync(symbol, media.Source, token.Id, CancellationToken.None);
                var loaded = NftImages.GetImage(media.Source);
                if (!string.IsNullOrEmpty(loaded.Url) && target != null)
                {
                    target.image = loaded.Texture ?? placeholder;
                }
            }).Forget(ex => Log.WriteWarning($"{LogPrefix}Failed to load NFT image: {ex}"));
        }

        private Texture GetNftMediaPlaceholder(NftMediaKind kind)
        {
            switch (kind)
            {
                case NftMediaKind.Video:
                    return ResourceManager.Instance?.NftVideoPlaceholder;
                case NftMediaKind.Audio:
                    return ResourceManager.Instance?.NftAudioPlaceholder;
                default:
                    return ResourceManager.Instance?.NftPhotoPlaceholder;
            }
        }

        private void OpenNftDetails(string symbol, string tokenId, bool locked, bool resetTrail)
        {
            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(tokenId))
            {
                return;
            }

            if (resetTrail)
            {
                context.ViewState.ClearNftInspectTrail();
            }

            var current = context.ViewState.PeekNftInspect();
            if (!current.HasValue || !string.Equals(current.Value.Symbol, symbol, StringComparison.OrdinalIgnoreCase) || !string.Equals(current.Value.TokenId, tokenId, StringComparison.OrdinalIgnoreCase))
            {
                context.ViewState.PushNftInspect(new WalletNftInspectEntry(symbol, tokenId, locked));
            }

            RefreshView();
        }

        private string BuildDetailSubtitle(string symbol, string tokenId, TokenDataResult token, DateTime mintDate)
        {
            var parts = new List<string> { $"{symbol} #{FormatId(tokenId, 6)}" };
            if (!string.IsNullOrWhiteSpace(token?.ChainName))
            {
                parts.Add(token.ChainName);
            }

            if (mintDate != DateTime.MinValue)
            {
                parts.Add(mintDate.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
            }

            return string.Join(" • ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private void UpdateActions(string symbol, AccountManager accountManager)
        {
            var selectionCount = nftPresenter.State.SelectedCount;
            var hasSelection = selectionCount > 0;
            var isDebugView = context.ViewState?.IsDebugNftActive ?? false;
            if (isDebugView)
            {
                // Debug view is read-only: hide Send/Burn to avoid accidental actions.
                SetActionButtonState(sendButton, false);
                sendButton.style.display = DisplayStyle.None;
                SetActionButtonState(burnButton, false);
                burnButton.style.display = DisplayStyle.None;
                SetActionButtonState(clearSelectionButton, hasSelection);
                SetActionButtonState(selectAllButton, true);
                SetActionButtonState(invertSelectionButton, true);
                return;
            }

            var platform = accountManager.CurrentPlatform;
            var settings = accountManager.Settings;
            var devMode = settings?.devMode ?? false;
            var nexusKind = settings?.nexusKind ?? NexusKind.Main_Net;

            var showSend = false;
            if (platform == PlatformKind.Phantasma && Tokens.GetToken(symbol, platform, out var token) && !string.IsNullOrEmpty(token.Flags) && token.IsTransferable())
            {
                var allowTransfer = token.IsFungible()
                    || devMode
                    || nexusKind == NexusKind.Test_Net
                    || nexusKind == NexusKind.Dev_Net; // NFT transfers enabled on test/dev nets or dev mode; fungible transfers always allowed.
                showSend = allowTransfer;
            }

            SetActionButtonState(sendButton, hasSelection && showSend);
            sendButton.style.display = showSend ? DisplayStyle.Flex : DisplayStyle.None;

            // NFT burn is available on Phantasma; enable only when selection is present.
            var canBurn = platform == PlatformKind.Phantasma && hasSelection;
            SetActionButtonState(burnButton, canBurn);
            burnButton.style.display = platform == PlatformKind.Phantasma ? DisplayStyle.Flex : DisplayStyle.None;

            SetActionButtonState(clearSelectionButton, hasSelection);
            SetActionButtonState(selectAllButton, true);
            SetActionButtonState(invertSelectionButton, true);
        }

        private void OnRefreshClicked()
        {
            var am = AccountManager.Instance;
            if (am == null)
            {
                SetStatus("Account is not ready.");
                return;
            }

            var symbol = string.IsNullOrWhiteSpace(currentSymbol) ? context.ViewState.TokenDashboardSymbol : currentSymbol;
            if (string.IsNullOrWhiteSpace(symbol))
            {
                SetStatus("Pick an NFT collection from Balances to open its dashboard.");
                return;
            }

            // NFT header summary (supply/flags) comes from token metadata, not balances.
            // Refresh token metadata on manual refresh so the header reflects updated supply.
            RequestTokenMetadataRefresh(symbol);

            // Manual refresh is two-step:
            // 1) Refresh NFTs immediately (uses current balance ids).
            // 2) Refresh balances to fetch new ids, then queue a follow-up NFT refresh in OnBalancesUpdated.
            StartRefreshSequence(symbol, includeWarmup: false);

            var isDebugView = context.ViewState?.IsDebugNftActive ?? false;
            if (isDebugView)
            {
                return;
            }

            if (!am.HasSelection)
            {
                return;
            }

            if (am.CurrentAccount.passwordProtected && string.IsNullOrEmpty(am.CurrentPasswordHash))
            {
                return;
            }

            pendingBalanceRefresh = true;
            // Snapshot symbol/platform so a later balance update refreshes the intended collection.
            pendingBalanceRefreshSymbol = symbol;
            pendingBalanceRefreshPlatform = am.CurrentPlatform != PlatformKind.None ? am.CurrentPlatform : PlatformKind.Phantasma;

            // Manual refresh should also update balances so new NFT ids (recent mints) are pulled into the list.
            am.RefreshBalances(true, pendingBalanceRefreshPlatform);
        }

        private void RequestTokenMetadataRefresh(string symbol)
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                return;
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                return; // Token metadata is sourced from Phantasma; other platforms do not use this list.
            }
            accountManager.RequestTokensReload();
        }

        private Task SendAsync()
        {
            return SendAsync(null, null);
        }

        private async Task SendAsync(string symbolOverride, IReadOnlyCollection<string> customIds)
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                SetStatus("Account is not ready.");
                return;
            }

            var settings = accountManager.Settings;
            var symbol = string.IsNullOrWhiteSpace(symbolOverride) ? (string.IsNullOrWhiteSpace(currentSymbol) ? context.ViewState.TokenDashboardSymbol : currentSymbol) : symbolOverride;
            var selectedIds = customIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? nftPresenter.SelectionSnapshot().ToList();
            if (string.IsNullOrWhiteSpace(symbol) || selectedIds.Count == 0)
            {
                SetStatus("No NFTs selected.");
                return;
            }

            Tokens.GetToken(symbol, accountManager.CurrentPlatform, out var transferToken);
            if (transferToken == null || string.IsNullOrEmpty(transferToken.Flags))
            {
                await ShowErrorAsync($"Operations with token {symbol} are not supported yet in this version.");
                return;
            }

            if (!transferToken.IsTransferable())
            {
                await ShowErrorAsync($"Transfers of {symbol} tokens are not allowed.");
                return;
            }

            var nexusKind = settings?.nexusKind ?? NexusKind.Main_Net;
            var allowTransferAction = transferToken.IsFungible()
                || (settings?.devMode ?? false)
                || nexusKind == NexusKind.Test_Net
                || nexusKind == NexusKind.Dev_Net; // NFT transfers enabled on test/dev nets or dev mode; fungible transfers always allowed.

            if (!allowTransferAction)
            {
                await ShowErrorAsync($"Transfers of {symbol} tokens are not available on this network.");
                return;
            }

            var destination = await PromptDestinationAsync(symbol);
            if (string.IsNullOrWhiteSpace(destination))
            {
                return;
            }

            var draft = nftTransferService.BuildNftTransferDraft(symbol, destination, selectedIds);
            if (!draft.Success)
            {
                await ShowErrorAsync(draft.Error);
                return;
            }

            var sendResult = await transactionOrchestrator.SendTransactionDraftAsync(draft.Draft, true);
            var transferMessage = WalletUiTransactionResultHelper.CombineWithPendingNotice($"You transferred {draft.Amount} {symbol} NFT(s)!");
            TxResultMessage(sendResult.hash, sendResult.txResult, sendResult.error, transferMessage);

            if (!string.IsNullOrWhiteSpace(sendResult.error) || sendResult.hash == Hash.Null)
            {
                return;
            }

            context.ViewState.NftScrollY = 0f;
            nftPresenter.ClearSelection();
            context.ViewState.MarkNftDirty(symbol);
            if (customIds != null)
            {
                context.ViewState.ClearNftInspectTrail();
            }
            RefreshView();
        }

        private Task BurnAsync()
        {
            return BurnAsync(null, null);
        }

        private async Task BurnAsync(string symbolOverride, IReadOnlyCollection<string> customIds)
        {
            var settings = AccountManager.Instance?.Settings;
            if (settings == null)
            {
                SetStatus("Account is not ready.");
                return;
            }

            var symbol = string.IsNullOrWhiteSpace(symbolOverride) ? (string.IsNullOrWhiteSpace(currentSymbol) ? context.ViewState.TokenDashboardSymbol : currentSymbol) : symbolOverride;
            var selectedIds = customIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? nftPresenter.SelectionSnapshot().ToList();
            if (string.IsNullOrWhiteSpace(symbol) || selectedIds.Count == 0)
            {
                SetStatus("No NFTs selected.");
                return;
            }

            var prep = burnService.PrepareNftBurn(symbol, selectedIds);
            if (!prep.Success)
            {
                await ShowErrorAsync(prep.Error);
                return;
            }

            var confirm = await WalletUiModalHelper.ShowConfirmAsync(modalHost, "Burn NFTs", prep.Message, "Burn", "Cancel");
            if (confirm != PromptResult.Success)
            {
                return;
            }

            var sendResult = await transactionOrchestrator.SendTransactionDraftAsync(prep.Data, true);
            TxResultMessage(sendResult.hash, sendResult.txResult, sendResult.error, $"You burned {selectedIds.Count} NFTs!");

            if (!string.IsNullOrWhiteSpace(sendResult.error) || sendResult.hash == Hash.Null)
            {
                return;
            }

            context.ViewState.NftScrollY = 0f;
            nftPresenter.ClearSelection();
            context.ViewState.MarkNftDirty(symbol);
            if (customIds != null)
            {
                context.ViewState.ClearNftInspectTrail();
            }
            RefreshView();
        }

        private async Task SelectFilteredAsync()
        {
            var symbol = string.IsNullOrWhiteSpace(currentSymbol) ? context.ViewState.TokenDashboardSymbol : currentSymbol;
            var snapshot = context.ViewState.GetNftSnapshot(symbol, s => nftPresenter.BuildSnapshot(s));

            if (snapshot.FilteredTokens != null && snapshot.FilteredTokens.Count > 0)
            {
                nftPresenter.ClearSelection();
                nftPresenter.Select(snapshot.FilteredTokens.Select(x => x.Id));
            }
            else
            {
                nftPresenter.ClearSelection();
                nftPresenter.Select(nftSource.GetNfts(symbol)?.Select(x => x.Id));
            }

            context.ViewState.MarkNftDirty(symbol);
            RefreshView();
            await Task.CompletedTask;
        }

        private async Task InvertSelectionAsync()
        {
            var symbol = string.IsNullOrWhiteSpace(currentSymbol) ? context.ViewState.TokenDashboardSymbol : currentSymbol;
            var snapshot = context.ViewState.GetNftSnapshot(symbol, s => nftPresenter.BuildSnapshot(s));

            if (snapshot.FilteredTokens != null && snapshot.FilteredTokens.Count > 0)
            {
                nftPresenter.InvertSelection(snapshot.FilteredTokens.Select(x => x.Id));
            }
            else
            {
                nftPresenter.InvertSelection(nftSource.GetNfts(symbol)?.Select(x => x.Id));
            }

            context.ViewState.MarkNftDirty(symbol);
            RefreshView();
            await Task.CompletedTask;
        }

        private async Task ClearSelectionAsync()
        {
            nftPresenter.ClearSelection();
            context.ViewState.MarkNftDirty(currentSymbol);
            RefreshView();
            await Task.CompletedTask;
        }

        private void OnFiltersChanged(string name, ttrsNftType? type, ttrsNftRarity? rarity, nftMinted? minted)
        {
            var state = nftPresenter.State;
            var filterName = name ?? state.FilterName;
            var filterType = type ?? state.FilterType;
            var filterRarity = rarity ?? state.FilterRarity;
            var filterMinted = minted ?? state.FilterMinted;

            if (nftPresenter.UpdateFilters(filterName, filterType, filterRarity, filterMinted))
            {
                context.ViewState.NftScrollY = 0f;
                nftPresenter.ClearSelection();
                context.ViewState.MarkNftDirty(currentSymbol);
                RefreshView();
            }
        }

        private void OnSortModeChanged(int index)
        {
            var settings = AccountManager.Instance?.Settings;
            if (settings == null)
            {
                return;
            }

            var symbol = string.IsNullOrWhiteSpace(currentSymbol) ? context.ViewState.TokenDashboardSymbol : currentSymbol;
            var isTtrs = string.Equals(symbol, "TTRS", StringComparison.OrdinalIgnoreCase);

            if (isTtrs)
            {
                settings.ttrsNftSortMode = Mathf.Clamp(index, 0, Enum.GetValues(typeof(TtrsNftSortMode)).Length - 1);
            }
            else
            {
                settings.nftSortMode = Mathf.Clamp(index, 0, Enum.GetValues(typeof(NftSortMode)).Length - 1);
            }

            context.ViewState.MarkNftDirty(symbol);
            RefreshView();
        }

        private void ToggleSortDirection()
        {
            var settings = AccountManager.Instance?.Settings;
            if (settings == null)
            {
                return;
            }

            var direction = (WalletSortDirection)settings.nftSortDirection;
            settings.nftSortDirection = direction == WalletSortDirection.Ascending
                ? (int)WalletSortDirection.Descending
                : (int)WalletSortDirection.Ascending;

            context.ViewState.MarkNftDirty(currentSymbol);
            RefreshView();
        }

        private void OnPageChanged(PageChange change)
        {
            switch (change)
            {
                case PageChange.First:
                    nftPresenter.State.GoToFirstPage();
                    break;
                case PageChange.Previous:
                    nftPresenter.State.GoToPreviousPage();
                    break;
                case PageChange.Next:
                    nftPresenter.State.GoToNextPage();
                    break;
                case PageChange.Last:
                    nftPresenter.State.GoToLastPage();
                    break;
            }

            context.ViewState.MarkNftDirty(currentSymbol);
            RefreshView();
        }

        private async Task<string> PromptDestinationAsync(string symbol)
        {
            var accounts = AccountManager.Instance?.Accounts;
            var (destResult, destInput) = await WalletUiModalHelper.ShowAddressInputDialogAsync(
                modalHost,
                $"Send {symbol} NFTs",
                "Enter destination address or pick one of your wallets.",
                accounts,
                confirmLabel: "Next",
                cancelLabel: "Cancel",
                initialValue: string.Empty);

            if (destResult != PromptResult.Success)
            {
                return string.Empty;
            }

            var destination = destInput?.Trim();
            if (string.IsNullOrWhiteSpace(destination))
            {
                return string.Empty;
            }

            if (Address.IsValidAddress(destination))
            {
                return destination;
            }

            if (ValidationUtils.IsValidIdentifier(destination))
            {
                var resolved = await ResolveAccountNameAsync(destination);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            await ShowErrorAsync("Invalid destination address.");
            return string.Empty;
        }

        private async Task<string> ResolveAccountNameAsync(string name)
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                await ShowErrorAsync("Account is not ready.");
                return string.Empty;
            }

            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            accountManager.ValidateAccountName(name, resolved =>
            {
                tcs.TrySetResult(resolved);
            });
            var resolvedAddress = await tcs.Task;
            if (string.IsNullOrWhiteSpace(resolvedAddress))
            {
                await ShowErrorAsync("No account with such name exists.");
                return string.Empty;
            }

            return resolvedAddress;
        }

        private void OpenContractInfo()
        {
            var accountManager = AccountManager.Instance;
            var symbol = string.IsNullOrWhiteSpace(currentSymbol) ? context.ViewState.TokenDashboardSymbol : currentSymbol;
            if (accountManager == null || string.IsNullOrWhiteSpace(symbol))
            {
                return;
            }

            if (string.Equals(symbol, "TTRS", StringComparison.OrdinalIgnoreCase))
            {
                Application.OpenURL("https://www.22series.com/inventory?#" + accountManager.GetAddress(accountManager.CurrentIndex, accountManager.CurrentPlatform));
                return;
            }

            var url = accountManager.GetPhantasmaContractURL(symbol);
            if (!string.IsNullOrWhiteSpace(url))
            {
                Application.OpenURL(url);
            }
        }

        private void OpenNftExplorer(string symbol, string tokenId)
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null || string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(tokenId))
            {
                return;
            }

            var explorerUrl = accountManager.GetPhantasmaNftURL(symbol, tokenId);
            if (!string.IsNullOrEmpty(explorerUrl))
            {
                Application.OpenURL(explorerUrl);
            }
        }

        private Task<PromptResult> ShowSendProgressAsync(string description, int txCount)
        {
            return transactionDialogs.ShowSendProgressAsync(description, txCount);
        }

        private Task<(Hash hash, TransactionResult txResult, string error)> StartConfirmationAsync(Hash hash, bool refreshBalanceAfterConfirmation)
        {
            return transactionDialogs.StartConfirmationAsync(hash, refreshBalanceAfterConfirmation);
        }

        private void TxResultMessage(Hash hash, TransactionResult txResult, string error, string successMessage, string failureMessage = null)
        {
            WalletUiTransactionResultHelper.ShowAsync(modalHost, () => AccountManager.Instance, hash, txResult, error, successMessage, failureMessage)
                .Forget(ex => Log.WriteWarning($"{LogPrefix}Failed to show transaction result: {ex}"));

            if (!string.IsNullOrWhiteSpace(error))
            {
                SetStatus(error);
                return;
            }

            if (hash != Hash.Null)
            {
                SetStatus(successMessage ?? $"Transaction sent: {hash}");
            }
        }

        private void HandleBackNavigation()
        {
            // Walk back through the inspected NFT trail before leaving the asset dashboard entirely.
            if (context.ViewState.HasNftInspect && context.ViewState.TryPopNftInspect(out _))
            {
                if (context.ViewState.HasNftInspect)
                {
                    RefreshView();
                    return;
                }

                ShowListMode();
                RefreshView();
                return;
            }

            ExitDebugView();
            onShowBalances?.Invoke();
        }

        private void ExitDebugView()
        {
            if (context.ViewState?.IsDebugNftActive ?? false)
            {
                // Leaving the NFT screen should reset debug-only behavior.
                context.ViewState.IsDebugNftActive = false;
            }
        }

        private void ShowListMode()
        {
            if (listContainer != null)
            {
                listContainer.style.display = DisplayStyle.Flex;
            }

            if (detailContainer != null)
            {
                detailContainer.style.display = DisplayStyle.None;
            }
        }

        private void ShowDetailMode()
        {
            if (listContainer != null)
            {
                listContainer.style.display = DisplayStyle.None;
            }

            if (detailContainer != null)
            {
                detailContainer.style.display = DisplayStyle.Flex;
            }
        }

        private void ClearUi()
        {
            listView?.Clear();
            totalCountLabel.text = string.Empty;
            selectedCountLabel.text = string.Empty;
            totalCountLabel.style.display = DisplayStyle.None;
            selectedCountLabel.style.display = DisplayStyle.None;
            pageInfoLabel.text = "Page 1 / 1";
            filterHintLabel.text = string.Empty;
            UpdatePaginationButtons(null);
            tokenIcon.image = null;
            if (summaryLabel != null)
            {
                summaryLabel.text = BuildSummaryLine(0, 0);
            }
            if (detailTitleLabel != null)
            {
                detailTitleLabel.text = "NFT";
            }

            if (detailSubtitleLabel != null)
            {
                detailSubtitleLabel.text = string.Empty;
            }

            if (detailDescriptionLabel != null)
            {
                detailDescriptionLabel.text = string.Empty;
            }
            detailTagContainer?.Clear();
            detailPropertiesContainer?.Clear();
            detailFungibleList?.Clear();
            detailNftList?.Clear();
            if (detailLockLabel != null)
            {
                detailLockLabel.style.display = DisplayStyle.None;
            }
            if (detailFungibleContainer != null)
            {
                detailFungibleContainer.style.display = DisplayStyle.None;
            }
            if (detailNftContainer != null)
            {
                detailNftContainer.style.display = DisplayStyle.None;
            }
            if (detailImage != null)
            {
                detailImage.image = ResourceManager.Instance?.NftPhotoPlaceholder;
            }
            ShowListMode();
        }

        private void SetStatus(string text)
        {
            WalletUiCommon.UpdateStatusLabel(statusLabel, text);
        }

        private void UpdateRefreshingState(bool refreshing)
        {
            isRefreshing = refreshing;
            refreshButton?.SetEnabled(!refreshing);

            if (refreshing)
            {
                if (statusLabel != null && string.IsNullOrEmpty(statusLabel.text))
                {
                    SetStatus(RefreshStatusMessage);
                }
            }
            else if (statusLabel != null && string.Equals(statusLabel.text, RefreshStatusMessage, StringComparison.Ordinal))
            {
                SetStatus(string.Empty);
            }
        }

        private void UpdateNavSelection()
        {
            WalletUiCommon.SetNavState(navBalances, true);
            WalletUiCommon.SetNavState(navHistory, false);
            WalletUiCommon.SetNavState(navAccount, false);
            WalletUiCommon.SetNavState(navExit, false);
        }

        private string FormatId(string id, int keep)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length <= keep * 2)
            {
                return id ?? string.Empty;
            }

            return $"{id.Substring(0, keep)}...{id.Substring(id.Length - keep)}";
        }

        private string NormalizeNftName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var trimmed = name.Trim();
            if (IsLikelyIdentifier(trimmed))
            {
                return WalletTextFormatter.AbbreviateMiddle(trimmed, 6, 6);
            }

            return AbbreviateLongWords(trimmed, WalletUiCommon.IsCompactWidth(root, CompactNftWidth));
        }

        private bool IsLikelyIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 20)
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (char.IsWhiteSpace(ch))
                {
                    return false;
                }

                if (!char.IsLetterOrDigit(ch))
                {
                    return false;
                }
            }

            return true;
        }

        private string AbbreviateLongWords(string text, bool compactMode)
        {
            if (!compactMode || string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 32)
                {
                    parts[i] = WalletTextFormatter.AbbreviateMiddle(parts[i], 6, 6);
                }
            }

            return string.Join(" ", parts);
        }

        private string FormatSortOption(Enum value)
        {
            return value.ToString().Replace("_", " ").Replace("Number", "#");
        }

        private void SetActionButtonState(Button button, bool enabled)
        {
            if (button == null)
            {
                return;
            }

            WalletUiCommon.SetButtonEnabledVisual(button, enabled, WalletUiTheme.TextPrimary, WalletUiTheme.TextMuted);
        }

        private void SetActionsEnabled(bool enabled)
        {
            if (!enabled)
            {
                actionEnableCache.Clear();
                foreach (var btn in EnumerateActionButtons())
                {
                    actionEnableCache[btn] = btn.enabledSelf;
                    SetActionButtonState(btn, false);
                }

                return;
            }

            foreach (var btn in EnumerateActionButtons())
            {
                if (actionEnableCache.TryGetValue(btn, out var state))
                {
                    SetActionButtonState(btn, state);
                }
            }

            actionEnableCache.Clear();
        }

        private IEnumerable<Button> EnumerateActionButtons()
        {
            if (sendButton != null) yield return sendButton;
            if (burnButton != null) yield return burnButton;
            if (selectAllButton != null) yield return selectAllButton;
            if (invertSelectionButton != null) yield return invertSelectionButton;
            if (clearSelectionButton != null) yield return clearSelectionButton;
        }

        private async Task ShowErrorAsync(string message)
        {
            await WalletUiModalHelper.ShowErrorAsync(modalHost, "Error", message, null, null);
            SetStatus(message);
        }

        private async Task RunSafeAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{LogPrefix}Action failed: {e}");
                await ShowErrorAsync($"Error: {e.Message}");
            }
        }

        private enum PageChange
        {
            First,
            Previous,
            Next,
            Last
        }
    }
}
