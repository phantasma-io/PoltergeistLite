using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Poltergeist.Wallet;
using Poltergeist.UiToolkit;
using PhantasmaPhoenix.Unity.Core.Logging;
using PhantasmaPhoenix.InteropChains.Legacy.Ethereum;
using PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Hex.HexConvertors.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using Poltergeist;
using TransactionResult = PhantasmaPhoenix.RPC.Models.TransactionResult;

namespace Poltergeist.UiToolkit.Accounts
{
    /// <summary>
    /// Account details screen for UITK (copy address, explorer links, key export).
    /// </summary>
    public sealed class WalletAccountView : IDisposable
    {
        private const string LogPrefix = "[UITK] ";

        private readonly WalletApplicationContext context;
        private readonly WalletAuthService authService;
        private readonly WalletAccountAdminService accountAdminService;
        private readonly WalletFeeRequirement feeRequirement;
        private readonly WalletTransactionOrchestrator transactionOrchestrator;
        private readonly WalletUiTransactionAdapter transactionUi;
        private readonly IWalletAuthUi sharedAuthUi;
        private readonly WalletUiModalHost modalHost;
        private readonly Action onShowBalances;
        private readonly Action onShowHistory;
        private readonly Action onShowAccount;
        private readonly Action onShowSettings;
        private readonly Action onExit;
        private EventCallback<KeyDownEvent> tabBlockHandler;

        private VisualElement root;
        private Label statusLabel;
        private Label subtitleLabel;
        private Label subtitleNetworkLabel;
        private AccountInfoElements accountInfo;
        private Image qrImage;
        private Texture2D qrTexture;
        private Button navBalances;
        private Button navHistory;
        private Button navAccount;
        private Button navExit;
        private Button migrateButton;
        private Button setNameButton;
        private VisualElement chainPickerPanel;
        private VisualElement copyPanel;
        private Label copyPanelTitle;
        private Label copyPanelCaption;
        private TextField copyPanelValueField;
        private string copyPanelCopyStatus;
        private WalletUiTransactionDialogs transactionDialogs;
        private VisualElement verificationPanel;
        private Label verificationMessageLabel;
        private TaskCompletionSource<string> chainPickerTcs;
        private SubHeaderElements subHeader;
        private List<Button> actionButtons = new List<Button>();
        private VisualElement actionRowHost;
        private Button[] actionCloudButtons;
        private bool actionRowResolved;

        public WalletAccountView(VisualElement host, WalletApplicationContext context, WalletUiModalHost modalHost, IWalletAuthUi sharedAuthUi, Action onShowBalances, Action onShowHistory, Action onShowAccount, Action onShowSettings, Action onExit)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            authService = context.AuthService ?? throw new ArgumentNullException(nameof(context.AuthService));
            accountAdminService = context.AccountAdminService ?? throw new ArgumentNullException(nameof(context.AccountAdminService));
            feeRequirement = context.FeeRequirement ?? throw new ArgumentNullException(nameof(context.FeeRequirement));
            transactionUi = new WalletUiTransactionAdapter(
                authService,
                sharedAuthUi ?? throw new ArgumentNullException(nameof(sharedAuthUi)),
                SetStatusText,
                SetActionsEnabled,
                ShowSendProgressAsync,
                StartConfirmationAsync);
            transactionOrchestrator = new WalletTransactionOrchestrator(() => AccountManager.Instance, transactionUi);
            this.sharedAuthUi = sharedAuthUi ?? throw new ArgumentNullException(nameof(sharedAuthUi));
            this.modalHost = modalHost ?? throw new ArgumentNullException(nameof(modalHost));
            this.onShowBalances = onShowBalances ?? throw new ArgumentNullException(nameof(onShowBalances));
            this.onShowHistory = onShowHistory ?? throw new ArgumentNullException(nameof(onShowHistory));
            this.onShowAccount = onShowAccount ?? (() => { });
            this.onShowSettings = onShowSettings ?? throw new ArgumentNullException(nameof(onShowSettings));
            this.onExit = onExit ?? throw new ArgumentNullException(nameof(onExit));

            BuildLayout(host ?? throw new ArgumentNullException(nameof(host)));
            RefreshView();
            UpdateNavSelection();
        }

        public void Dispose()
        {
            ClearQrTexture();
            transactionDialogs?.Dispose();
            WalletUiCommon.UnblockTabNavigation(root, tabBlockHandler);
            tabBlockHandler = null;
        }

        public void OnAccountsReady()
        {
            RefreshView();
            UpdateNavSelection();
        }

        public void MarkAsActive()
        {
            UpdateNavSelection();
            UpdateUnstableActionVisibility(AccountManager.Instance?.Settings);
        }

        public void RefreshView()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null || !accountManager.HasSelection)
            {
                SetStatus("No wallet selected.");
                UpdateLabels(string.Empty, string.Empty, NexusKind.Unknown);
                ClearQrTexture();
                return;
            }

            var accountName = string.IsNullOrWhiteSpace(accountManager.CurrentAccount.name) ? "Wallet" : accountManager.CurrentAccount.name;
            var address = accountManager.CurrentAccount.phaAddress ?? string.Empty;
            var settings = accountManager.Settings;
            var nexusName = settings?.nexusName;
            var nexusKind = settings?.nexusKind ?? NexusKind.Main_Net;

            subtitleLabel.text = WalletUiCommon.BuildContextSubtitle("Account", accountName, AccountManager.Instance?.CurrentPlatform);
            subHeader.LeftLabel.text = string.Empty;
            WalletUiCommon.ApplyNetworkBadge(subtitleNetworkLabel, nexusName, nexusKind);
            UpdateUnstableActionVisibility(settings);
            UpdateLabels(accountName, address, nexusKind, nexusName);
            UpdateQr(accountManager);
            SetStatus(string.Empty);
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
            root.style.backgroundColor = Color.clear;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;
            root.style.paddingTop = 16;
            root.style.paddingBottom = 16;
            root.style.color = WalletUiTheme.TextPrimary;
            root.style.alignItems = Align.Stretch;
            root.style.overflow = Overflow.Hidden;
            root.style.position = Position.Relative;
            ApplyDefaultFont(root);
            tabBlockHandler = WalletUiCommon.BlockTabNavigation(root);

            var content = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    width = new Length(100, LengthUnit.Percent),
                    maxWidth = 1680,
                    alignSelf = Align.Center,
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = 0,
                    minHeight = 0,
                    paddingLeft = 8,
                    paddingRight = 8
                }
            };
            ApplyDefaultFont(content);

            var headerBlock = WalletUiCommon.BuildHeaderBlock(
                subHeaderSubtitle: "Account",
                subHeaderLeft: string.Empty,
                rightContent: null,
                middleContent: null,
                headerMarginBottom: 10f,
                subHeaderMarginTop: 12f,
                subHeaderMarginBottom: 6f);
            subHeader = headerBlock.SubHeader;
            subtitleLabel = subHeader.SubtitleLabel;
            subtitleNetworkLabel = subHeader.NetworkLabel;
            content.Add(headerBlock.Root);

            accountInfo = WalletUiCommon.BuildAccountInfo(CopyAddress, OpenExplorer, showTitleRow: false);
            accountInfo.Root.style.marginTop = 6;
            accountInfo.Root.style.marginBottom = 12;
            accountInfo.Root.style.alignSelf = Align.Center;
            content.Add(accountInfo.Root);

            statusLabel = WalletUiCommon.CreateStatusLabel();
            statusLabel.style.alignSelf = Align.Center;
            statusLabel.style.width = new Length(100, LengthUnit.Percent);
            statusLabel.style.maxWidth = 1680;
            content.Add(statusLabel);

            var ethExplorerBtn = WalletUiCommon.CreateSecondaryButton("Open Etherscan", () => OpenExplorerFor(PlatformKind.Ethereum), 14, 30);
            ethExplorerBtn.style.minWidth = 140;
            var bscExplorerBtn = WalletUiCommon.CreateSecondaryButton("Open BscScan", () => OpenExplorerFor(PlatformKind.BSC), 14, 30);
            bscExplorerBtn.style.minWidth = 140;
            var neoExplorerBtn = WalletUiCommon.CreateSecondaryButton("Open Neotube", () => OpenExplorerFor(PlatformKind.Neo), 14, 30);
            neoExplorerBtn.style.minWidth = 140;
            var explorerRow = WalletUiCommon.CreateButtonRow(8f, ethExplorerBtn, bscExplorerBtn, neoExplorerBtn);
            content.Add(explorerRow);

            qrImage = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
                style =
                {
                    width = 220,
                    height = 220,
                    alignSelf = Align.Center,
                    marginBottom = 12,
                    backgroundColor = Color.clear,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium,
                    borderLeftWidth = 0,
                    borderRightWidth = 0,
                    borderTopWidth = 0,
                    borderBottomWidth = 0,
                    borderLeftColor = Color.clear,
                    borderRightColor = Color.clear,
                    borderTopColor = Color.clear,
                    borderBottomColor = Color.clear,
                    paddingTop = 0,
                    paddingBottom = 0,
                    paddingLeft = 0,
                    paddingRight = 0
                }
            };
            ApplyDefaultFont(qrImage);
            content.Add(qrImage);

            var exportWifBtn = WalletUiCommon.CreateSecondaryButton("Copy WIF", () => ExportWifAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}ExportWIF failed: {ex}")), 14, 36);
            exportWifBtn.style.minWidth = 140;
            var exportHexBtn = WalletUiCommon.CreateSecondaryButton("Copy HEX", () => ExportHexAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}ExportHEX failed: {ex}")), 14, 36);
            exportHexBtn.style.minWidth = 140;
            var actionsRow = WalletUiCommon.CreateButtonRow(10f, exportWifBtn, exportHexBtn);
            content.Add(actionsRow);

            content.Add(new VSpacer());

            migrateButton = WalletUiCommon.CreateSecondaryButton("Migrate", () => OnMigrateAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Migrate failed: {ex}")), 14, 32);
            setNameButton = WalletUiCommon.CreateSecondaryButton("Set Name", () => OnSetNameAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Set name failed: {ex}")), 14, 32);
            var proofBtn = WalletUiCommon.CreateSecondaryButton("Proof of Addresses", () => OnProofOfAddressesAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Proof of addresses failed: {ex}")), 14, 32);
            migrateButton.style.minWidth = 140;
            setNameButton.style.minWidth = 140;
            proofBtn.style.minWidth = 180;

            var signBtn = WalletUiCommon.CreateSecondaryButton("Sign Message", () => OnSignMessageAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Sign message failed: {ex}")), 14, 32);
            var verifyBtn = WalletUiCommon.CreateSecondaryButton("Verify Signature", () => OnVerifySignatureAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Verify signature failed: {ex}")), 14, 32);
            var scanBtn = WalletUiCommon.CreateSecondaryButton("Connect (QR)", OpenQrScanner, 14, 32);
            signBtn.style.minWidth = 160;
            verifyBtn.style.minWidth = 170;
            scanBtn.style.minWidth = 150;
            actionCloudButtons = new[] { migrateButton, setNameButton, proofBtn, signBtn, verifyBtn, scanBtn };
            // Wide layouts keep all actions on one centered row. A narrow (mobile) layout wraps that
            // row into an uneven staircase, so once the real width is known we rebuild the narrow case
            // as an even 2-column grid. The desktop row is left exactly as before.
            actionRowHost = new VisualElement { style = { flexDirection = FlexDirection.Column } };
            actionRowHost.Add(WalletUiCommon.CreateButtonRow(8f, actionCloudButtons));
            actionRowHost.RegisterCallback<GeometryChangedEvent>(OnActionRowGeometry);
            content.Add(actionRowHost);

            actionButtons.AddRange(new[] { ethExplorerBtn, bscExplorerBtn, neoExplorerBtn, exportWifBtn, exportHexBtn, migrateButton, setNameButton, proofBtn, signBtn, verifyBtn, scanBtn });

            var footer = WalletUiCommon.BuildWalletNavBar(out navBalances, out navHistory, out navAccount, out navExit, () => onShowBalances?.Invoke(), () => onShowHistory?.Invoke(), () => onShowAccount?.Invoke(), () => onExit?.Invoke());
            content.Add(footer);

            root.Add(content);
            BuildModal(root);
        }

        // The action row is built flat (one centered row) for wide layouts. The first time we learn
        // the real width, a narrow layout is rebuilt as an even 2-column grid so the buttons line up
        // as columns instead of a centered staircase. Resolved once; desktop keeps the flat row.
        private void OnActionRowGeometry(GeometryChangedEvent evt)
        {
            if (actionRowResolved)
            {
                return;
            }

            var width = evt.newRect.width;
            if (width <= 1f)
            {
                return;
            }

            actionRowResolved = true;
            if (width < 640f)
            {
                BuildCompactActionGrid();
            }
        }

        private void BuildCompactActionGrid()
        {
            if (actionRowHost == null || actionCloudButtons == null)
            {
                return;
            }

            actionRowHost.Clear();
            var grid = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    justifyContent = Justify.Center,
                    alignSelf = Align.Center,
                    width = new Length(100, LengthUnit.Percent)
                }
            };
            ApplyDefaultFont(grid);

            foreach (var btn in actionCloudButtons)
            {
                if (btn == null)
                {
                    continue;
                }

                // Equal flexible columns: two per row, every button the same width - no staircase.
                btn.style.minWidth = 0;
                btn.style.flexGrow = 1;
                btn.style.flexShrink = 1;
                btn.style.flexBasis = new Length(42, LengthUnit.Percent);
                btn.style.marginLeft = 4;
                btn.style.marginRight = 4;
                btn.style.marginTop = 4;
                btn.style.marginBottom = 4;
                grid.Add(btn);
            }

            actionRowHost.Add(grid);
        }

        private void UpdateLabels(string accountName, string address, NexusKind nexusKind, string nexusName = null)
        {
            accountInfo.AccountLabel.text = accountName ?? string.Empty;
            accountInfo.AddressLabel.text = address ?? string.Empty;
            WalletUiCommon.ApplyNetworkBadge(accountInfo.NetworkLabel, nexusName, nexusKind);
        }

        private void UpdateQr(AccountManager accountManager)
        {
            ClearQrTexture();

            if (accountManager == null || !accountManager.HasSelection)
            {
                qrImage.image = null;
                return;
            }

            var platform = accountManager.CurrentPlatform;
            var address = accountManager.GetAddress(accountManager.CurrentIndex, platform);
            if (string.IsNullOrWhiteSpace(address))
            {
                qrImage.image = null;
                SetStatus("QR unavailable: empty address.");
                return;
            }

            var payload = $"{platform.ToString().ToLowerInvariant()}://{address}";
            try
            {
                qrTexture = context.QrCodeGenerator.Generate(payload, 256);
                qrImage.image = qrTexture;
            }
            catch (Exception e)
            {
                SetStatus("Failed to build QR.");
                Log.WriteWarning($"{LogPrefix}QR generation failed: {e}");
            }
        }

        private void ClearQrTexture()
        {
            if (qrTexture != null)
            {
                UnityEngine.Object.Destroy(qrTexture);
                qrTexture = null;
            }
        }

        private void CopyAddress()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null || !accountManager.HasSelection)
            {
                SetStatus("No address to copy.", WalletUiStatusIntent.TransientShort);
                return;
            }

            var address = accountManager.CurrentAccount.phaAddress;
            if (string.IsNullOrWhiteSpace(address))
            {
                SetStatus("No address to copy.", WalletUiStatusIntent.TransientShort);
                return;
            }

            GUIUtility.systemCopyBuffer = address;
            SetStatus("Address copied.", WalletUiStatusIntent.TransientShort);
        }

        private void OpenExplorer()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null || !accountManager.HasSelection)
            {
                SetStatus("No address to open.", WalletUiStatusIntent.TransientShort);
                return;
            }

            var platform = accountManager.CurrentPlatform;
            var address = accountManager.GetAddress(accountManager.CurrentIndex, platform);
            if (string.IsNullOrWhiteSpace(address))
            {
                SetStatus("No address to open.", WalletUiStatusIntent.TransientShort);
                return;
            }

            var url = GetExplorerUrl(accountManager, platform, address);
            if (string.IsNullOrWhiteSpace(url))
            {
                SetStatus("Explorer URL is not configured.", WalletUiStatusIntent.TransientLong);
                return;
            }

            Application.OpenURL(url);
            SetStatus("Opening explorer...", WalletUiStatusIntent.TransientShort);
        }

        private void OpenExplorerFor(PlatformKind platform)
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null || !accountManager.HasSelection)
            {
                SetStatus("No wallet selected.", WalletUiStatusIntent.TransientShort);
                return;
            }

            string address = null;
            switch (platform)
            {
                case PlatformKind.Ethereum:
                case PlatformKind.BSC:
                    address = accountManager.CurrentAccount.ethAddress;
                    break;
                case PlatformKind.Neo:
                    address = accountManager.CurrentAccount.neoAddress;
                    break;
            }

            if (string.IsNullOrWhiteSpace(address))
            {
                SetStatus("Address is not available.", WalletUiStatusIntent.TransientShort);
                return;
            }

            var url = platform switch
            {
                PlatformKind.Ethereum => accountManager.GetEthExplorerURL(address),
                PlatformKind.BSC => accountManager.GetBscExplorerURL(address),
                PlatformKind.Neo => accountManager.GetN2ExplorerURL(address),
                _ => null
            };

            if (string.IsNullOrWhiteSpace(url))
            {
                SetStatus("Explorer URL is not configured.", WalletUiStatusIntent.TransientLong);
                return;
            }

            Application.OpenURL(url);
            SetStatus("Opening explorer...", WalletUiStatusIntent.TransientShort);
        }

        private static string GetExplorerUrl(AccountManager accountManager, PlatformKind platform, string address)
        {
            return platform switch
            {
                PlatformKind.Phantasma => accountManager.GetPhantasmaAddressURL(address),
                PlatformKind.Ethereum => accountManager.GetEthExplorerURL(address),
                PlatformKind.BSC => accountManager.GetBscExplorerURL(address),
                PlatformKind.Neo => accountManager.GetN2ExplorerURL(address),
                _ => null
            };
        }

        private async Task ExportWifAsync()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null || !accountManager.HasSelection)
            {
                SetStatus("No wallet selected.");
                return;
            }

            var authorized = await RequirePasswordAsync("Export private key (WIF)", ignoreStoredPassword: true);
            if (!authorized)
            {
                SetStatus("Password required to export key.");
                return;
            }

            try
            {
                var wif = accountManager.CurrentWif;
                ShowCopyPanel("Your private key (WIF)", "Never share this key. It provides full access to your wallet.", wif, "WIF copied to clipboard.");
                SetStatus("WIF ready to copy.");
            }
            catch (Exception e)
            {
                SetStatus("Failed to export WIF.");
                Log.WriteWarning($"{LogPrefix}ExportWif failed: {e}");
            }
        }

        private async Task ExportHexAsync()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null || !accountManager.HasSelection)
            {
                SetStatus("No wallet selected.");
                return;
            }

            var authorized = await RequirePasswordAsync("Export private key (HEX)", ignoreStoredPassword: true);
            if (!authorized)
            {
                SetStatus("Password required to export key.");
                return;
            }

            try
            {
                var keys = EthereumKey.FromWIF(accountManager.CurrentWif);
                var hexKey = HexByteConvertorExtensions.ToHex(keys.PrivateKey);
                ShowCopyPanel("Your private key (HEX)", "Never share this key. It provides full access to your wallet.", hexKey, "HEX key copied to clipboard.");
                SetStatus("HEX key ready to copy.");
            }
            catch (Exception e)
            {
                SetStatus("Failed to export HEX key.");
                Log.WriteWarning($"{LogPrefix}ExportHex failed: {e}");
            }
        }

        private async Task OnMigrateAsync()
        {
            var wifPrompt = await ShowModalAsync("Account migration", "Insert WIF of the target account", 32, 128, allowEmpty: false);
            if (wifPrompt.result != PromptResult.Success)
            {
                return;
            }

            var wif = wifPrompt.input;
            var accountManager = AccountManager.Instance;
            if (accountManager == null || accountManager.CurrentState == null)
            {
                SetStatus("Account is not ready.");
                return;
            }

            var oldWif = accountManager.CurrentWif;
            var newKeys = PhantasmaKeys.FromWIF(wif);
            if (newKeys.Address.Text == accountManager.CurrentState.address)
            {
                SetStatus("Provide a different target WIF.");
                return;
            }

            var plan = accountAdminService.BuildMigrateDraft(newKeys.Address);
            if (!plan.Success)
            {
                SetStatus(plan.Error);
                return;
            }

            var confirm = await ShowModalAsync(
                "Confirm migration",
                $"Migrate this account to target address?\n{newKeys.Address.Text}\n\nEnsure both old and new keys are backed up before proceeding.",
                0,
                0,
                allowEmpty: true,
                hasInput: false);
            if (confirm.result != PromptResult.Success)
            {
                SetStatus("Migration cancelled.");
                return;
            }

            var (hash, txResult, error) = await SendTransactionDraftAsync(plan.Draft, true);
            ShowTxResultMessage(hash, txResult, error, null, "It was not possible to migrate the account.");
            if (string.IsNullOrEmpty(error) && hash != Hash.Null)
            {
                accountManager.ReplaceAccountWIF(accountManager.CurrentIndex, wif, accountManager.CurrentPasswordHash, out var deletedDuplicateWallet);
                var duplicateText = string.IsNullOrEmpty(deletedDuplicateWallet) ? string.Empty : $" Duplicate '{deletedDuplicateWallet}' was deleted.";
                var caption = $"The account was migrated.{duplicateText}\n\nPrevious WIF (no longer valid):";
                ShowCopyPanel("Migration complete", caption, oldWif, "Old WIF copied to clipboard.");
                SetStatus("Account migrated.");
            }
            else
            {
                SetStatus(string.IsNullOrEmpty(error) ? "Migration failed." : error);
            }
        }

        private async Task OnSetNameAsync()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager?.CurrentState == null)
            {
                SetStatus("Account is not ready.");
                return;
            }

            var stakeAmount = accountManager.CurrentState.balances != null
                ? accountManager.CurrentState.balances.Where(x => x.Symbol == DomainSettings.StakingTokenSymbol).Select(x => x.Staked).FirstOrDefault()
                : System.Numerics.BigInteger.Zero;
            var soulDecimals = Tokens.GetTokenDecimals(DomainSettings.StakingTokenSymbol, accountManager.CurrentPlatform);
            var oneSoul = WalletAmountParser.FromDecimal(1m, soulDecimals);
            if (stakeAmount < oneSoul)
            {
                SetStatus("Need at least 1 SOUL staked to register a name.");
                return;
            }

            var nameResult = await ShowModalAsync("Register name", "Enter a name for this address", AccountManager.MinAccountNameLength, AccountManager.MaxAccountNameLength, allowEmpty: false);
            if (nameResult.result != PromptResult.Success)
            {
                return;
            }

            var name = nameResult.input;
            if (!ValidationUtils.IsValidIdentifier(name))
            {
                SetStatus("Invalid name. Only lowercase letters/numbers, 3-15 chars.");
                return;
            }

            var hasKcal = await EnsureKcalAvailabilityAsync(accountManager);
            if (!hasKcal)
            {
                return;
            }

            var confirm = await ShowModalAsync("Confirm name registration", $"Send a transaction to register the name '{name}'?", 0, 0, allowEmpty: true, hasInput: false);
            if (confirm.result != PromptResult.Success)
            {
                SetStatus("Name registration cancelled.");
                return;
            }

            var draft = accountAdminService.BuildRegisterNameDraft(name, accountManager.CurrentState.address);
            if (!draft.Success)
            {
                SetStatus(draft.Error);
                return;
            }

            var (hash, txResult, error) = await SendTransactionDraftAsync(draft.Draft, true);
            var nameTxMessage = WalletUiTransactionResultHelper.CombineWithPendingNotice("Name registration sent.");
            ShowTxResultMessage(hash, txResult, error, nameTxMessage);
            if (string.IsNullOrEmpty(error) && hash != Hash.Null)
            {
                SetStatus("Name registration sent.");
                RefreshView();
                accountManager.RefreshHistory(true, PlatformKind.Phantasma);
                await PromptRenameLocalNameAsync(name);
            }
            else
            {
                SetStatus(string.IsNullOrEmpty(error) ? "Name registration failed." : error);
            }
        }

        private async Task PromptRenameLocalNameAsync(string name)
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                return;
            }

            var result = await ShowModalAsync("Rename local account", $"Name transaction submitted for '{name}'. Rename the local account on this device as well?", 0, 0, allowEmpty: true, hasInput: false);
            if (result.result != PromptResult.Success)
            {
                return;
            }

            var ok = accountManager.RenameAccount(name);
            SetStatus(ok ? $"Local account renamed to '{name}'." : "Failed to rename local account.");
            RefreshView();
        }

        private async Task OnSignMessageAsync()
        {
            var chainAndMessage = await PromptChainAndMessageAsync();
            if (chainAndMessage == null)
            {
                return;
            }

            if (!await RequirePasswordAsync())
            {
                return;
            }

            var (chain, message) = chainAndMessage.Value;
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                SetStatus("Account is not ready.");
                return;
            }

            var wif = accountManager.CurrentAccount.GetWif(accountManager.CurrentPasswordHash);
            var messageBytes = Encoding.ASCII.GetBytes(message);
            string signature;

            switch (chain)
            {
                case "Phantasma":
                    var phaSig = PhantasmaKeys.FromWIF(wif).Sign(messageBytes);
                    signature = Base16.Encode(((Ed25519Signature)phaSig).Bytes);
                    break;
                case "Ethereum":
                    var ethKeys = EthereumKey.FromWIF(wif);
                    var ethSig = ECDsa.SignDeterministic(messageBytes, ethKeys.PrivateKey, ECDsaCurve.Secp256k1);
                    signature = Base16.Encode(ethSig);
                    break;
                case "Neo Legacy":
                    var neoKeys = PhantasmaPhoenix.InteropChains.Legacy.Neo2.NeoKeys.FromWIF(wif);
                    var neoSig = ECDsa.SignDeterministic(messageBytes, neoKeys.PrivateKey, ECDsaCurve.Secp256r1);
                    signature = Base16.Encode(neoSig);
                    break;
                default:
                    SetStatus("Unsupported chain.");
                    return;
            }

            ShowCopyPanel("Signature", "Copy the signature below", signature, "Signature copied to clipboard.");
            SetStatus("Signature generated.");
        }

        private async Task OnVerifySignatureAsync()
        {
            var chainMessageAndSig = await PromptChainMessageAndSignatureAsync();
            if (chainMessageAndSig == null)
            {
                return;
            }

            if (!await RequirePasswordAsync())
            {
                return;
            }

            var (chain, message, signatureHex) = chainMessageAndSig.Value;
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                SetStatus("Account is not ready.");
                return;
            }

            var wif = accountManager.CurrentAccount.GetWif(accountManager.CurrentPasswordHash);
            var messageBytes = Encoding.ASCII.GetBytes(message);
            var signature = signatureHex?.Trim();
            if (string.IsNullOrWhiteSpace(signature) || signature.Length % 2 != 0 || !HexByteConvertorExtensions.IsHex(signature))
            {
                SetStatus("Invalid signature format.");
                return;
            }

            byte[] signatureBytes;
            try
            {
                signatureBytes = Base16.Decode(signature);
            }
            catch (Exception)
            {
                SetStatus("Invalid signature format.");
                return;
            }

            try
            {
                var valid = false;

                switch (chain)
                {
                    case "Phantasma":
                        var phaKeys = PhantasmaKeys.FromWIF(wif);
                        valid = Ed25519.Verify(signatureBytes, messageBytes, phaKeys.PublicKey);
                        break;
                    case "Ethereum":
                        var ethKeys = EthereumKey.FromWIF(wif);
                        valid = ECDsa.Verify(messageBytes, signatureBytes, ethKeys.PublicKey, ECDsaCurve.Secp256k1);
                        break;
                    case "Neo Legacy":
                        var neoKeys = PhantasmaPhoenix.InteropChains.Legacy.Neo2.NeoKeys.FromWIF(wif);
                        valid = ECDsa.Verify(messageBytes, signatureBytes, neoKeys.PublicKey, ECDsaCurve.Secp256r1);
                        break;
                    default:
                        SetStatus("Unsupported chain.");
                        return;
                }

                ShowVerificationResult(valid);
                SetStatus(valid ? "Signature is correct." : "Signature is incorrect.");
            }
            catch (Exception)
            {
                SetStatus("Invalid signature format.");
            }
        }

        private async Task OnProofOfAddressesAsync()
        {
            if (!await RequirePasswordAsync())
            {
                return;
            }

            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                SetStatus("Account is not ready.");
                return;
            }

            var wif = accountManager.CurrentAccount.GetWif(accountManager.CurrentPasswordHash);
            var signer = new ProofOfAddressesSigner(wif);
            var proofMessage = signer.GenerateMessage();

            // Step 1: show proof text
            var first = await ShowModalAsync("Proof of addresses", proofMessage, 0, 0, allowEmpty: true, hasInput: false);
            if (first.result != PromptResult.Success)
            {
                SetStatus("POA cancelled.");
                return;
            }

            // Step 2: show signed proof, ask to send
            var signedMessage = signer.GenerateSignedMessage();
            var second = await ShowModalAsync("Signed proof of addresses", signedMessage, 0, 0, allowEmpty: true, hasInput: false);
            if (second.result != PromptResult.Success)
            {
                SetStatus("POA send cancelled.");
                return;
            }

            SetStatus("Sending POA...");
            SendPoaAsync(accountManager.Settings?.phantasmaPoaUrl, signedMessage).Forget(ex =>
            {
                Log.WriteWarning($"{LogPrefix}POA send failed: {ex}");
                SetStatus("POA send failed.");
            });
        }

        private async Task<(string chain, string message)?> PromptChainAndMessageAsync()
        {
            var chain = await ShowChainPickerAsync();
            if (string.IsNullOrWhiteSpace(chain))
            {
                return null;
            }

            var messageResult = await ShowModalAsync("Enter message", "Message to sign", 1, -1, allowEmpty: false, hasInput: true, multiline: true);
            if (messageResult.result != PromptResult.Success)
            {
                return null;
            }

            return (chain, messageResult.input);
        }

        private async Task<(string chain, string message, string signature)?> PromptChainMessageAndSignatureAsync()
        {
            var chain = await ShowChainPickerAsync();
            if (string.IsNullOrWhiteSpace(chain))
            {
                return null;
            }

            var messageResult = await ShowModalAsync("Enter message", "Message that was signed", 1, -1, allowEmpty: false, hasInput: true, multiline: true);
            if (messageResult.result != PromptResult.Success)
            {
                return null;
            }

            var sigResult = await ShowModalAsync("Enter signature", "Hex signature", 1, -1, allowEmpty: false, hasInput: true, multiline: true);
            if (sigResult.result != PromptResult.Success)
            {
                return null;
            }

            return (chain, messageResult.input, sigResult.input);
        }

        private Task<string> ShowChainPickerAsync()
        {
            HideModal();
            chainPickerTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            chainPickerPanel.style.display = DisplayStyle.Flex;
            modalHost.ShowPanel(chainPickerPanel);
            return chainPickerTcs.Task;
        }

        private void HandleChainPickerSelection(string chain)
        {
            var tcs = chainPickerTcs;
            chainPickerTcs = null;
            HideModal();
            tcs?.TrySetResult(chain);
        }

        private void CancelChainPicker()
        {
            var tcs = chainPickerTcs;
            chainPickerTcs = null;
            HideModal();
            tcs?.TrySetResult(null);
        }

        private void OnCopyPanelCopy()
        {
            if (copyPanelValueField != null)
            {
                GUIUtility.systemCopyBuffer = copyPanelValueField.text ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(copyPanelCopyStatus))
            {
                SetStatus(copyPanelCopyStatus, WalletUiStatusIntent.TransientShort);
            }

            HideModal();
        }

        private void ShowCopyPanel(string title, string caption, string value, string copyStatus)
        {
            HideModal();
            copyPanelCopyStatus = copyStatus;

            if (copyPanelTitle != null)
            {
                copyPanelTitle.text = string.IsNullOrWhiteSpace(title) ? "Copy value" : title;
            }

            if (copyPanelCaption != null)
            {
                copyPanelCaption.text = string.IsNullOrWhiteSpace(caption) ? "Copy the value below" : caption;
            }

            if (copyPanelValueField != null)
            {
                copyPanelValueField.value = value ?? string.Empty;
                copyPanelValueField.SetEnabled(true);
            }

            copyPanel.style.display = DisplayStyle.Flex;
            modalHost.ShowPanel(copyPanel);
        }

        private void ShowVerificationResult(bool valid)
        {
            HideModal();
            if (verificationMessageLabel != null)
            {
                verificationMessageLabel.text = valid ? "Signature is correct." : "Signature is incorrect.";
                verificationMessageLabel.style.color = valid ? WalletUiTheme.TextPrimary : Color.red;
            }

            verificationPanel.style.display = DisplayStyle.Flex;
            modalHost.ShowPanel(verificationPanel);
        }

        private void BuildModal(VisualElement parent)
        {
            chainPickerPanel = WalletUiModalFactory.CreateChainPickerPanel(HandleChainPickerSelection, CancelChainPicker, ApplyDefaultFont);
            copyPanel = WalletUiModalFactory.CreateCopyPanel(OnCopyPanelCopy, HideModal, ApplyDefaultFont, out copyPanelTitle, out copyPanelCaption, out copyPanelValueField);
            verificationPanel = WalletUiModalFactory.CreateVerificationPanel(HideModal, ApplyDefaultFont, out verificationMessageLabel);

            transactionDialogs = new WalletUiTransactionDialogs(modalHost, () => AccountManager.Instance, SetStatusText);
            transactionDialogs.RegisterBlockingPanels(null, chainPickerPanel, copyPanel, verificationPanel);
        }

        private void OpenQrScanner()
        {
            // Scan a dApp pairing QR (cross-device) and feed the URI to the same deeplink
            // endpoint the OS uses; the scanner releases the camera when it closes.
            var scannerView = new WalletUiQrScannerView(
                modalHost,
                ApplyDefaultFont,
                "Scan dApp QR",
                "Point the camera at the dApp QR code.",
                "That QR is not a Phantasma pairing code. Keep scanning.",
                text => WalletQrScanner.IsPairingUri(text) ? text.Trim() : null,
                uri => ConnectorManager.Instance?.DeeplinkEndpoint?.TryHandle(uri, _ => { }));
            scannerView.Open();
        }

        private Task<(PromptResult result, string input)> ShowModalAsync(string title, string caption, int minLength, int maxLength, bool allowEmpty = false, bool hasInput = true, bool multiline = false, bool isPassword = false)
        {
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
                "Confirm",
                "Cancel",
                showSecondary: true,
                initialValue: string.Empty,
                successResult: PromptResult.Success,
                cancelResult: PromptResult.Failure);
        }

        private Task<PromptResult> ShowSendProgressAsync(string description, int txCount)
        {
            return transactionDialogs != null
                ? transactionDialogs.ShowSendProgressAsync(description, txCount)
                : Task.FromResult(PromptResult.Failure);
        }

        private Task<(Hash hash, TransactionResult txResult, string error)> StartConfirmationAsync(Hash hash, bool refreshBalanceAfterConfirmation)
        {
            return transactionDialogs != null
                ? transactionDialogs.StartConfirmationAsync(hash, refreshBalanceAfterConfirmation)
                : Task.FromResult((Hash.Null, (TransactionResult)null, "Transaction UI is not ready."));
        }

        private void HideModal()
        {
            modalHost.HideAll();
            chainPickerTcs = null;
            transactionDialogs?.HideTransactionPanels();
            if (copyPanelValueField != null)
            {
                copyPanelValueField.value = string.Empty;
            }
            copyPanelCopyStatus = null;
            if (verificationMessageLabel != null)
            {
                verificationMessageLabel.text = string.Empty;
            }
        }

        private Task<(Hash hash, TransactionResult txResult, string error)> SendTransactionDraftAsync(WalletTransactionDraft draft, bool refreshBalanceAfterConfirmation)
        {
            return transactionOrchestrator.SendTransactionDraftAsync(draft, refreshBalanceAfterConfirmation);
        }

        private void ShowTxResultMessage(Hash hash, TransactionResult txResult, string error, string successMessage = null, string failureMessage = null)
        {
            WalletUiTransactionResultHelper.ShowAsync(modalHost, () => AccountManager.Instance, hash, txResult, error, successMessage, failureMessage)
                .Forget(ex => Log.WriteWarning($"{LogPrefix}Failed to show transaction result: {ex}"));

            if (!string.IsNullOrWhiteSpace(error))
            {
                SetStatus(error);
                return;
            }

            if (hash != Hash.Null && !string.IsNullOrWhiteSpace(successMessage))
            {
                SetStatus(successMessage);
            }
        }

        private async Task<bool> EnsureKcalAvailabilityAsync(AccountManager accountManager)
        {
            if (accountManager == null || accountManager.CurrentState == null)
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

            SetStatus(string.IsNullOrWhiteSpace(errorText) ? "KCAL is required to make transactions!" : errorText);
            return false;
        }

        private async Task<bool> RequirePasswordAsync(string description = "Authorization", bool ignoreStoredPassword = false)
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null || !accountManager.HasSelection)
            {
                SetStatus("No wallet selected.");
                return false;
            }

            if (!accountManager.CurrentAccount.passwordProtected || (!ignoreStoredPassword && !string.IsNullOrEmpty(accountManager.CurrentPasswordHash)))
            {
                return true;
            }

            var tcs = new TaskCompletionSource<PromptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            authService.RequestPassword(description, accountManager.CurrentPlatform, true, false, sharedAuthUi, result => tcs.TrySetResult(result), ignoreStoredPassword);
            var promptResult = await tcs.Task;
            if (promptResult == PromptResult.Success)
            {
                return true;
            }

            SetStatus("Password required.");
            return false;
        }

        private async Task SendPoaAsync(string url, string message)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                SetStatus("POA URL is not configured.");
                return;
            }

            var jsonMessage = "{\"message\": \"" + Convert.ToBase64String(Encoding.ASCII.GetBytes(message)) + "\"}";
            try
            {
                await WebClientAsync.PostAsync<string>(url.TrimEnd('/') + "/api/v1/poa/register", jsonMessage, System.Threading.CancellationToken.None);
                SetStatus("POA sent.");
            }
            catch (Exception)
            {
                SetStatus("POA send failed.");
            }
        }

        private void SetActionsEnabled(bool enabled)
        {
            foreach (var btn in actionButtons)
            {
                btn?.SetEnabled(enabled);
            }
        }

        private void SetStatus(string text, WalletUiStatusIntent intent = WalletUiStatusIntent.None)
        {
            WalletUiCommon.UpdateStatusLabel(statusLabel, text, autoHideSeconds: 0f, intent: intent);
        }

        private void SetStatusText(string text)
        {
            SetStatus(text);
        }

        private void UpdateNavSelection()
        {
            WalletUiCommon.SetNavState(navBalances, false);
            WalletUiCommon.SetNavState(navHistory, false);
            WalletUiCommon.SetNavState(navAccount, true);
            WalletUiCommon.SetNavState(navExit, false);
        }

        // Hide unstable account actions unless a developer explicitly opts in.
        private void UpdateUnstableActionVisibility(Poltergeist.Settings settings)
        {
            var showUnstableTools = settings != null && settings.devMode && settings.showUnstableTools;

            if (migrateButton != null)
            {
                migrateButton.style.display = showUnstableTools ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (setNameButton != null)
            {
                setNameButton.style.display = showUnstableTools ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void ApplyDefaultFont(VisualElement element)
        {
            WalletUiCommon.ApplyDefaultFont(element);
        }

    }
}
