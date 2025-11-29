using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Poltergeist;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Unity.Core.Logging;
using PhantasmaPhoenix.Core;
using Poltergeist.Wallet;
using Poltergeist.UiToolkit;

namespace Poltergeist.UiToolkit.Balances
{
    /// <summary>
    /// Token-centric dashboard for fungible assets (actions, stats, metadata).
    /// </summary>
    public sealed class WalletTokenDashboardView : IDisposable
    {
        private const string LogPrefix = "[UITK] ";

        private readonly WalletApplicationContext context;
        private readonly WalletBalancePresenter balancePresenter;
        private readonly WalletUiModalHost modalHost;
        private readonly WalletUiSignals uiSignals;
        private readonly WalletTransferService transferService;
        private readonly WalletStakeService stakeService;
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

        private VisualElement root;
        private ScrollView scrollView;
        private HeaderElements header;
        private SubHeaderElements subHeader;
        private Label subtitleLabel;
        private Label subtitleNetworkLabel;
        private Label summaryLabel;
        private Label statusLabel;
        private Label tokenTitleLabel;
        private Label tokenSubtitleLabel;
        private Label totalValueLabel;
        private Label availableValueLabel;
        private Label stakedValueLabel;
        private Label claimableValueLabel;
        private Label availableFiatLabel;
        private Label stakedFiatLabel;
        private Label claimableFiatLabel;
        private Label totalFiatLabel;
        private Label supplyValueLabel;
        private Label maxSupplyLabel;
        private Label burnedSupplyLabel;
        private Label decimalsLabel;
        private Label flagsLabel;
        private Image tokenIcon;
        private VisualElement heroCard;
        private VisualElement statsRow;
        private VisualElement actionsRow;
        private VisualElement advancedActions;
        private Button refreshButton;
        private Button sendButton;
        private Button stakeButton;
        private Button unstakeButton;
        private Button claimButton;
        private Button burnButton;
        private Button smRewardButton;
        private Button infoButton;
        private Button tokenExplorerButton;
        private Button tokenHoldersButton;
        private Button coingeckoButton;
        private Button cmcButton;
        private Button navBalances;
        private Button navHistory;
        private Button navAccount;
        private Button navExit;
        private readonly Dictionary<Button, bool> actionEnableCache = new Dictionary<Button, bool>();
        private string currentSymbol;

        public WalletTokenDashboardView(
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
            balancePresenter = context.BalancePresenter ?? throw new ArgumentNullException(nameof(context.BalancePresenter));
            this.modalHost = modalHost ?? throw new ArgumentNullException(nameof(modalHost));
            uiSignals = context.UiSignals ?? throw new ArgumentNullException(nameof(context.UiSignals));
            transferService = context.TransferService ?? throw new ArgumentNullException(nameof(context.TransferService));
            stakeService = context.StakeService ?? throw new ArgumentNullException(nameof(context.StakeService));
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
        }

        public void ShowToken(string symbol)
        {
            currentSymbol = symbol;
            context.ViewState.TokenDashboardSymbol = symbol;
            RefreshView();
        }

        public void OnAccountsReady()
        {
            RefreshView();
        }

        public void MarkAsActive()
        {
            UpdateNavSelection();
        }

        private void OnRefreshClicked()
        {
            if (!EnsureTokensReady())
            {
                return;
            }

            balancePresenter.Refresh(false);
            context.ViewState.MarkBalancesDirty();
            RefreshView();
        }

        private void Subscribe()
        {
            uiSignals.EnsureSubscribed();
            uiSignals.BalancesUpdated += OnBalancesUpdated;
            uiSignals.BalancesRefreshStarted += OnBalancesRefreshStarted;
            uiSignals.SettingsChanged += OnSettingsChanged;
        }

        private void Unsubscribe()
        {
            uiSignals.BalancesUpdated -= OnBalancesUpdated;
            uiSignals.BalancesRefreshStarted -= OnBalancesRefreshStarted;
            uiSignals.SettingsChanged -= OnSettingsChanged;
        }

        private void OnBalancesUpdated(PlatformKind platform)
        {
            context.ViewState.MarkBalancesDirty();
            RefreshView();
        }

        private void OnBalancesRefreshStarted(PlatformKind platform)
        {
            context.ViewState.MarkBalancesDirty();
            RefreshView();
        }

        private void OnSettingsChanged()
        {
            RefreshNetworkBadge();
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

            var content = WalletUiCommon.CreateScreenContent(paddingLeft: 8, paddingRight: 8);

            refreshButton = WalletUiCommon.CreateSecondaryButton("Refresh", OnRefreshClicked, 14, 32);
            refreshButton.style.minWidth = 120;

            var headerBlock = WalletUiCommon.BuildHeaderBlock("Asset", "Asset", string.Empty, refreshButton, showHeaderSubtitle: false, headerMarginBottom: 10f, subHeaderMarginTop: 10f, subHeaderMarginBottom: 8f);
            header = headerBlock.Header;
            subHeader = headerBlock.SubHeader;
            subtitleLabel = subHeader.SubtitleLabel;
            subtitleNetworkLabel = subHeader.NetworkLabel;
            summaryLabel = subHeader.LeftLabel;
            headerBlock.Root.style.flexShrink = 0;
            content.Add(headerBlock.Root);

            statusLabel = new Label(string.Empty)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 14,
                    color = WalletUiTheme.TextSecondary,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 10,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 4,
                    paddingBottom = 4,
                    backgroundColor = WalletUiTheme.PanelBackground,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall,
                    minHeight = 24
                }
            };
            WalletUiCommon.ApplyDefaultFont(statusLabel);
            statusLabel.style.display = DisplayStyle.None;
            content.Add(statusLabel);

            var listWrapper = WalletUiCommon.BuildScrollContainer(
                out scrollView,
                onScrollChanged: null,
                shouldBlockWheel: () => modalHost?.Overlay != null && modalHost.Overlay.style.display == DisplayStyle.Flex,
                paddingLeft: 4f,
                paddingRight: 4f,
                paddingTop: 6f,
                paddingBottom: 80f,
                marginTop: 4f,
                marginBottom: 10f,
                maxWidth: 1680f,
                alignSelf: Align.Center);

            var scrollContent = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    width = new Length(100, LengthUnit.Percent),
                    alignSelf = Align.Stretch
                }
            };
            WalletUiCommon.ApplyDefaultFont(scrollContent);

            heroCard = BuildHeroCard();
            scrollContent.Add(heroCard);

            statsRow = BuildStatsRow();
            scrollContent.Add(statsRow);

            actionsRow = BuildActionsRow();
            scrollContent.Add(actionsRow);

            var infoSection = BuildInfoSection();
            scrollContent.Add(infoSection);

            advancedActions = BuildAdvancedActionsRow();
            advancedActions.style.marginTop = 10;
            scrollContent.Add(advancedActions);

            scrollView.Add(scrollContent);
            listWrapper.style.flexGrow = 1;
            content.Add(listWrapper);

            var footer = WalletUiCommon.BuildWalletNavBar(out navBalances, out navHistory, out navAccount, out navExit, () => onShowBalances?.Invoke(), () => onShowHistory?.Invoke(), () => onShowAccount?.Invoke(), () => onShowBalances?.Invoke());
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
                    paddingTop = 16,
                    paddingBottom = 16,
                    marginBottom = 12,
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
            WalletUiCommon.ApplyDefaultFont(card);

            tokenIcon = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
                style =
                {
                    width = 64,
                    height = 64,
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
            tokenTitleLabel = new Label("Token")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 24,
                    color = WalletUiTheme.TextPrimary
                }
            };
            WalletUiCommon.ApplyDefaultFont(tokenTitleLabel);
            tokenSubtitleLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 14,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginTop = 4
                }
            };
            WalletUiCommon.ApplyDefaultFont(tokenSubtitleLabel);

            titleBlock.Add(tokenTitleLabel);
            titleBlock.Add(tokenSubtitleLabel);

            var totalBlock = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    alignItems = Align.FlexEnd
                }
            };
            totalValueLabel = new Label("0")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 20,
                    color = WalletUiTheme.TextPrimary
                }
            };
            WalletUiCommon.ApplyDefaultFont(totalValueLabel);

            totalFiatLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 13,
                    unityTextAlign = TextAnchor.MiddleRight,
                    marginTop = 2
                }
            };
            WalletUiCommon.ApplyDefaultFont(totalFiatLabel);

            totalBlock.Add(totalValueLabel);
            totalBlock.Add(totalFiatLabel);

            card.Add(titleBlock);
            card.Add(totalBlock);
            return card;
        }

        private VisualElement BuildStatsRow()
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    justifyContent = Justify.FlexStart,
                    alignItems = Align.Stretch,
                    marginBottom = 10
                }
            };
            WalletUiCommon.ApplyDefaultFont(row);

            row.Add(CreateStatCard("Available", out availableValueLabel, out availableFiatLabel));
            row.Add(CreateStatCard("Staked", out stakedValueLabel, out stakedFiatLabel));
            row.Add(CreateStatCard("Claimable", out claimableValueLabel, out claimableFiatLabel));

            return row;
        }

        private VisualElement BuildActionsRow()
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    marginBottom = 8
                }
            };
            WalletUiCommon.ApplyDefaultFont(row);

            sendButton = WalletUiCommon.CreateSecondaryButton("Send", () => RunSafeAsync(SendAsync), 16, 44);
            sendButton.style.minWidth = 160;
            sendButton.style.marginRight = 10;
            sendButton.style.marginBottom = 10;
            row.Add(sendButton);

            stakeButton = WalletUiCommon.CreateSecondaryButton("Stake", () => RunSafeAsync(StakeAsync), 16, 44);
            stakeButton.style.minWidth = 140;
            stakeButton.style.marginRight = 10;
            stakeButton.style.marginBottom = 10;
            row.Add(stakeButton);

            unstakeButton = WalletUiCommon.CreateSecondaryButton("Unstake", () => RunSafeAsync(UnstakeAsync), 16, 44);
            unstakeButton.style.minWidth = 140;
            unstakeButton.style.marginRight = 10;
            unstakeButton.style.marginBottom = 10;
            row.Add(unstakeButton);

            claimButton = WalletUiCommon.CreateSecondaryButton("Claim", () => RunSafeAsync(ClaimAsync), 16, 44);
            claimButton.style.minWidth = 130;
            claimButton.style.marginRight = 10;
            claimButton.style.marginBottom = 10;
            row.Add(claimButton);

            return row;
        }

        private VisualElement BuildAdvancedActionsRow()
        {
            var container = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    marginBottom = 12
                }
            };
            WalletUiCommon.ApplyDefaultFont(container);

            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    marginTop = 2
                }
            };
            WalletUiCommon.ApplyDefaultFont(row);

            burnButton = WalletUiCommon.CreateSecondaryButton("Burn", () => RunSafeAsync(BurnAsync), 14, 36);
            burnButton.style.minWidth = 110;
            burnButton.style.marginRight = 8;
            burnButton.style.marginBottom = 8;
            row.Add(burnButton);

            smRewardButton = WalletUiCommon.CreateSecondaryButton("SM reward", () => RunSafeAsync(ClaimSmRewardAsync), 14, 36);
            smRewardButton.style.minWidth = 130;
            smRewardButton.style.marginRight = 8;
            smRewardButton.style.marginBottom = 8;
            row.Add(smRewardButton);

            infoButton = WalletUiCommon.CreateSecondaryButton("Address info", () => RunSafeAsync(ShowDevInfoAsync), 14, 36);
            infoButton.style.minWidth = 140;
            infoButton.style.marginRight = 8;
            infoButton.style.marginBottom = 8;
            row.Add(infoButton);

            tokenExplorerButton = WalletUiCommon.CreateSecondaryButton("Token explorer", () => OpenUrl(BuildTokenExplorerUrl(currentSymbol)), 14, 36);
            tokenExplorerButton.style.minWidth = 140;
            tokenExplorerButton.style.marginRight = 8;
            tokenExplorerButton.style.marginBottom = 8;
            row.Add(tokenExplorerButton);

            tokenHoldersButton = WalletUiCommon.CreateSecondaryButton("Top holders", () => OpenUrl(BuildTokenHoldersUrl(currentSymbol)), 14, 36);
            tokenHoldersButton.style.minWidth = 120;
            tokenHoldersButton.style.marginRight = 8;
            tokenHoldersButton.style.marginBottom = 8;
            row.Add(tokenHoldersButton);

            coingeckoButton = WalletUiCommon.CreateSecondaryButton("Coingecko", () => OpenUrl("https://www.coingecko.com/en/coins/phantasma"), 14, 36);
            coingeckoButton.style.minWidth = 120;
            coingeckoButton.style.marginRight = 8;
            coingeckoButton.style.marginBottom = 8;
            row.Add(coingeckoButton);

            cmcButton = WalletUiCommon.CreateSecondaryButton("CoinMarketCap", () => OpenUrl("https://coinmarketcap.com/en/currencies/phantasma"), 14, 36);
            cmcButton.style.minWidth = 150;
            cmcButton.style.marginRight = 8;
            cmcButton.style.marginBottom = 8;
            row.Add(cmcButton);

            container.Add(row);
            return container;
        }

        private VisualElement BuildInfoSection()
        {
            var section = new VisualElement
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
                    borderBottomColor = WalletUiTheme.ModalBorder
                }
            };
            WalletUiCommon.ApplyDefaultFont(section);

            section.Add(BuildInfoRow("Decimals", out decimalsLabel));
            section.Add(BuildInfoRow("Supply", out supplyValueLabel));
            section.Add(BuildInfoRow("Max supply", out maxSupplyLabel));
            section.Add(BuildInfoRow("Burned", out burnedSupplyLabel));
            section.Add(BuildInfoRow("Flags", out flagsLabel));

            return section;
        }

        private VisualElement CreateStatCard(string title, out Label valueLabel, out Label fiatLabel)
        {
            var card = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1,
                    minWidth = 180,
                    paddingLeft = 14,
                    paddingRight = 14,
                    paddingTop = 10,
                    paddingBottom = 10,
                    marginRight = 10,
                    marginBottom = 10,
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
                    borderTopColor = WalletUiTheme.HighlightEdge,
                    borderBottomColor = WalletUiTheme.CardBorder
                }
            };
            WalletUiCommon.ApplyDefaultFont(card);

            var titleLabel = new Label(title)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 13,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 4
                }
            };
            WalletUiCommon.ApplyDefaultFont(titleLabel);
            card.Add(titleLabel);

            valueLabel = new Label("0")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 17,
                    color = WalletUiTheme.TextPrimary
                }
            };
            WalletUiCommon.ApplyDefaultFont(valueLabel);
            card.Add(valueLabel);

            fiatLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 12,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginTop = 2
                }
            };
            WalletUiCommon.ApplyDefaultFont(fiatLabel);
            card.Add(fiatLabel);

            return card;
        }

        private VisualElement BuildInfoRow(string labelText, out Label valueLabel)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.SpaceBetween,
                    marginBottom = 6
                }
            };
            WalletUiCommon.ApplyDefaultFont(row);

            var label = new Label(labelText)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 13,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            WalletUiCommon.ApplyDefaultFont(label);
            row.Add(label);

            valueLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 13,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleRight,
                    flexGrow = 1,
                    marginLeft = 8
                }
            };
            WalletUiCommon.ApplyDefaultFont(valueLabel);
            row.Add(valueLabel);

            return row;
        }

        private async void RefreshView()
        {
            try
            {
                UpdateNavSelection();
                var accountManager = AccountManager.Instance;
                if (accountManager == null)
                {
                    SetStatus("Account is not ready.");
                    return;
                }

                var settings = accountManager.Settings;
                if (settings == null)
                {
                    SetStatus("Settings are not loaded yet.");
                    return;
                }

                var symbol = string.IsNullOrWhiteSpace(currentSymbol) ? context.ViewState.TokenDashboardSymbol : currentSymbol;
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    SetStatus("Pick an asset from Balances to open its dashboard.");
                    ClearUi();
                    return;
                }

                var snapshot = context.ViewState.GetBalancesSnapshot(() => balancePresenter.BuildSnapshot());
                if (snapshot == null || snapshot.Balances == null || snapshot.Balances.Count == 0)
                {
                    SetStatus("No balances to show.");
                    ClearUi();
                    return;
                }

                if (snapshot.IsRefreshing)
                {
                    SetStatus("Fetching balances...");
                    return;
                }

                if (snapshot.HasError)
                {
                    SetStatus(snapshot.ErrorMessage);
                    ClearUi();
                    return;
                }

                var entry = snapshot.Balances.FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    SetStatus($"No balance found for {symbol}.");
                    ClearUi();
                    return;
                }

                if (!accountManager.HasSelection)
                {
                    SetStatus("Select a wallet first.");
                    ClearUi();
                    return;
                }

                if (accountManager.CurrentAccount.passwordProtected && string.IsNullOrEmpty(accountManager.CurrentPasswordHash))
                {
                    SetStatus("Wallet is locked. Open it from the wallet list.");
                    ClearUi();
                    return;
                }

                if (accountManager.CurrentState == null)
                {
                    SetStatus("Account state is unavailable.");
                    ClearUi();
                    return;
                }

                subtitleLabel.text = WalletUiCommon.BuildContextSubtitle("Asset", snapshot.AccountName, snapshot.Platform);
                summaryLabel.text = $"{entry.Symbol} • {entry.Decimals} decimals";
                WalletUiCommon.ApplyNetworkBadge(subtitleNetworkLabel, settings.nexusName, settings.nexusKind);

                UpdateHero(entry, accountManager.CurrentPlatform);
                UpdateStats(entry);
                UpdateInfo(entry, accountManager.CurrentPlatform);
                UpdateActions(entry, accountManager);
                SetStatus(string.Empty);
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{LogPrefix}Token dashboard refresh failed: {e}");
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

        private void UpdateHero(WalletBalanceEntry entry, PlatformKind platform)
        {
            var token = Tokens.GetToken(entry.Symbol, platform);
            tokenTitleLabel.text = $"{entry.Symbol} {(string.IsNullOrWhiteSpace(token?.Name) ? string.Empty : $"• {token.Name}")}".Trim();
            tokenSubtitleLabel.text = string.Empty;

            if (ResourceManager.Instance != null)
            {
                var iconTexture = ResourceManager.Instance.GetToken(entry.Symbol, platform) as Texture2D;
                tokenIcon.image = iconTexture;
                tokenIcon.style.display = iconTexture != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            else
            {
                tokenIcon.style.display = DisplayStyle.None;
            }

            var totalText = WalletAmountFormatter.Format(entry.Total, entry.Decimals, AccountManager.Instance?.Settings?.balanceDisplayPrecision ?? 4);
            totalValueLabel.text = $"{totalText} {entry.Symbol}";
            var totalFiat = SumFiat(entry.FiatWorth, entry.StakedFiatWorth);
            totalFiatLabel.text = FormatFiat(totalFiat);
        }

        private void UpdateStats(WalletBalanceEntry entry)
        {
            var precision = AccountManager.Instance?.Settings?.balanceDisplayPrecision ?? 4;
            availableValueLabel.text = $"{WalletAmountFormatter.Format(entry.Available, entry.Decimals, precision)} {entry.Symbol}";
            stakedValueLabel.text = $"{WalletAmountFormatter.Format(entry.Staked, entry.Decimals, precision)} {entry.Symbol}";
            claimableValueLabel.text = $"{WalletAmountFormatter.Format(entry.Claimable, entry.Decimals, precision)} {entry.Symbol}";

            availableFiatLabel.text = FormatFiat(entry.FiatWorth);
            stakedFiatLabel.text = FormatFiat(entry.StakedFiatWorth);
            claimableFiatLabel.text = entry.Claimable > BigInteger.Zero ? "Pending rewards" : string.Empty;
        }

        private void UpdateInfo(WalletBalanceEntry entry, PlatformKind platform)
        {
            var token = Tokens.GetToken(entry.Symbol, platform);
            decimalsLabel.text = token != null ? token.Decimals.ToString(CultureInfo.InvariantCulture) : entry.Decimals.ToString(CultureInfo.InvariantCulture);
            supplyValueLabel.text = FormatSupply(token?.CurrentSupply, entry.Decimals, entry.Symbol);
            maxSupplyLabel.text = FormatSupply(token?.MaxSupply, entry.Decimals, entry.Symbol);
            burnedSupplyLabel.text = FormatSupply(token?.BurnedSupply, entry.Decimals, entry.Symbol);
            flagsLabel.text = token?.Flags ?? string.Empty;
        }

        private void UpdateActions(WalletBalanceEntry entry, AccountManager accountManager)
        {
            var platform = accountManager.CurrentPlatform;
            var isPhantasma = platform == PlatformKind.Phantasma;
            var token = Tokens.GetToken(entry.Symbol, platform);
            var isFungible = token?.IsFungible() ?? entry.Fungible;
            var devMode = accountManager.Settings?.devMode ?? false;

            SetActionButtonState(sendButton, isPhantasma && isFungible && entry.Available > BigInteger.Zero && (token?.IsTransferable() ?? true));
            sendButton.text = "Send";

            if (string.Equals(entry.Symbol, DomainSettings.StakingTokenSymbol, StringComparison.OrdinalIgnoreCase) && isPhantasma)
            {
                var soulDecimals = Tokens.GetTokenDecimals(DomainSettings.StakingTokenSymbol, platform);
                var minStakeForButton = WalletAmountParser.FromDecimal(1.2m, soulDecimals);
                stakeButton.style.display = DisplayStyle.Flex;
                unstakeButton.style.display = DisplayStyle.Flex;
                claimButton.style.display = DisplayStyle.None;
                SetActionButtonState(claimButton, false);
                SetActionButtonState(stakeButton, entry.Available > minStakeForButton);

                var unstakeAvailability = stakeService.GetUnstakeAvailability();
                SetActionButtonState(unstakeButton, entry.Staked > BigInteger.Zero && unstakeAvailability.Success);
            }
            else if (string.Equals(entry.Symbol, DomainSettings.FuelTokenSymbol, StringComparison.OrdinalIgnoreCase) && isPhantasma)
            {
                SetActionButtonState(stakeButton, false);
                SetActionButtonState(unstakeButton, false);
                stakeButton.style.display = DisplayStyle.None;
                unstakeButton.style.display = DisplayStyle.None;
                claimButton.style.display = DisplayStyle.Flex;
                SetActionButtonState(claimButton, entry.Claimable > BigInteger.Zero);
            }
            else
            {
                SetActionButtonState(stakeButton, false);
                SetActionButtonState(unstakeButton, false);
                SetActionButtonState(claimButton, false);
                stakeButton.style.display = DisplayStyle.None;
                unstakeButton.style.display = DisplayStyle.None;
                claimButton.style.display = DisplayStyle.None;
            }

            var burnEligible = devMode && isPhantasma && entry.Burnable && isFungible && entry.Available > BigInteger.Zero;
            SetActionButtonState(burnButton, burnEligible);
            burnButton.style.display = burnEligible ? DisplayStyle.Flex : DisplayStyle.None;

            var smEligible = devMode &&
                string.Equals(entry.Symbol, DomainSettings.StakingTokenSymbol, StringComparison.OrdinalIgnoreCase) &&
                entry.Staked >= WalletAmountParser.FromDecimal(50000m, entry.Decimals);
            SetActionButtonState(smRewardButton, smEligible);
            smRewardButton.style.display = smEligible ? DisplayStyle.Flex : DisplayStyle.None;

            SetActionButtonState(infoButton, devMode);
            infoButton.style.display = devMode ? DisplayStyle.Flex : DisplayStyle.None;

            var tokenUrl = BuildTokenExplorerUrl(entry.Symbol);
            var holdersUrl = BuildTokenHoldersUrl(entry.Symbol);
            var showExplorer = isPhantasma && !string.IsNullOrWhiteSpace(tokenUrl);
            var showHolders = isPhantasma && !string.IsNullOrWhiteSpace(holdersUrl);
            var isSoul = string.Equals(entry.Symbol, DomainSettings.StakingTokenSymbol, StringComparison.OrdinalIgnoreCase);

            SetActionButtonState(tokenExplorerButton, showExplorer);
            tokenExplorerButton.style.display = showExplorer ? DisplayStyle.Flex : DisplayStyle.None;

            SetActionButtonState(tokenHoldersButton, showHolders);
            tokenHoldersButton.style.display = showHolders ? DisplayStyle.Flex : DisplayStyle.None;

            SetActionButtonState(coingeckoButton, isSoul);
            coingeckoButton.style.display = isSoul ? DisplayStyle.Flex : DisplayStyle.None;

            SetActionButtonState(cmcButton, isSoul);
            cmcButton.style.display = isSoul ? DisplayStyle.Flex : DisplayStyle.None;

            var showAdvanced = burnEligible || smEligible || devMode || showExplorer || showHolders || isSoul;
            advancedActions.style.display = showAdvanced ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private async Task SendAsync()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                SetStatus("Account is not ready.");
                return;
            }

            var symbol = string.IsNullOrWhiteSpace(currentSymbol) ? context.ViewState.TokenDashboardSymbol : currentSymbol;
            if (string.IsNullOrWhiteSpace(symbol))
            {
                SetStatus("No asset selected.");
                return;
            }
            var availability = transferService.GetFungibleAvailability(symbol);
            if (!availability.Success)
            {
                await ShowErrorAsync(availability.Error);
                return;
            }

            var destination = await PromptDestinationAsync(symbol);
            if (string.IsNullOrWhiteSpace(destination))
            {
                return;
            }

            var amount = await PromptAmountAsync(symbol, availability.Data1, availability.Data2);
            if (amount <= 0)
            {
                return;
            }

            var planResult = transferService.BuildFungibleTransferDraft(symbol, amount, destination);
            if (!planResult.Success)
            {
                await ShowErrorAsync(planResult.Error);
                return;
            }

            var sendResult = await transactionOrchestrator.SendTransactionDraftAsync(planResult.Draft, true);
            TxResultMessage(sendResult.hash, sendResult.txResult, sendResult.error, $"You transferred {WalletAmountFormatter.Format(planResult.Amount, Tokens.GetTokenDecimals(symbol, accountManager.CurrentPlatform), MoneyFormatType.Long)} {symbol}!");
        }

        private async Task StakeAsync()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                SetStatus("Account is not ready.");
                return;
            }

            var symbol = DomainSettings.StakingTokenSymbol;
            var entry = GetCurrentEntry();
            if (entry == null)
            {
                return;
            }

            var soulDecimals = Tokens.GetTokenDecimals(symbol, accountManager.CurrentPlatform);
            var minStake = WalletAmountParser.FromDecimal(0.1m, soulDecimals);
            var maxStake = entry.Available;
            var amount = await PromptAmountAsync(symbol, minStake, maxStake, BuildStakeCaption(entry, soulDecimals));
            if (amount <= 0)
            {
                return;
            }

            var confirmMessage = BuildStakeConfirmMessage(amount, entry, soulDecimals);
            var confirmation = await WalletUiModalHelper.ShowConfirmAsync(modalHost, "Stake SOUL", confirmMessage, "Stake", "Cancel");
            if (confirmation != PromptResult.Success)
            {
                return;
            }

            if (!await EnsureKcalAsync())
            {
                return;
            }

            var draftResult = stakeService.BuildStakeDraft(amount);
            if (!draftResult.Success)
            {
                await ShowErrorAsync(draftResult.Error);
                return;
            }

            var sendResult = await transactionOrchestrator.SendTransactionDraftAsync(draftResult.Draft, true);
            TxResultMessage(sendResult.hash, sendResult.txResult, sendResult.error, "Your SOUL tokens were staked!");
        }

        private async Task UnstakeAsync()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                SetStatus("Account is not ready.");
                return;
            }

            var entry = GetCurrentEntry();
            if (entry == null)
            {
                return;
            }

            var availability = stakeService.GetUnstakeAvailability();
            if (!availability.Success)
            {
                await ShowErrorAsync(availability.Error);
                return;
            }

            var soulDecimals = Tokens.GetTokenDecimals(DomainSettings.StakingTokenSymbol, accountManager.CurrentPlatform);
            var minUnstake = WalletAmountParser.FromDecimal(0.1m, soulDecimals);
            var amount = await PromptAmountAsync(DomainSettings.StakingTokenSymbol, minUnstake, entry.Staked);
            if (amount <= 0)
            {
                return;
            }

            var messageResult = stakeService.BuildUnstakeMessage(amount);
            if (!messageResult.Success)
            {
                await ShowErrorAsync(messageResult.Error);
                return;
            }

            var confirmation = await WalletUiModalHelper.ShowConfirmAsync(modalHost, "Unstake SOUL", messageResult.Data, "Unstake", "Cancel");
            if (confirmation != PromptResult.Success)
            {
                return;
            }

            if (!await EnsureKcalAsync())
            {
                return;
            }

            var draftResult = stakeService.BuildUnstakeDraft(amount);
            if (!draftResult.Success)
            {
                await ShowErrorAsync(draftResult.Error);
                return;
            }

            var sendResult = await transactionOrchestrator.SendTransactionDraftAsync(draftResult.Draft, true);
            TxResultMessage(sendResult.hash, sendResult.txResult, sendResult.error, "Your SOUL tokens were unstaked!");
        }

        private async Task ClaimAsync()
        {
            var entry = GetCurrentEntry();
            if (entry == null || entry.Claimable <= BigInteger.Zero)
            {
                SetStatus("Nothing to claim.");
                return;
            }

            var messageResult = stakeService.BuildClaimKcalMessage(entry.Claimable);
            if (!messageResult.Success)
            {
                await ShowErrorAsync(messageResult.Error);
                return;
            }

            var confirm = await WalletUiModalHelper.ShowConfirmAsync(modalHost, "Claim KCAL", messageResult.Data, "Claim", "Cancel");
            if (confirm != PromptResult.Success)
            {
                return;
            }

            if (!await EnsureKcalAsync())
            {
                return;
            }

            var draftResult = stakeService.BuildClaimKcalDraft(entry.Claimable);
            if (!draftResult.Success)
            {
                await ShowErrorAsync(draftResult.Error);
                return;
            }

            var sendResult = await transactionOrchestrator.SendTransactionDraftAsync(draftResult.Draft, true);
            TxResultMessage(sendResult.hash, sendResult.txResult, sendResult.error, "Your KCAL tokens were claimed!");
        }

        private async Task BurnAsync()
        {
            var entry = GetCurrentEntry();
            if (entry == null)
            {
                return;
            }

            var amount = await PromptAmountAsync(entry.Symbol, WalletAmountParser.FromDecimal(0.1m, entry.Decimals), entry.Available);
            if (amount <= 0)
            {
                return;
            }

            var prep = burnService.PrepareFungibleBurn(entry.Symbol, entry.Available, amount);
            if (!prep.Success)
            {
                await ShowErrorAsync(prep.Error);
                return;
            }

            var confirmMessage = string.IsNullOrWhiteSpace(prep.Message) ? "Are you sure you want to burn these tokens?" : prep.Message;
            var confirm = await WalletUiModalHelper.ShowConfirmAsync(modalHost, "Burn tokens", confirmMessage, "Burn", "Cancel");
            if (confirm != PromptResult.Success)
            {
                return;
            }

            var sendResult = await transactionOrchestrator.SendTransactionDraftAsync(prep.Data, true);
            TxResultMessage(sendResult.hash, sendResult.txResult, sendResult.error, $"You burned {WalletAmountFormatter.Format(amount, entry.Decimals, MoneyFormatType.Long)} {entry.Symbol} tokens!");
        }

        private async Task ClaimSmRewardAsync()
        {
            var draftResult = stakeService.BuildClaimSmRewardDraft();
            if (!draftResult.Success)
            {
                await ShowErrorAsync(draftResult.Error);
                return;
            }

            var confirm = await WalletUiModalHelper.ShowConfirmAsync(modalHost, "Claim SM reward", "Claim Soul Master reward now?", "Claim", "Cancel");
            if (confirm != PromptResult.Success)
            {
                return;
            }

            if (!await EnsureKcalAsync())
            {
                return;
            }

            var sendResult = await transactionOrchestrator.SendTransactionDraftAsync(draftResult.Draft, true);
            TxResultMessage(sendResult.hash, sendResult.txResult, sendResult.error, "You claimed SM reward!");
        }

        private async Task ShowDevInfoAsync()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                SetStatus("Account is not ready.");
                return;
            }

            var address = accountManager.CurrentState?.address;
            if (string.IsNullOrWhiteSpace(address))
            {
                SetStatus("Address is not available.");
                return;
            }

            var (info, error) = await GetAddressInfoAsync(address, accountManager.CurrentAccount);
            if (!string.IsNullOrEmpty(error))
            {
                await ShowErrorAsync(error);
                return;
            }

            await WalletUiModalHelper.ShowInfoAsync(modalHost, "Account info", info);
        }

        private WalletBalanceEntry GetCurrentEntry()
        {
            var snapshot = context.ViewState.GetBalancesSnapshot(() => balancePresenter.BuildSnapshot());
            if (snapshot?.Balances == null)
            {
                return null;
            }

            var symbol = string.IsNullOrWhiteSpace(currentSymbol) ? context.ViewState.TokenDashboardSymbol : currentSymbol;
            return snapshot.Balances.FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<string> PromptDestinationAsync(string symbol)
        {
            var accounts = AccountManager.Instance?.Accounts;
            var (destResult, destInput) = await WalletUiModalHelper.ShowAddressInputDialogAsync(
                modalHost,
                $"Send {symbol}",
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

        private async Task<BigInteger> PromptAmountAsync(string symbol, BigInteger minAmount, BigInteger maxAmount, string captionOverride = null)
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                await ShowErrorAsync("Account is not ready.");
                return BigInteger.Zero;
            }

            var decimals = Tokens.GetTokenDecimals(symbol, accountManager.CurrentPlatform);
            var displayPrecision = accountManager.Settings?.balanceDisplayPrecision ?? 4;
            var caption = string.IsNullOrWhiteSpace(captionOverride)
                ? $"Amount between {WalletAmountFormatter.Format(minAmount, decimals, displayPrecision)} and {WalletAmountFormatter.Format(maxAmount, decimals, displayPrecision)} {symbol}"
                : captionOverride;
            var maxText = WalletAmountFormatter.Format(maxAmount, decimals, displayPrecision);
            var (amountResult, amountInput) = await WalletUiModalHelper.ShowAmountInputDialogAsync(
                modalHost,
                $"Amount of {symbol}",
                caption,
                maxText,
                value =>
                {
                    var validation = amountValidator.ParseAndValidate(value, symbol, minAmount, maxAmount);
                    return (validation.Success, validation.Error);
                },
                confirmLabel: "Continue",
                cancelLabel: "Cancel",
                initialValue: WalletAmountFormatter.Format(maxAmount, decimals, displayPrecision));

            if (amountResult != PromptResult.Success)
            {
                return BigInteger.Zero;
            }

            var validation = amountValidator.ParseAndValidate(amountInput, symbol, minAmount, maxAmount);
            if (!validation.Success)
            {
                await ShowErrorAsync(validation.Error);
                return BigInteger.Zero;
            }

            return validation.Data;
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

        private async Task<(string info, string error)> GetAddressInfoAsync(string address, Account account)
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                return (string.Empty, "Account is not ready.");
            }

            var tcs = new TaskCompletionSource<(string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
            accountManager.GetPhantasmaAddressInfo(address, account, (result, error) => tcs.TrySetResult((result, error)));
            return await tcs.Task;
        }

        private async Task ShowErrorAsync(string message)
        {
            await WalletUiModalHelper.ShowErrorAsync(modalHost, "Error", message, null, null);
            SetStatus(message);
        }

        private string BuildFiatLine(string available, string staked)
        {
            if (string.IsNullOrWhiteSpace(available) && string.IsNullOrWhiteSpace(staked))
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(available) && !string.IsNullOrWhiteSpace(staked))
            {
                return $"≈ {available} (liquid) + {staked} (staked)";
            }

            return $"≈ {(!string.IsNullOrWhiteSpace(available) ? available : staked)}";
        }

        private string FormatSupply(string raw, uint decimals, string symbol)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "(n/a)";
            }

            if (!BigInteger.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            {
                return raw;
            }

            return $"{WalletAmountFormatter.Format(value, decimals, MoneyFormatType.Long)} {symbol}";
        }

        private string FormatFiat(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
        }

        private string SumFiat(string availableFiat, string stakedFiat)
        {
            // Both values already come preformatted; we can only sum if both parse cleanly to decimal.
            if (decimal.TryParse(availableFiat?.Replace("$", string.Empty).Replace(",", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out var available) &&
                decimal.TryParse(stakedFiat?.Replace("$", string.Empty).Replace(",", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out var staked))
            {
                var total = available + staked;
                return $"≈ {WalletAmountFormatter.Format(total, MoneyFormatType.Standard)} $";
            }

            return availableFiat ?? stakedFiat ?? string.Empty;
        }

        private string BuildStakeCaption(WalletBalanceEntry entry, uint decimals)
        {
            decimal? expectedKcal = null;
            if (WalletAmountFormatter.TryToDecimal(entry.Staked + entry.Available, decimals, out var totalStake))
            {
                var crownMultiplier = 1m;
                var crowns = AccountManager.Instance?.CurrentState?.balances?.FirstOrDefault(x => x.Symbol.Equals("CROWN", StringComparison.OrdinalIgnoreCase));
                if (crowns != null && WalletAmountFormatter.TryToDecimal(crowns.Available, crowns.Decimals, out var crownAmount))
                {
                    crownMultiplier += crownAmount * 0.05m;
                }

                expectedKcal = totalStake * 0.002m * crownMultiplier;
            }

            var caption = "Enter amount to stake";
            if (expectedKcal.HasValue)
            {
                var fmt = expectedKcal.Value >= 1 ? MoneyFormatType.Standard : MoneyFormatType.Long;
                caption += $"\nExpected daily KCAL: {WalletAmountFormatter.Format(expectedKcal.Value, fmt)}";
            }

            caption += "\nStaking locks SOUL for 24 hours.";
            return caption;
        }

        private string BuildStakeConfirmMessage(BigInteger amount, WalletBalanceEntry entry, uint decimals)
        {
            var accountManager = AccountManager.Instance;
            var message = $"Do you want to stake {WalletAmountFormatter.Format(amount, decimals)} SOUL?";

            if (accountManager?.CurrentState != null)
            {
                decimal? expectedKcal = null;
                if (WalletAmountFormatter.TryToDecimal(amount + entry.Staked, decimals, out var totalStake))
                {
                    var crownMultiplier = 1m;
                    var crowns = accountManager.CurrentState.balances?.FirstOrDefault(x => x.Symbol.Equals("CROWN", StringComparison.OrdinalIgnoreCase));
                    if (crowns != null && WalletAmountFormatter.TryToDecimal(crowns.Available, crowns.Decimals, out var crownAmount))
                    {
                        crownMultiplier += crownAmount * 0.05m;
                    }

                    expectedKcal = totalStake * 0.002m * crownMultiplier;
                }

                if (expectedKcal.HasValue)
                {
                    var fmt = expectedKcal.Value >= 1 ? MoneyFormatType.Standard : MoneyFormatType.Long;
                    message += $"\nYou will be able to claim {WalletAmountFormatter.Format(expectedKcal.Value, fmt)} KCAL per day.";
                }

                var kcalBalance = accountManager.CurrentState.balances?.FirstOrDefault(s => s.Symbol == DomainSettings.FuelTokenSymbol);
                var kcalDecimals = Tokens.GetTokenDecimals(DomainSettings.FuelTokenSymbol, accountManager.CurrentPlatform);
                if (kcalBalance != null && kcalBalance.Claimable > BigInteger.Zero)
                {
                    var fmt = WalletAmountFormatter.Format(kcalBalance.Claimable, kcalDecimals, MoneyFormatType.Long);
                    message += $"\n\nAll unclaimed KCAL will be claimed: {fmt} KCAL.";
                }
            }

            var hundredKSoul = WalletAmountParser.FromDecimal(100000m, decimals);
            if (amount >= hundredKSoul)
            {
                message += "\n\nSoul Master rewards are distributed evenly to every wallet with 50K or more SOUL. As you are staking over 100K SOUL, to maximise your rewards, you may wish to stake each 50K SOUL in a separate wallet.";
            }

            message += "\n\nPlease note, after staking you won't be able to unstake SOUL tokens for next 24 hours.";
            return message;
        }

        private bool EnsureTokensReady()
        {
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

        private async Task<bool> EnsureKcalAsync()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                SetStatus("Account is not ready.");
                return false;
            }

            if (accountManager.CurrentPlatform != PlatformKind.Phantasma)
            {
                return true;
            }

            var feeDecimals = Tokens.GetTokenDecimals(DomainSettings.FuelTokenSymbol, accountManager.CurrentPlatform);
            var minFee = WalletAmountParser.FromDecimal(0.1m, feeDecimals);
            var tcs = new TaskCompletionSource<(PromptResult result, string error)>(TaskCreationOptions.RunContinuationsAsynchronously);
            feeRequirement.EnsureKcal(minFee, (result, error) => tcs.TrySetResult((result, error)));
            var (res, errorText) = await tcs.Task;
            if (res == PromptResult.Success)
            {
                return true;
            }

            await ShowErrorAsync(string.IsNullOrWhiteSpace(errorText) ? "KCAL is required to make transactions!" : errorText);
            return false;
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

            var entry = GetCurrentEntry();
            var accountManager = AccountManager.Instance;
            if (entry != null && accountManager != null)
            {
                UpdateActions(entry, accountManager);
            }
        }

        private IEnumerable<Button> EnumerateActionButtons()
        {
            if (sendButton != null) yield return sendButton;
            if (stakeButton != null) yield return stakeButton;
            if (unstakeButton != null) yield return unstakeButton;
            if (claimButton != null) yield return claimButton;
            if (burnButton != null) yield return burnButton;
            if (smRewardButton != null) yield return smRewardButton;
            if (infoButton != null) yield return infoButton;
        }

        private void SetActionButtonState(Button button, bool enabled)
        {
            if (button == null)
            {
                return;
            }

            WalletUiCommon.SetButtonEnabledVisual(button, enabled, WalletUiTheme.TextPrimary, WalletUiTheme.TextMuted);
        }

        private Task<PromptResult> ShowSendProgressAsync(string description, int txCount)
        {
            return transactionDialogs.ShowSendProgressAsync(description, txCount);
        }

        private Task<(Hash hash, TransactionResult txResult, string error)> StartConfirmationAsync(Hash hash, bool refreshBalanceAfterConfirmation)
        {
            return transactionDialogs.StartConfirmationAsync(hash, refreshBalanceAfterConfirmation);
        }

        private void TxResultMessage(Hash hash, TransactionResult txResult, string error, string successMessage)
        {
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

        private void ClearUi()
        {
            tokenTitleLabel.text = "Token";
            tokenSubtitleLabel.text = string.Empty;
            totalValueLabel.text = "0";
            totalFiatLabel.text = string.Empty;
            if (tokenIcon != null)
            {
                tokenIcon.image = null;
                tokenIcon.style.display = DisplayStyle.None;
            }
            availableValueLabel.text = "0";
            availableFiatLabel.text = string.Empty;
            stakedValueLabel.text = "0";
            stakedFiatLabel.text = string.Empty;
            claimableValueLabel.text = "0";
            claimableFiatLabel.text = string.Empty;
            decimalsLabel.text = string.Empty;
            supplyValueLabel.text = string.Empty;
            maxSupplyLabel.text = string.Empty;
            burnedSupplyLabel.text = string.Empty;
            flagsLabel.text = string.Empty;
        }

        private void SetStatus(string text)
        {
            statusLabel.text = text ?? string.Empty;
            statusLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private string BuildTokenExplorerUrl(string symbol)
        {
            var settings = AccountManager.Instance?.Settings;
            var baseUrl = settings?.phantasmaExplorer;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return string.Empty;
            }

            if (!baseUrl.EndsWith("/"))
            {
                baseUrl += "/";
            }

            return $"{baseUrl}en/token?id={symbol}";
        }

        private string BuildTokenHoldersUrl(string symbol)
        {
            var url = BuildTokenExplorerUrl(symbol);
            return string.IsNullOrWhiteSpace(url) ? string.Empty : $"{url}&tab=holders";
        }

        private void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            Application.OpenURL(url);
        }

        private void UpdateNavSelection()
        {
            WalletUiCommon.SetNavState(navBalances, true);
            WalletUiCommon.SetNavState(navHistory, false);
            WalletUiCommon.SetNavState(navAccount, false);
            WalletUiCommon.SetNavState(navExit, false);
        }

        private void RunSafeAsync(Func<Task> action)
        {
            async void Wrapper()
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

            Wrapper();
        }
    }
}
