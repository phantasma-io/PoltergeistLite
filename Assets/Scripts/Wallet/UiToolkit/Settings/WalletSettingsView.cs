#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_WIN || UNITY_EDITOR_LINUX
#define UITK_DESKTOP_PREVIEW_SUPPORTED
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Unity.Core.Logging;
using PhantasmaPhoenix.VM;
using Poltergeist;
using Poltergeist.UiToolkit;
using Poltergeist.Wallet;
using Poltergeist.Build;

namespace Poltergeist.UiToolkit.Settings
{
    /// <summary>
    /// Settings screen for UITK, composed with shared form builders to keep UI consistent.
    /// Settings screen for runtime UI Toolkit flows, including confirmations and dev tools.
    /// </summary>
    public sealed class WalletSettingsView : IDisposable
    {
        private const string LogPrefix = "[UITK] ";
        private static readonly bool ShowOnlyFirstSettingsField = false; // Show full settings form (set true for debugging layout)
        private const bool EnableScrollDebugLog = false;
        private const string ScrollLogPrefix = "[UITK][Settings][Scroll] ";

        private readonly WalletSettingsPresenter presenter;
        private readonly WalletSettingsActions actions;
        private readonly WalletSettingsViewState viewState;
        private readonly Action onExit;
        private readonly Func<string, string, Task<ValidationResult>> onOpenDebugNft;
        private EventCallback<KeyDownEvent> tabBlockHandler;

        private VisualElement root;
        private ScrollView scrollView;
        private Label statusLabel;
        private Label warningLabel;
        private HeaderBlockElements headerBlock;
        private SubHeaderElements subHeader;
        private Label buildInfoLabel;

        // Controls for refresh/update
        private PopupField<string> currencyDropdown;
        private PopupField<string> nexusDropdown;
        private PopupField<string> mnemonicDropdown;
        private PopupField<string> passwordModeDropdown;
        private PopupField<string> logLevelDropdown;
#if UITK_DESKTOP_PREVIEW_SUPPORTED
        private PopupField<string> previewDeviceDropdown;
#endif
        private TextField logFolderPathField;
        private Label defaultEndpointInfoLabel;
        private TextField rpcUrlField;
        private TextField explorerUrlField;
        private TextField nftExplorerUrlField;
        private TextField poaUrlField;
        private TextField nexusNameField;
        private TextField feePriceField;
        private TextField feeLimitField;
        private TextField balanceThresholdField;
        private TextField balancePrecisionField;
        private TextField framerateField;
        private TextField uiScaleMultiplierField;
        private TextField windowWidthField;
        private TextField windowHeightField;
        private TextField scriptlessGasField;
        private TextField scriptlessDataField;
        private Toggle devModeToggle;
        private Toggle devNoValidationToggle;
        private Toggle useVmTransactionsToggle;
        private Toggle logOverwriteToggle;
        private Toggle showUnstableToolsToggle;
        private VisualElement devToolsSection;
        private VisualElement rpcUrlRow;
        private VisualElement explorerUrlRow;
        private VisualElement nftExplorerRow;
        private VisualElement poaUrlRow;
        private VisualElement nexusNameRow;
        private VisualElement devNoValidationRow;
        private VisualElement useVmTransactionsRow;
        private VisualElement showUnstableToolsRow;
        private VisualElement scriptlessGasRow;
        private VisualElement scriptlessDataRow;
        private Button deleteEverythingButton;
        private Button stakingInfoButton;
        private Button addressInfoButton;
        private Button debugNftButton;
        private VisualElement tabsBar;
        private VisualElement tabContent;
        private readonly Dictionary<string, VisualElement> tabs = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, Button> tabButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, string> tabLabels = new Dictionary<string, string>();
        private readonly List<string> tabOrder = new List<string>();
        private string activeTabKey;
        private VisualElement generalSection;
        private VisualElement endpointsSection;
        private VisualElement feesSection;
        private VisualElement performanceSection;
        private VisualElement advancedSection;
        private VisualElement actionsContainer;
        private VisualElement scrollWrapper;
        private VisualElement bodyContainer;

        // Modal UI
        private readonly WalletUiModalHost modalHost;

        private VisualElement copyPanel;
        private Label copyPanelTitle;
        private Label copyPanelCaption;
        private TextField copyPanelValueField;
        private string copyPanelCopyStatus;

        private bool isPopulating;

        public WalletSettingsView(VisualElement host, WalletApplicationContext context, WalletUiModalHost modalHost, Action onExit, Func<string, string, Task<ValidationResult>> onOpenDebugNft = null)
        {
            presenter = context.SettingsPresenter ?? throw new ArgumentNullException(nameof(context.SettingsPresenter));
            actions = context.SettingsActions ?? throw new ArgumentNullException(nameof(context.SettingsActions));
            viewState = presenter.State ?? new WalletSettingsViewState();
            this.onExit = onExit ?? throw new ArgumentNullException(nameof(onExit));
            this.onOpenDebugNft = onOpenDebugNft;
            this.modalHost = modalHost ?? throw new ArgumentNullException(nameof(modalHost));

            BuildLayout(host ?? throw new ArgumentNullException(nameof(host)));
            Refresh();
        }

        public void Dispose()
        {
            HideModal();
            WalletUiCommon.UnblockTabNavigation(root, tabBlockHandler);
            tabBlockHandler = null;
        }

        public void OnAccountsReady()
        {
            Refresh();
        }

        public void MarkAsActive()
        {
        }

        public void Refresh()
        {
            WalletSettingsViewSnapshot snapshot;
            try
            {
                snapshot = presenter.BuildSnapshot();
            }
            catch (Exception e)
            {
                SetStatus($"Settings not available: {e.Message}", true);
                return;
            }

            isPopulating = true;
            UpdateHeaderTexts(snapshot);
            PopulateControls(snapshot);
            isPopulating = false;
            viewState.ScrollY = 0;
            scrollView.scrollOffset = new UnityEngine.Vector2(scrollView.scrollOffset.x, 0f);
            LogScrollState("refresh-end");
            SetStatus(string.Empty);
        }

        private void UpdateHeaderTexts(WalletSettingsViewSnapshot snapshot)
        {
            if (subHeader == null || snapshot == null)
            {
                return;
            }

            var settings = AccountManager.Instance?.Settings;
            var requiresReconfiguration = settings?.settingRequireReconfiguration ?? false;
            var title = "Settings";
            if (snapshot.NexusKind == NexusKind.Unknown)
            {
                title = "Wallet Setup";
            }
            else if (requiresReconfiguration)
            {
                title = "Wallet Setup (Connection failed)";
            }

            subHeader.SubtitleLabel.text = title;
            WalletUiCommon.ApplyNetworkBadge(subHeader.NetworkLabel, snapshot.NexusName, snapshot.NexusKind);
        }

        private void BuildLayout(VisualElement host)
        {
            root = host;
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1;
            root.style.flexShrink = 1;
            root.style.flexBasis = 0;
            root.style.width = new Length(100, LengthUnit.Percent);
            root.style.height = new Length(100, LengthUnit.Percent);
            root.style.minHeight = 0;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;
            root.style.paddingTop = 16;
            root.style.paddingBottom = 16;
            root.style.alignItems = Align.Stretch;
            root.style.overflow = Overflow.Hidden;
            root.style.backgroundColor = Color.clear;
            WalletUiCommon.ApplyDefaultFont(root);
            tabBlockHandler = WalletUiCommon.BlockTabNavigation(root);

            buildInfoLabel = new Label($"Version was built on: {Info.Instance.BuildTime} UTC")
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 14,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginBottom = 12,
                    marginTop = -22
                }
            };
            WalletUiCommon.ApplyDefaultFont(buildInfoLabel);

            var content = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    width = new Length(100, LengthUnit.Percent),
                    maxWidth = 1680,
                    alignSelf = Align.Center,
                    height = new Length(100, LengthUnit.Percent),
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = 0,
                    minHeight = 0,
                    paddingLeft = 8,
                    paddingRight = 8,
                    overflow = Overflow.Hidden
                }
            };
            WalletUiCommon.ApplyDefaultFont(content);

            headerBlock = WalletUiCommon.BuildHeaderBlock(
                subHeaderSubtitle: "Settings",
                subHeaderLeft: string.Empty,
                rightContent: null,
                middleContent: buildInfoLabel,
                headerMarginBottom: 8f,
                subHeaderMarginTop: 6f,
                subHeaderMarginBottom: 8f);
            subHeader = headerBlock.SubHeader;
            headerBlock.Root.style.flexShrink = 0;
            ApplyBuildInfoLayout(content);
            content.Add(headerBlock.Root);

            statusLabel = WalletUiCommon.CreateStatusLabel();
            statusLabel.style.alignSelf = Align.Center;
            statusLabel.style.width = new Length(100, LengthUnit.Percent);
            statusLabel.style.maxWidth = 1680;
            content.Add(statusLabel);

            bodyContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = 0,
                    minHeight = 0,
                    justifyContent = Justify.FlexStart,
                    alignItems = Align.Stretch
                }
            };
            WalletUiCommon.ApplyDefaultFont(bodyContainer);

            scrollWrapper = WalletUiCommon.BuildScrollContainer(
                out scrollView,
                v => viewState.ScrollY = v,
                shouldBlockWheel: () => false,
                paddingLeft: 0f,
                paddingRight: 0f,
                paddingTop: 0f,
                paddingBottom: 48f,
                marginTop: 12f,
                marginBottom: 0f,
                maxWidth: 1680f,
                alignSelf: Align.Center);
            scrollView.RegisterCallback<GeometryChangedEvent>(_ => OnScrollGeometryChanged());
            scrollView.RegisterCallback<GeometryChangedEvent>(_ => StyleScrollBar(scrollView));
            LogScrollState("init-scrollview");

            BuildForm(scrollView);
            if (tabsBar != null)
            {
                tabsBar.style.alignSelf = Align.Center;
                tabsBar.style.width = new Length(100, LengthUnit.Percent);
                tabsBar.style.maxWidth = 1680;
                tabsBar.style.marginTop = 12;
                tabsBar.style.flexShrink = 0;
                bodyContainer.Add(tabsBar);
            }
            if (warningLabel != null)
            {
                warningLabel.style.alignSelf = Align.Center;
                warningLabel.style.width = new Length(100, LengthUnit.Percent);
                warningLabel.style.maxWidth = 1680;
                bodyContainer.Add(warningLabel);
            }
            bodyContainer.Add(scrollWrapper);
            if (actionsContainer != null)
            {
                actionsContainer.style.marginTop = 6;
                actionsContainer.style.marginBottom = 12;
                bodyContainer.Add(actionsContainer);
            }
            content.Add(bodyContainer);

            content.Add(BuildSettingsFooter());

            root.Add(content);
            BuildModal(root);
        }

        private void ApplyBuildInfoLayout(VisualElement host)
        {
            if (buildInfoLabel == null)
            {
                return;
            }

            const float CompactWidthThreshold = 980f;

            void Apply()
            {
                var compact = WalletUiCommon.IsCompactWidth(host, CompactWidthThreshold);
                buildInfoLabel.style.marginTop = compact ? 0 : -22;
                buildInfoLabel.style.marginBottom = compact ? 6 : 12;
                buildInfoLabel.style.whiteSpace = WhiteSpace.Normal;
            }

            Apply();
            host?.RegisterCallback<GeometryChangedEvent>(_ => Apply());
        }

        private void BuildForm(ScrollView scroll)
        {
            scroll.Clear();

            warningLabel = BuildWarningLabel();
            // Tabs bar must wrap on narrow screens so labels stay readable on mobile.
            tabsBar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexStart,
                    flexWrap = Wrap.Wrap,
                    alignContent = Align.FlexStart,
                    marginTop = 8,
                    marginBottom = 12,
                    width = new Length(100, LengthUnit.Percent)
                }
            };
            WalletUiCommon.ApplyDefaultFont(tabsBar);

            tabContent = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    width = new Length(100, LengthUnit.Percent),
                    maxWidth = 1680,
                    alignSelf = Align.Center,
                    paddingLeft = 14,
                    paddingRight = 14,
                    paddingTop = 12,
                    paddingBottom = 16,
                    marginBottom = 16,
                    minHeight = 520,
                    flexGrow = 0,
                    flexShrink = 0,
                    backgroundColor = WalletUiTheme.PanelBackground
                }
            };
            WalletUiCommon.ApplyDefaultFont(tabContent);
            WalletUiCommon.ApplyCardStyle(tabContent, WalletUiTheme.GetPanelGradientTexture(), WalletUiTheme.RadiusMedium, WalletUiTheme.PanelBackground, WalletUiTheme.CardBorder, WalletUiTheme.HighlightEdge);

            generalSection = WalletUiFormFactory.CreateFormSection(string.Empty);
            currencyDropdown = WalletUiFormFactory.CreateDropdown("Currency", Array.Empty<string>(), 0, idx => OnChanged(() => presenter.SetCurrencyIndex(idx)));
            mnemonicDropdown = WalletUiFormFactory.CreateDropdown("Seed length", Array.Empty<string>(), 0, idx => OnChanged(() => presenter.SetMnemonicIndex(idx)));
            passwordModeDropdown = WalletUiFormFactory.CreateDropdown("Password mode", Array.Empty<string>(), 0, idx => OnChanged(() => presenter.SetPasswordModeIndex(idx)));
            logLevelDropdown = WalletUiFormFactory.CreateDropdown("Log level", Array.Empty<string>(), 0, idx => OnChanged(() => presenter.SetLogLevelIndex(idx)));
            logFolderPathField = WalletUiFormFactory.CreateTextField("Log folder path (optional)", string.Empty, value => OnChanged(() => presenter.SetLogFolderPath(value)));

            generalSection.Add(WalletUiFormFactory.CreateLabeledRow("Currency", currencyDropdown));
            generalSection.Add(WalletUiFormFactory.CreateLabeledRow("Seed length", mnemonicDropdown));
            generalSection.Add(WalletUiFormFactory.CreateLabeledRow("Password mode", passwordModeDropdown));
            generalSection.Add(WalletUiFormFactory.CreateLabeledRow("Log level", logLevelDropdown));
            generalSection.Add(WalletUiFormFactory.CreateLabeledRow("Log folder path", logFolderPathField, "Leave empty to use default log location"));

            endpointsSection = WalletUiFormFactory.CreateFormSection(string.Empty);
            nexusDropdown = WalletUiFormFactory.CreateDropdown("Nexus", Array.Empty<string>(), 0, idx => OnChanged(() => presenter.SetNexusIndex(idx), true));
            rpcUrlField = WalletUiFormFactory.CreateTextField("Phantasma RPC URL", string.Empty, value => OnChanged(() => presenter.SetPhantasmaRpcUrl(value)));
            explorerUrlField = WalletUiFormFactory.CreateTextField("Phantasma Explorer URL", string.Empty, value => OnChanged(() => presenter.SetPhantasmaExplorerUrl(value)));
            nftExplorerUrlField = WalletUiFormFactory.CreateTextField("Phantasma NFT URL", string.Empty, value => OnChanged(() => presenter.SetPhantasmaNftExplorerUrl(value)));
            poaUrlField = WalletUiFormFactory.CreateTextField("Phantasma POA URL", string.Empty, value => OnChanged(() => presenter.SetPhantasmaPoaUrl(value)));
            nexusNameField = WalletUiFormFactory.CreateTextField("Nexus Name", string.Empty, value => OnChanged(() => presenter.SetNexusName(value)));
            rpcUrlRow = WalletUiFormFactory.CreateLabeledRow("Phantasma RPC URL", rpcUrlField);
            explorerUrlRow = WalletUiFormFactory.CreateLabeledRow("Phantasma Explorer URL", explorerUrlField);
            nftExplorerRow = WalletUiFormFactory.CreateLabeledRow("Phantasma NFT URL", nftExplorerUrlField);
            poaUrlRow = WalletUiFormFactory.CreateLabeledRow("Phantasma POA URL", poaUrlField);
            nexusNameRow = WalletUiFormFactory.CreateLabeledRow("Nexus Name", nexusNameField);
            defaultEndpointInfoLabel = new Label("Using default endpoints for the selected network.")
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 12,
                    unityFontStyleAndWeight = FontStyle.Normal,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 6
                }
            };
            WalletUiCommon.ApplyDefaultFont(defaultEndpointInfoLabel);
            endpointsSection.Add(WalletUiFormFactory.CreateLabeledRow("Nexus", nexusDropdown));
            endpointsSection.Add(rpcUrlRow);
            endpointsSection.Add(explorerUrlRow);
            endpointsSection.Add(nftExplorerRow);
            endpointsSection.Add(poaUrlRow);
            endpointsSection.Add(nexusNameRow);
            endpointsSection.Add(defaultEndpointInfoLabel);

            // Chain first, then General to match requested order.
            AddTabSection("chain", "Chain", endpointsSection);
            AddTabSection("general", "General", generalSection);

            feesSection = WalletUiFormFactory.CreateFormSection(string.Empty);
            feePriceField = WalletUiFormFactory.CreateTextField("Phantasma fee price", string.Empty, value => OnChanged(() => presenter.SetFeePrice(value)));
            feeLimitField = WalletUiFormFactory.CreateTextField("Phantasma fee limit", string.Empty, value => OnChanged(() => presenter.SetFeeLimit(value)));
            feesSection.Add(WalletUiFormFactory.CreateLabeledRow("Phantasma fee price", feePriceField));
            feesSection.Add(WalletUiFormFactory.CreateLabeledRow("Phantasma fee limit", feeLimitField));
            scriptlessGasField = WalletUiFormFactory.CreateTextField("Scriptless max gas", string.Empty, value => OnChanged(() => presenter.SetScriptlessMaxGas(value)));
            scriptlessDataField = WalletUiFormFactory.CreateTextField("Scriptless max data", string.Empty, value => OnChanged(() => presenter.SetScriptlessMaxData(value)));
            scriptlessGasRow = WalletUiFormFactory.CreateLabeledRow("Scriptless max gas", scriptlessGasField);
            scriptlessDataRow = WalletUiFormFactory.CreateLabeledRow("Scriptless max data", scriptlessDataField);
            feesSection.Add(scriptlessGasRow);
            feesSection.Add(scriptlessDataRow);
            AddTabSection("fees", "Fees", feesSection);

            performanceSection = WalletUiFormFactory.CreateFormSection(string.Empty);
            framerateField = WalletUiFormFactory.CreateTextField("UI framerate (-1 for default)", string.Empty, value => OnChanged(() => presenter.SetUiFramerate(value)));
            uiScaleMultiplierField = WalletUiFormFactory.CreateTextField("UI scale multiplier (1 = default)", string.Empty, value => OnChanged(() => presenter.SetUiScaleMultiplier(value)));
#if UITK_DESKTOP_PREVIEW_SUPPORTED
            previewDeviceDropdown = WalletUiFormFactory.CreateDropdown("Preview device (desktop)", Array.Empty<string>(), 0, idx => OnChanged(() => presenter.SetUiPreviewDeviceIndex(idx), true));
#endif
            windowWidthField = WalletUiFormFactory.CreateTextField("Initial window width", string.Empty, value => OnChanged(() => presenter.SetInitialWindowWidth(value)));
            windowHeightField = WalletUiFormFactory.CreateTextField("Initial window height", string.Empty, value => OnChanged(() => presenter.SetInitialWindowHeight(value)));
            balanceThresholdField = WalletUiFormFactory.CreateTextField("Balance display threshold", string.Empty, value => OnChanged(() => presenter.SetBalanceDisplayThreshold(value)));
            balancePrecisionField = WalletUiFormFactory.CreateTextField("Balance display precision", string.Empty, value => OnChanged(() => presenter.SetBalanceDisplayPrecision(value)));
            performanceSection.Add(WalletUiFormFactory.CreateLabeledRow("Balance display threshold", balanceThresholdField));
            performanceSection.Add(WalletUiFormFactory.CreateLabeledRow("Balance display precision", balancePrecisionField));
            performanceSection.Add(WalletUiFormFactory.CreateLabeledRow("UI framerate (-1 for default)", framerateField));
            performanceSection.Add(WalletUiFormFactory.CreateLabeledRow("UI scale multiplier (1 = default)", uiScaleMultiplierField, "Scales UITK UI size on all platforms"));
#if UITK_DESKTOP_PREVIEW_SUPPORTED
            performanceSection.Add(WalletUiFormFactory.CreateLabeledRow("Preview device (desktop)", previewDeviceDropdown, "Emulates phone/tablet viewport on desktop only"));
#endif
            performanceSection.Add(WalletUiFormFactory.CreateLabeledRow("Initial window width", windowWidthField));
            performanceSection.Add(WalletUiFormFactory.CreateLabeledRow("Initial window height", windowHeightField));
            AddTabSection("display", "Display", performanceSection);

            advancedSection = WalletUiFormFactory.CreateFormSection(string.Empty);
            logOverwriteToggle = WalletUiFormFactory.CreateToggle("Log overwrite mode", false, value => OnChanged(() => presenter.SetLogOverwriteMode(value)));
            devModeToggle = WalletUiFormFactory.CreateToggle("Developer mode", false, value => OnChanged(() => presenter.SetDevMode(value), true));
            devNoValidationToggle = WalletUiFormFactory.CreateToggle("Developer mode (no validation)", false, value => OnChanged(() => presenter.SetDevModeNoValidation(value)));
            useVmTransactionsToggle = WalletUiFormFactory.CreateToggle("Use VM transactions", false, value => OnChanged(() => presenter.SetUseVmTransactions(value)));
            showUnstableToolsToggle = WalletUiFormFactory.CreateToggle(
                "Show unstable tools",
                false,
                value => OnChanged(() =>
                {
                    presenter.SetShowUnstableTools(value);
                    UpdateUnstableToolVisibility(devModeToggle?.value ?? false, value);
                }));
            advancedSection.Add(WalletUiFormFactory.CreateLabeledRow(string.Empty, logOverwriteToggle));
            advancedSection.Add(WalletUiFormFactory.CreateLabeledRow(string.Empty, devModeToggle));
            devNoValidationRow = WalletUiFormFactory.CreateLabeledRow(string.Empty, devNoValidationToggle);
            useVmTransactionsRow = WalletUiFormFactory.CreateLabeledRow(string.Empty, useVmTransactionsToggle);
            showUnstableToolsRow = WalletUiFormFactory.CreateLabeledRow(string.Empty, showUnstableToolsToggle);
            advancedSection.Add(devNoValidationRow);
            advancedSection.Add(useVmTransactionsRow);
            advancedSection.Add(showUnstableToolsRow);
            AddTabSection("advanced", "Advanced", advancedSection);

            BuildTabsBar();
            tabContent.style.marginTop = 10;
            scroll.Add(tabContent);
            if (EnableScrollDebugLog)
            {
                scroll.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => WalletUiCommon.LogScrollState("settings-content-container-geometry", scrollView, root, scrollWrapper));
                tabContent.RegisterCallback<GeometryChangedEvent>(_ => WalletUiCommon.LogScrollState("settings-form-geometry", scrollView, root, scrollWrapper));
            }

            actionsContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    width = new Length(100, LengthUnit.Percent),
                    maxWidth = 1680,
                    alignSelf = Align.Center,
                    flexShrink = 0,
                    marginTop = 6,
                    marginBottom = 12
                }
            };
            WalletUiCommon.ApplyDefaultFont(actionsContainer);

            stakingInfoButton = WalletUiCommon.CreateSecondaryButton("Staking info", OnStakingInfo, 14, 32);
            addressInfoButton = WalletUiCommon.CreateSecondaryButton("Address info", () => OnAddressInfoAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Address info failed: {ex}")), 14, 32);
            debugNftButton = WalletUiCommon.CreateSecondaryButton("View NFT", () => OnOpenDebugNftAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Debug NFT open failed: {ex}")), 14, 32);
            var describeScriptBtn = WalletUiCommon.CreateSecondaryButton("Describe script", () => OnDescribeScriptAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Describe script failed: {ex}")), 14, 32);
            var decodeTxBtn = WalletUiCommon.CreateSecondaryButton("Decode tx", () => OnDecodeTransactionAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Decode transaction failed: {ex}")), 14, 32);
            var verifyPoaBtn = WalletUiCommon.CreateSecondaryButton("Verify POA", () => OnVerifyProofOfAddressesAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Verify POA failed: {ex}")), 14, 32);
            var legacySeedBtn = WalletUiCommon.CreateSecondaryButton("Old seed to WIF", () => OnLegacySeedToWifAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Legacy seed conversion failed: {ex}")), 14, 32);
            var devToolsRowLocal = WalletUiCommon.CreateButtonRow(8f, stakingInfoButton, addressInfoButton, debugNftButton, describeScriptBtn, decodeTxBtn, verifyPoaBtn, legacySeedBtn);
            devToolsSection = devToolsRowLocal;
            actionsContainer.Add(devToolsRowLocal);

            var clearCacheBtn = WalletUiCommon.CreateSecondaryButton("Clear cache", () => ConfirmDeleteAsync(actions.ClearCacheConfirmation, OnClearCache).Forget(ex => Log.WriteWarning($"{LogPrefix}Clear cache confirm failed: {ex}")), 14, 32);
            var resetNotificationsBtn = WalletUiCommon.CreateSecondaryButton("Reset notifications", OnResetNotifications, 14, 32);
            var resetSettingsBtn = WalletUiCommon.CreateSecondaryButton("Reset settings", () => ConfirmDeleteAsync(actions.ResetSettingsConfirmation, OnResetSettings).Forget(ex => Log.WriteWarning($"{LogPrefix}Reset settings confirm failed: {ex}")), 14, 32);
            deleteEverythingButton = WalletUiCommon.CreateSecondaryButton("Delete everything", () => ConfirmDeleteAsync(actions.DeleteEverythingConfirmation, OnDeleteEverything).Forget(ex => Log.WriteWarning($"{LogPrefix}Delete everything confirm failed: {ex}")), 14, 32);
            var utilitiesRowLocal = WalletUiCommon.CreateButtonRow(8f, clearCacheBtn, resetNotificationsBtn, resetSettingsBtn, deleteEverythingButton);
            actionsContainer.Add(utilitiesRowLocal);

            ApplyDebugLayout();
        }

        private static Label BuildWarningLabel()
        {
            var label = new Label
            {
                text = string.Empty,
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 13,
                    unityTextAlign = TextAnchor.UpperLeft,
                    marginBottom = 10,
                    marginTop = 4,
                    display = DisplayStyle.None,
                    whiteSpace = WhiteSpace.Normal,
                    backgroundColor = WalletUiTheme.ScreenGlassStrong,
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 8,
                    paddingBottom = 8,
                    borderLeftWidth = 3,
                    borderLeftColor = WalletUiTheme.AccentPrimary,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall,
                    flexShrink = 0
                }
            };
            WalletUiCommon.ApplyDefaultFont(label);
            return label;
        }

        private void ApplyDebugLayout()
        {
            if (!ShowOnlyFirstSettingsField)
            {
                ActivateTab(string.IsNullOrEmpty(activeTabKey) ? "general" : activeTabKey);
                return;
            }

            HideChildrenAfterFirstField(generalSection);
            HideElement(endpointsSection);
            HideElement(feesSection);
            HideElement(performanceSection);
            HideElement(advancedSection);
            MakeFirstFieldInline(generalSection);
        }

        private static void HideChildrenAfterFirstField(VisualElement section)
        {
            if (section == null)
            {
                return;
            }

            for (var i = 0; i < section.childCount; i++)
            {
                if (i <= 1)
                {
                    continue;
                }

                var child = section[i];
                if (child != null)
                {
                    child.style.display = DisplayStyle.None;
                }
            }
        }

        private static void HideElement(VisualElement element)
        {
            if (element != null)
            {
                element.style.display = DisplayStyle.None;
            }
        }

        private static void MakeFirstFieldInline(VisualElement section)
        {
            if (section == null || section.childCount < 2)
            {
                return;
            }

            var row = section[1] as VisualElement;
            if (row == null)
            {
                return;
            }

            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 6;

            if (row.childCount > 0 && row[0] is Label label)
            {
                label.style.marginBottom = 0;
                label.style.marginRight = 8;
                label.style.minWidth = 120;
                label.style.width = new Length(150, LengthUnit.Pixel);
                label.style.flexShrink = 0;
            }

            if (row.childCount > 1 && row[1] is VisualElement field)
            {
                field.style.width = new StyleLength(StyleKeyword.Auto);
                field.style.flexGrow = 1;
                field.style.marginBottom = 0;
                field.style.marginTop = 0;
            }
        }

        private void AddTabSection(string key, string label, VisualElement section)
        {
            if (string.IsNullOrWhiteSpace(key) || section == null)
            {
                return;
            }

            if (!tabs.TryGetValue(key, out var container))
            {
                container = new VisualElement
                {
                    style =
                    {
                        flexDirection = FlexDirection.Column,
                        width = new Length(100, LengthUnit.Percent),
                        alignSelf = Align.Stretch
                    }
                };
                WalletUiCommon.ApplyDefaultFont(container);
                tabs[key] = container;
                tabLabels[key] = label ?? key;
                tabOrder.Add(key);
                tabContent.Add(container);
            }

            container.Add(section);
        }

        private void AddTabSection(string key, VisualElement section)
        {
            var label = tabLabels.TryGetValue(key, out var existing) ? existing : key;
            AddTabSection(key, label, section);
        }

        private void BuildTabsBar()
        {
            tabsBar.Clear();
            tabButtons.Clear();
            foreach (var key in tabOrder)
            {
                if (!tabLabels.TryGetValue(key, out var label))
                {
                    continue;
                }

                var btn = new Button(() => ActivateTab(key))
                {
                    text = label,
                    style =
                    {
                        color = WalletUiTheme.TextPrimary,
                        unityFontStyleAndWeight = FontStyle.Bold,
                        fontSize = 15,
                        paddingLeft = 14,
                        paddingRight = 14,
                        paddingTop = 8,
                        paddingBottom = 8,
                        minHeight = 36,
                        minWidth = 108,
                        marginRight = 8,
                        marginBottom = 8
                    }
                };
                WalletUiCommon.ApplyDefaultFont(btn);
                WalletUiCommon.ApplyCardStyle(btn, null, WalletUiTheme.RadiusSmall);
                btn.focusable = false;
                btn.tabIndex = -1;
                btn.pickingMode = PickingMode.Position;
                btn.style.unityTextAlign = TextAnchor.MiddleCenter;
                btn.style.whiteSpace = WhiteSpace.NoWrap;
                btn.style.flexShrink = 0;
                tabButtons[key] = btn;
                tabsBar.Add(btn);
            }

            if (string.IsNullOrEmpty(activeTabKey) && tabOrder.Count > 0)
            {
                ActivateTab(tabOrder[0]);
            }
            else
            {
                ActivateTab(activeTabKey);
            }
        }

        private void ActivateTab(string key)
        {
            if (string.IsNullOrEmpty(key) || !tabs.ContainsKey(key))
            {
                return;
            }

            activeTabKey = key;
            foreach (var kv in tabs)
            {
                kv.Value.style.display = kv.Key == key ? DisplayStyle.Flex : DisplayStyle.None;
            }

            foreach (var kv in tabButtons)
            {
                var isActive = kv.Key == key;
                kv.Value.style.backgroundColor = isActive ? WalletUiTheme.SecondaryButton : WalletUiTheme.CardBackground;
                kv.Value.style.color = isActive ? Color.white : WalletUiTheme.TextPrimary;
                var border = isActive ? WalletUiTheme.SecondaryButtonBorder : WalletUiTheme.CardBorder;
                kv.Value.style.borderLeftColor = border;
                kv.Value.style.borderRightColor = border;
                kv.Value.style.borderTopColor = isActive ? WalletUiTheme.SecondaryButtonBorder : WalletUiTheme.HighlightEdge;
                kv.Value.style.borderBottomColor = border;
            }
        }

        private VisualElement BuildSettingsFooter()
        {
            return WalletUiCommon.BuildFooter(
                out _,
                ("View", OnCopyDisplaySettings),
                ("Log folder", OnShowLogLocation),
                ("Revert", OnCancelChanges),
                ("Apply", OnApplySettings)
            );
        }

        private void PopulateControls(WalletSettingsViewSnapshot snapshot)
        {
            SetDropdown(currencyDropdown, snapshot.CurrencyOptions, snapshot.CurrencyIndex);
            SetDropdown(nexusDropdown, snapshot.NexusDisplayOptions, snapshot.NexusIndex);
            SetDropdown(mnemonicDropdown, snapshot.MnemonicDisplayOptions, snapshot.MnemonicIndex);
            SetDropdown(passwordModeDropdown, snapshot.PasswordDisplayOptions, snapshot.PasswordModeIndex);
            SetDropdown(logLevelDropdown, snapshot.LogLevelDisplayOptions, snapshot.LogLevelIndex);
#if UITK_DESKTOP_PREVIEW_SUPPORTED
            SetDropdown(previewDeviceDropdown, snapshot.UiPreviewDeviceDisplayOptions, snapshot.UiPreviewDeviceIndex);
#endif
            logFolderPathField.value = snapshot.LogFolderPath ?? string.Empty;

            rpcUrlField.value = snapshot.PhantasmaRpcUrl ?? string.Empty;
            explorerUrlField.value = snapshot.PhantasmaExplorerUrl ?? string.Empty;
            nftExplorerUrlField.value = snapshot.PhantasmaNftExplorerUrl ?? string.Empty;
            poaUrlField.value = snapshot.PhantasmaPoaUrl ?? string.Empty;
            nexusNameField.value = snapshot.NexusName ?? string.Empty;

            feePriceField.value = snapshot.FeePriceText ?? string.Empty;
            feeLimitField.value = snapshot.FeeLimitText ?? string.Empty;
            balanceThresholdField.value = snapshot.BalanceDisplayThresholdText ?? string.Empty;
            balancePrecisionField.value = snapshot.BalanceDisplayPrecisionText ?? string.Empty;
            framerateField.value = snapshot.UiFramerateText ?? string.Empty;
            uiScaleMultiplierField.value = snapshot.UiScaleMultiplierText ?? string.Empty;
            windowWidthField.value = snapshot.InitialWindowWidthText ?? string.Empty;
            windowHeightField.value = snapshot.InitialWindowHeightText ?? string.Empty;
            scriptlessGasField.value = snapshot.ScriptlessMaxGasText ?? string.Empty;
            scriptlessDataField.value = snapshot.ScriptlessMaxDataText ?? string.Empty;

            logOverwriteToggle.value = snapshot.LogOverwriteMode;
            devModeToggle.value = snapshot.DevMode;
            devNoValidationToggle.value = snapshot.DevModeNoValidation;
            useVmTransactionsToggle.value = snapshot.UseVmTransactions;
            showUnstableToolsToggle.value = snapshot.ShowUnstableTools;

            if (!ShowOnlyFirstSettingsField)
            {
                if (rpcUrlRow != null)
                {
                    rpcUrlRow.style.display = snapshot.HasCustomEndpoints ? DisplayStyle.Flex : DisplayStyle.None;
                    rpcUrlField.style.display = snapshot.HasCustomEndpoints ? DisplayStyle.Flex : DisplayStyle.None;
                }

                if (explorerUrlRow != null)
                {
                    explorerUrlRow.style.display = snapshot.HasCustomEndpoints ? DisplayStyle.Flex : DisplayStyle.None;
                    explorerUrlField.style.display = snapshot.HasCustomEndpoints ? DisplayStyle.Flex : DisplayStyle.None;
                }

                if (nftExplorerRow != null)
                {
                    nftExplorerRow.style.display = snapshot.HasCustomEndpoints ? DisplayStyle.Flex : DisplayStyle.None;
                    nftExplorerUrlField.style.display = snapshot.HasCustomEndpoints ? DisplayStyle.Flex : DisplayStyle.None;
                }

                if (poaUrlRow != null)
                {
                    poaUrlRow.style.display = snapshot.HasCustomEndpoints ? DisplayStyle.Flex : DisplayStyle.None;
                    poaUrlField.style.display = snapshot.HasCustomEndpoints ? DisplayStyle.Flex : DisplayStyle.None;
                }

                if (nexusNameRow != null)
                {
                    nexusNameRow.style.display = snapshot.HasCustomName ? DisplayStyle.Flex : DisplayStyle.None;
                    nexusNameField.style.display = snapshot.HasCustomName ? DisplayStyle.Flex : DisplayStyle.None;
                }

                if (defaultEndpointInfoLabel != null)
                {
                    defaultEndpointInfoLabel.style.display = snapshot.HasCustomEndpoints ? DisplayStyle.None : DisplayStyle.Flex;
                }
            }

            ToggleDevVisibility(snapshot.DevMode, snapshot.ShowUnstableTools);

            if (deleteEverythingButton != null)
            {
                var am = AccountManager.Instance;
                var accountCount = am?.Accounts?.Count ?? 0;
                deleteEverythingButton.SetEnabled(accountCount > 0);
            }

            if (warningLabel != null)
            {
                warningLabel.text = snapshot.ShouldShowNetworkWarning ? "WARNING: Non-main network; use only for development/testing." : string.Empty;
                warningLabel.style.display = snapshot.ShouldShowNetworkWarning ? DisplayStyle.Flex : DisplayStyle.None;
            }

            ApplyDebugLayout();
        }

        private void ToggleDevVisibility(bool devMode, bool showUnstableTools)
        {
            if (ShowOnlyFirstSettingsField)
            {
                if (devToolsSection != null)
                {
                    devToolsSection.style.display = devMode ? DisplayStyle.Flex : DisplayStyle.None;
                }
                UpdateUnstableToolVisibility(devMode, showUnstableTools);
                return;
            }

            if (devNoValidationRow != null)
            {
                devNoValidationRow.style.display = devMode ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (useVmTransactionsRow != null)
            {
                useVmTransactionsRow.style.display = devMode ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (showUnstableToolsRow != null)
            {
                showUnstableToolsRow.style.display = devMode ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (scriptlessGasRow != null)
            {
                scriptlessGasRow.style.display = devMode ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (scriptlessDataRow != null)
            {
                scriptlessDataRow.style.display = devMode ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (devToolsSection != null)
            {
                devToolsSection.style.display = devMode ? DisplayStyle.Flex : DisplayStyle.None;
            }

            UpdateUnstableToolVisibility(devMode, showUnstableTools);
        }

        // Keep unstable dev tools hidden unless explicitly enabled in dev mode.
        private void UpdateUnstableToolVisibility(bool devMode, bool showUnstableTools)
        {
            var show = devMode && showUnstableTools;

            if (stakingInfoButton != null)
            {
                stakingInfoButton.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (addressInfoButton != null)
            {
                addressInfoButton.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void SetDropdown(PopupField<string> field, IReadOnlyList<string> options, int index)
        {
            field.choices = options == null ? new List<string>() : new List<string>(options);
            field.index = Mathf.Clamp(index, 0, Math.Max(0, (options?.Count ?? 1) - 1));
        }

        private void OnClearCache()
        {
            actions.ClearCache();
            SetStatus(actions.ClearCacheSuccess, intent: WalletUiStatusIntent.TransientLong);
        }

        private void OnResetNotifications()
        {
            var result = actions.ResetNotifications();
            if (result.Success)
            {
                SetStatus(actions.ResetNotificationsSuccess, intent: WalletUiStatusIntent.TransientLong);
            }
            else
            {
                SetStatus(result.Error, true);
            }
        }

        private void OnResetSettings()
        {
            var result = actions.ResetSettingsToDefaults();
            if (!result.Success)
            {
                SetStatus(result.Error, true);
                return;
            }

            presenter.ResetStateFromSettings();
            Refresh();
            SetStatus(actions.ResetSettingsSuccess, intent: WalletUiStatusIntent.TransientLong);
        }

        private void OnDeleteEverything()
        {
            var result = actions.DeleteEverything();
            if (!result.Success)
            {
                SetStatus(result.Error, true);
                return;
            }

            SetStatus(actions.DeleteEverythingSuccess, intent: WalletUiStatusIntent.TransientLong);
            onExit?.Invoke();
        }

        private void OnCopyDisplaySettings()
        {
            var settingsText = actions.GetDisplaySettings();
            ShowCopyPanel("Display Settings", "Copy display settings text below", settingsText ?? string.Empty, "Settings copied to clipboard.");
        }

        private void OnShowLogLocation()
        {
            var path = actions.GetLogFolderPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                SetStatus("Log file path is not available.", true);
                return;
            }

            var platform = Application.platform;
            if (platform == RuntimePlatform.WindowsPlayer || platform == RuntimePlatform.WindowsEditor || platform == RuntimePlatform.OSXPlayer || platform == RuntimePlatform.OSXEditor)
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                    SetStatus("Opening log folder...", intent: WalletUiStatusIntent.TransientShort);
                    return;
                }
                catch (Exception e)
                {
                    Log.WriteWarning($"{LogPrefix}Failed to open log folder: {e}");
                }
            }

            ShowCopyPanel("Log file path", "Copy log folder path", path, "Log path copied.");
        }

        private void OnApplySettings()
        {
            var ok = presenter.ValidateAndApply(error =>
            {
                SetStatus(error, true);
                ShowInfoAsync("Validation error", error).Forget(ex => Log.WriteWarning($"{LogPrefix}Validation dialog failed: {ex}"));
            });

            if (ok)
            {
                var am = AccountManager.Instance;
                if (am?.Settings != null)
                {
                    am.Settings.settingRequireReconfiguration = false;
                }

                WalletUiToolkitRoot.RefreshPanelScale();
                WalletApplicationContext.Instance?.ViewState?.ResetSnapshots();
                WalletApplicationContext.Instance?.UiSignals?.RaiseSettingsChanged();
                SetStatus("Settings applied.", intent: WalletUiStatusIntent.TransientLong);
                ExitToMain();
            }
        }

        private void OnCancelChanges()
        {
            var am = AccountManager.Instance;
            if (am?.Settings == null)
            {
                SetStatus("Account manager is not ready.", true);
                return;
            }

            am.Settings.Load();
            presenter.ResetStateFromSettings();
            SetStatus("Changes reverted.", intent: WalletUiStatusIntent.TransientLong);
            ExitToMain();
        }

        private async Task OnVerifyProofOfAddressesAsync()
        {
            var (result, input) = await ShowModalAsync("Verify proof of addresses", actions.ProofOfAddressesPrompt, 1, -1, allowEmpty: false, hasInput: true, multiline: true);
            if (result != PromptResult.Success)
            {
                return;
            }

            var verifyResult = actions.VerifyProofOfAddresses(input, devModeToggle?.value ?? false);
            if (!verifyResult.Success)
            {
                SetStatus(verifyResult.Error, true);
                await ShowInfoAsync("Verification failed", verifyResult.Error);
                return;
            }

            await ShowInfoAsync("Verification", verifyResult.Data);
            SetStatus("Proof of addresses verified.", intent: WalletUiStatusIntent.TransientLong);
        }

        private async Task OnLegacySeedToWifAsync()
        {
            var seedPrompt = await ShowModalAsync("Old seed to WIF", actions.LegacySeedPrompt, 1, -1, allowEmpty: false, hasInput: true, isPassword: false, multiline: true);
            if (seedPrompt.result != PromptResult.Success || string.IsNullOrWhiteSpace(seedPrompt.input))
            {
                return;
            }

            var passwordPrompt = await ShowModalAsync("Legacy seed password", actions.LegacySeedPasswordPrompt, 0, 64, allowEmpty: true, hasInput: true, isPassword: true);
            if (passwordPrompt.result != PromptResult.Success)
            {
                return;
            }

            var conversionResult = actions.ConvertLegacySeedToWif(seedPrompt.input, passwordPrompt.input);
            if (!conversionResult.Success)
            {
                SetStatus(conversionResult.Error, true);
                await ShowInfoAsync("Conversion failed", conversionResult.Error);
                return;
            }

            ShowCopyPanel("WIF", "Copy the generated WIF", conversionResult.Data, "WIF copied to clipboard.");
            SetStatus("WIF generated.", intent: WalletUiStatusIntent.TransientLong);
        }

        private async Task OnDescribeScriptAsync()
        {
            var modalResult = await ShowModalAsync("Transaction script", "Enter transaction script in Base16 encoding", 1, -1, allowEmpty: false, hasInput: true, isPassword: false, multiline: true);
            if (modalResult.result != PromptResult.Success)
            {
                return;
            }

            byte[] script;
            try
            {
                script = Base16.Decode((modalResult.input ?? string.Empty).CleanHex(), false);
            }
            catch (Exception e)
            {
                SetStatus($"Cannot parse script: {e.Message}", true);
                return;
            }

            if (script == null)
            {
                SetStatus("Cannot parse script.", true);
                return;
            }

            SetStatus("Parsing script...");
            try
            {
                var (description, error) = await DescriptionUtils.GetDescriptionAsync(script, true, CancellationToken.None);
                if (!string.IsNullOrEmpty(error))
                {
                    SetStatus("Error during script parsing.", true);
                    await ShowInfoAsync("Script error", "Error during script parsing.\nDetails: " + error);
                    return;
                }

                ShowCopyPanel("Script description", "Copy the generated description", description, "Description copied to clipboard.");
                SetStatus("Script parsed.", intent: WalletUiStatusIntent.TransientLong);
            }
            catch (Exception e)
            {
                SetStatus("Error during script parsing.", true);
                await ShowInfoAsync("Script error", "Error during script parsing.\nDetails: " + e);
            }
        }

        private async Task OnDecodeTransactionAsync()
        {
            var modalResult = await ShowModalAsync("Decode transaction", "Enter transaction in Base16 encoding", 1, -1, allowEmpty: false, hasInput: true, isPassword: false, multiline: true);
            if (modalResult.result != PromptResult.Success)
            {
                return;
            }

            PhantasmaPhoenix.Protocol.Transaction tx = null;
            try
            {
                tx = PhantasmaPhoenix.Protocol.Transaction.Unserialize(Base16.Decode((modalResult.input ?? string.Empty).CleanHex(), false));
            }
            catch (Exception e)
            {
                SetStatus("Cannot parse transaction.", true);
                await ShowInfoAsync("Decode failed", $"Cannot parse transaction.\nDetails: {e}");
                return;
            }

            if (tx == null)
            {
                SetStatus("Cannot parse transaction.", true);
                return;
            }

            SetStatus("Parsing transaction...");
            try
            {
                var (description, error) = await DescriptionUtils.GetDescriptionAsync(tx.Script, true, CancellationToken.None);
                if (!string.IsNullOrEmpty(error))
                {
                    SetStatus("Error during tx parsing.", true);
                    await ShowInfoAsync("Decode failed", "Error during tx parsing.\nDetails: " + error);
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine($"Nexus name: {tx.NexusName}");
                sb.AppendLine($"Chain name: {tx.ChainName}");
                sb.AppendLine($"Expiration: {tx.Expiration}");
                sb.AppendLine($"Payload: {Encoding.UTF8.GetString(tx.Payload)}");
                sb.AppendLine($"Hash: {tx.Hash}");
                sb.AppendLine($"Signatures count: {(tx.HasSignatures ? tx.Signatures.Length : 0)}");
                if (tx.HasSignatures)
                {
                    sb.AppendLine("Signatures:");
                    foreach (var s in tx.Signatures)
                    {
                        sb.AppendLine(s.ToString());
                    }
                }
                sb.AppendLine().Append(description);

                ShowCopyPanel("Tx description", "Copy the decoded transaction info", sb.ToString(), "Transaction description copied.");
                SetStatus("Transaction decoded.", intent: WalletUiStatusIntent.TransientLong);
            }
            catch (Exception e)
            {
                SetStatus("Error during tx parsing.", true);
                await ShowInfoAsync("Decode failed", "Error during tx parsing.\nDetails: " + e);
            }
        }

        private void OnStakingInfo()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                SetStatus("Account manager is not available.", true);
                return;
            }

            byte[] scriptMasterClaimDate;
            byte[] scriptMasterCount;
            byte[] scriptClaimMasterCount;
            byte[] scriptMasterThreshold;
            try
            {
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetMasterClaimDate", 1);
                    scriptMasterClaimDate = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetMasterCount");
                    scriptMasterCount = sb.EndScript();
                }
                {
                    var sb = new ScriptBuilder();
                    sb.CallContract("stake", "GetMasterThreshold");
                    scriptMasterThreshold = sb.EndScript();
                }
            }
            catch (Exception e)
            {
                ShowInfoAsync("Something went wrong", "Something went wrong!\n" + e.Message + "\n\n" + e.StackTrace).Forget(ex => Log.WriteWarning($"{LogPrefix}ShowInfo failed: {ex}"));
                return;
            }

            SetStatus("Requesting staking info...");
            accountManager.InvokeScriptPhantasma("main", scriptMasterClaimDate, (masterClaimDateResult, masterClaimInvokeError) =>
            {
                if (!string.IsNullOrEmpty(masterClaimInvokeError))
                {
                    ShowInfoAsync("Script invocation error", "Script invocation error!\n\n" + masterClaimInvokeError).Forget(ex => Log.WriteWarning($"{LogPrefix}Staking info dialog failed: {ex}"));
                    SetStatus("Staking info failed.", true);
                    return;
                }

                var sbClaim = new ScriptBuilder();
                sbClaim.CallContract("stake", "GetClaimMasterCount", VMObject.FromBytes(masterClaimDateResult).AsTimestamp());
                scriptClaimMasterCount = sbClaim.EndScript();

                accountManager.InvokeScriptPhantasma("main", scriptClaimMasterCount, (claimMasterCountResult, claimMasterCountInvokeError) =>
                {
                    if (!string.IsNullOrEmpty(claimMasterCountInvokeError))
                    {
                        ShowInfoAsync("Script invocation error", "Script invocation error!\n\n" + claimMasterCountInvokeError).Forget(ex => Log.WriteWarning($"{LogPrefix}Staking info dialog failed: {ex}"));
                        SetStatus("Staking info failed.", true);
                        return;
                    }

                    accountManager.InvokeScriptPhantasma("main", scriptMasterCount, (masterCountResult, masterCountInvokeError) =>
                    {
                        if (!string.IsNullOrEmpty(masterCountInvokeError))
                        {
                            ShowInfoAsync("Script invocation error", "Script invocation error!\n\n" + masterCountInvokeError).Forget(ex => Log.WriteWarning($"{LogPrefix}Staking info dialog failed: {ex}"));
                            SetStatus("Staking info failed.", true);
                            return;
                        }

                        accountManager.InvokeScriptPhantasma("main", scriptMasterThreshold, (masterThresholdResult, masterThresholdInvokeError) =>
                        {
                            if (!string.IsNullOrEmpty(masterThresholdInvokeError))
                            {
                                ShowInfoAsync("Script invocation error", "Script invocation error!\n\n" + masterThresholdInvokeError).Forget(ex => Log.WriteWarning($"{LogPrefix}Staking info dialog failed: {ex}"));
                                SetStatus("Staking info failed.", true);
                                return;
                            }

                            var masterClaimDate = VMObject.FromBytes(masterClaimDateResult).AsTimestamp();
                            var claimMasterCount = VMObject.FromBytes(claimMasterCountResult).AsNumber();
                            var masterCount = VMObject.FromBytes(masterCountResult).AsNumber();
                            var masterThreshold = WalletAmountFormatter.Format(VMObject.FromBytes(masterThresholdResult).AsNumber(), 8);

                            var message = $"Phantasma staking information:\n\n" +
                                          $"All SMs: {masterCount}\n" +
                                          $"SMs eligible for next rewards distribution: {claimMasterCount}\n" +
                                          $"SM reward prediction: {125000 / claimMasterCount} SOUL\n" +
                                          $"Next SM rewards distribution date: {masterClaimDate}\n" +
                                          $"SM threshold: {masterThreshold} SOUL\n";

                            ShowCopyPanel("Account information", message, message, "Staking info copied.");
                            SetStatus("Staking info fetched.", intent: WalletUiStatusIntent.TransientLong);
                        });
                    });
                });
            });
        }

        private async Task OnAddressInfoAsync()
        {
            var modalResult = await ShowModalAsync("Phantasma address info", "Enter an address", 1, -1, allowEmpty: false, hasInput: true, isPassword: false, multiline: false);
            if (modalResult.result != PromptResult.Success)
            {
                return;
            }

            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                SetStatus("Account manager is not available.", true);
                return;
            }

            var tcs = new TaskCompletionSource<(string info, string error)>(TaskCreationOptions.RunContinuationsAsynchronously);
            accountManager.GetPhantasmaAddressInfo(modalResult.input, null, (info, error) => tcs.TrySetResult((info, error)));
            var (infoText, errorText) = await tcs.Task;
            if (!string.IsNullOrEmpty(errorText))
            {
                await ShowInfoAsync("Error", "Something went wrong!\n" + errorText);
                SetStatus("Failed to get address info.", true);
                return;
            }

            ShowCopyPanel("Account information", infoText, infoText, "Info copied to clipboard.");
            SetStatus("Address info fetched.", intent: WalletUiStatusIntent.TransientLong);
        }

        private async Task OnOpenDebugNftAsync()
        {
            if (onOpenDebugNft == null)
            {
                SetStatus("Debug NFT viewer is not available.", true);
                return;
            }

            var symbolPrompt = await ShowModalAsync("View NFT", "Enter NFT symbol", 1, 32, allowEmpty: false, hasInput: true, isPassword: false, multiline: false);
            if (symbolPrompt.result != PromptResult.Success)
            {
                return;
            }

            var symbol = symbolPrompt.input?.Trim();
            if (string.IsNullOrWhiteSpace(symbol))
            {
                SetStatus("NFT symbol is required.", true);
                return;
            }

            var idPrompt = await ShowModalAsync("View NFT", "Enter NFT token id", 1, 128, allowEmpty: false, hasInput: true, isPassword: false, multiline: false);
            if (idPrompt.result != PromptResult.Success)
            {
                return;
            }

            var tokenId = idPrompt.input?.Trim();
            if (string.IsNullOrWhiteSpace(tokenId))
            {
                SetStatus("NFT token id is required.", true);
                return;
            }

            SetStatus("Loading NFT...");
            var result = await onOpenDebugNft(symbol, tokenId);
            if (!result.Success)
            {
                SetStatus(result.Error, true);
                await ShowInfoAsync("Debug NFT failed", result.Error);
                return;
            }

            SetStatus(string.IsNullOrWhiteSpace(result.Message) ? "Debug NFT opened." : result.Message, intent: WalletUiStatusIntent.TransientLong);
        }

        private async Task ConfirmDeleteAsync(string message, Action onConfirm)
        {
            var result = await WalletUiModalHelper.ShowConfirmAsync(modalHost, "Confirm", message, "Confirm", "Cancel");
            if (result == PromptResult.Success)
            {
                onConfirm?.Invoke();
            }
        }

        private async Task ShowInfoAsync(string title, string message)
        {
            await WalletUiModalHelper.ShowInfoAsync(modalHost, title, message);
        }

        private void SetStatus(string text, bool isError = false, WalletUiStatusIntent intent = WalletUiStatusIntent.None)
        {
            var resolvedIntent = isError ? WalletUiStatusIntent.None : intent;
            WalletUiCommon.UpdateStatusLabel(statusLabel, text, autoHideSeconds: 0f, intent: resolvedIntent);
            statusLabel.style.color = isError ? Color.red : WalletUiTheme.TextSecondary;
        }

        private void OnScrollGeometryChanged()
        {
            LogScrollState("geometry-changed");
        }

        private void LogScrollState(string reason)
        {
            if (!EnableScrollDebugLog || scrollView == null)
            {
                return;
            }

            WalletUiCommon.LogScrollState($"settings-{reason}", scrollView, root, scrollWrapper);

            try
            {
                var sb = new StringBuilder();
                sb.Append(ScrollLogPrefix).Append(reason)
                  .Append($" sectionsH=(")
                  .Append($"gen:{(generalSection?.layout.height ?? 0f):F1},")
                  .Append($"endp:{(endpointsSection?.layout.height ?? 0f):F1},")
                  .Append($"fees:{(feesSection?.layout.height ?? 0f):F1},")
                  .Append($"perf:{(performanceSection?.layout.height ?? 0f):F1},")
                  .Append($"adv:{(advancedSection?.layout.height ?? 0f):F1})")
                  .Append($" actionsH={(actionsContainer?.layout.height ?? 0f):F1}")
                  .Append($" headerH={(headerBlock?.Root?.layout.height ?? 0f):F1}")
                  .Append($" statusH={(statusLabel?.layout.height ?? 0f):F1}")
                  .Append($" warningH={(warningLabel?.layout.height ?? 0f):F1}")
                  .Append($" contentChildren={(scrollView.contentContainer?.childCount ?? 0)}");
                Log.Write(sb.ToString());
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{ScrollLogPrefix}Extra scroll log failed for '{reason}': {e.Message}");
            }
        }

        private static void StyleScrollBar(ScrollView sv)
        {
            if (sv == null || sv.verticalScroller == null)
            {
                return;
            }

            var scroller = sv.verticalScroller;
            scroller.style.visibility = Visibility.Visible;
            scroller.style.width = 12;
            scroller.style.minWidth = 12;
            scroller.style.backgroundColor = new Color(0.08f, 0.1f, 0.15f, 0.6f);

            var slider = scroller.slider;
            if (slider != null)
            {
                slider.style.backgroundColor = new Color(0.35f, 0.45f, 0.6f, 0.9f);
                slider.style.minHeight = 24;
                slider.style.borderTopLeftRadius = WalletUiTheme.RadiusSmall;
                slider.style.borderTopRightRadius = WalletUiTheme.RadiusSmall;
                slider.style.borderBottomLeftRadius = WalletUiTheme.RadiusSmall;
                slider.style.borderBottomRightRadius = WalletUiTheme.RadiusSmall;
            }
        }

        private void OnChanged(Action setter, bool requiresRefresh = false)
        {
            if (isPopulating)
            {
                return;
            }

            setter?.Invoke();
            if (requiresRefresh)
            {
                Refresh();
            }
        }

        private void BuildModal(VisualElement parent)
        {
            copyPanel = WalletUiModalFactory.CreateCopyPanel(OnCopyPanelCopy, HideModal, WalletUiCommon.ApplyDefaultFont, out copyPanelTitle, out copyPanelCaption, out copyPanelValueField);
        }

        private void ApplyResponsiveLayout()
        {
        }

        private Task<(PromptResult result, string input)> ShowModalAsync(string title, string caption, int minLength, int maxLength, bool allowEmpty = false, bool hasInput = true, bool showSecondary = true, string primaryText = "Confirm", bool isPassword = false, string initialValue = "", bool multiline = false)
        {
            var secondaryText = showSecondary ? "Cancel" : "Close";

            return WalletUiModalHelper.ShowPromptAsync(
                modalHost,
                title,
                caption,
                minLength,
                maxLength,
                allowEmpty,
                hasInput,
                isPassword,
                multiline,
                string.IsNullOrWhiteSpace(primaryText) ? "Confirm" : primaryText,
                secondaryText,
                showSecondary,
                initialValue ?? string.Empty,
                successResult: PromptResult.Success,
                cancelResult: PromptResult.Failure);
        }

        private void ShowCopyPanel(string title, string caption, string value, string copyStatus)
        {
            modalHost.HideAll();
            HideCopyPanel();
            copyPanelCopyStatus = copyStatus;

            copyPanelTitle.text = string.IsNullOrWhiteSpace(title) ? "Copy value" : title;
            copyPanelCaption.text = string.IsNullOrWhiteSpace(caption) ? "Copy the value below" : caption;
            copyPanelValueField.value = value ?? string.Empty;
            copyPanelValueField.SetEnabled(true);

            modalHost.ShowPanel(copyPanel);
        }

        private void HideCopyPanel()
        {
            if (copyPanel != null)
            {
                copyPanel.style.display = DisplayStyle.None;
            }

            if (copyPanelValueField != null)
            {
                copyPanelValueField.value = string.Empty;
            }

            copyPanelCopyStatus = null;
        }

        private void OnCopyPanelCopy()
        {
            if (copyPanelValueField != null)
            {
                GUIUtility.systemCopyBuffer = copyPanelValueField.text ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(copyPanelCopyStatus))
            {
                SetStatus(copyPanelCopyStatus, intent: WalletUiStatusIntent.TransientShort);
            }

            HideModal();
        }

        private void HideModal()
        {
            modalHost.HideAll();
            HideCopyPanel();
        }

        private void ExitToMain()
        {
            onExit?.Invoke();
        }
    }
}
