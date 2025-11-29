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
        private readonly IWalletAuthUi sharedAuthUi;
        private readonly WalletUiModalHost modalHost;
        private readonly Action onShowBalances;
        private readonly Action onShowHistory;
        private readonly Action onShowAccount;
        private readonly Action onShowSettings;
        private readonly Action onExit;

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
        private HeaderElements header;
        private SubHeaderElements subHeader;
        private List<Button> actionButtons = new List<Button>();

        public WalletAccountView(VisualElement host, WalletApplicationContext context, WalletUiModalHost modalHost, IWalletAuthUi sharedAuthUi, Action onShowBalances, Action onShowHistory, Action onShowAccount, Action onShowSettings, Action onExit)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            authService = context.AuthService ?? throw new ArgumentNullException(nameof(context.AuthService));
            accountAdminService = context.AccountAdminService ?? throw new ArgumentNullException(nameof(context.AccountAdminService));
            feeRequirement = context.FeeRequirement ?? throw new ArgumentNullException(nameof(context.FeeRequirement));
            transactionOrchestrator = new WalletTransactionOrchestrator(() => AccountManager.Instance, new AccountTransactionUi(authService, sharedAuthUi ?? throw new ArgumentNullException(nameof(sharedAuthUi)), SetStatus, SetActionsEnabled, ShowSendProgressDialog, StartConfirmationWait));
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
        }

        public void OnAccountsReady()
        {
            RefreshView();
            UpdateNavSelection();
        }

        public void MarkAsActive()
        {
            UpdateNavSelection();
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

            var headerBlock = WalletUiCommon.BuildHeaderBlock("Account", "Account", headerMarginBottom: 10f, subHeaderMarginTop: 12f, subHeaderMarginBottom: 6f);
            header = headerBlock.Header;
            subHeader = headerBlock.SubHeader;
            subtitleLabel = subHeader.SubtitleLabel;
            subtitleNetworkLabel = subHeader.NetworkLabel;
            content.Add(headerBlock.Root);

            accountInfo = WalletUiCommon.BuildAccountInfo(CopyAddress, OpenExplorer, showTitleRow: false);
            accountInfo.Root.style.marginTop = 6;
            accountInfo.Root.style.marginBottom = 12;
            accountInfo.Root.style.alignSelf = Align.Center;
            content.Add(accountInfo.Root);

            var explorerRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    alignSelf = Align.Center,
                    marginBottom = 10
                }
            };
            ApplyDefaultFont(explorerRow);

            var ethExplorerBtn = WalletUiCommon.CreateSecondaryButton("Open Etherscan", () => OpenExplorerFor(PlatformKind.Ethereum), 14, 30);
            ethExplorerBtn.style.minWidth = 140;
            var bscExplorerBtn = WalletUiCommon.CreateSecondaryButton("Open BscScan", () => OpenExplorerFor(PlatformKind.BSC), 14, 30);
            bscExplorerBtn.style.minWidth = 140;
            bscExplorerBtn.style.marginLeft = 8;
            var neoExplorerBtn = WalletUiCommon.CreateSecondaryButton("Open Neotube", () => OpenExplorerFor(PlatformKind.Neo), 14, 30);
            neoExplorerBtn.style.minWidth = 140;
            neoExplorerBtn.style.marginLeft = 8;

            explorerRow.Add(ethExplorerBtn);
            explorerRow.Add(bscExplorerBtn);
            explorerRow.Add(neoExplorerBtn);
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

            var actionsRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    alignSelf = Align.Center,
                    marginTop = 4,
                    marginBottom = 4
                }
            };
            ApplyDefaultFont(actionsRow);

            var exportWifBtn = WalletUiCommon.CreateSecondaryButton("Copy WIF", ExportWif, 14, 36);
            exportWifBtn.style.minWidth = 140;
            var exportHexBtn = WalletUiCommon.CreateSecondaryButton("Copy HEX", ExportHex, 14, 36);
            exportHexBtn.style.minWidth = 140;
            exportHexBtn.style.marginLeft = 10;

            actionsRow.Add(exportWifBtn);
            actionsRow.Add(exportHexBtn);
            content.Add(actionsRow);

            var spacer = new VisualElement { style = { flexGrow = 1, minHeight = 0 } };
            content.Add(spacer);

            var actionsRow2 = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    alignSelf = Align.Center,
                    marginTop = 4,
                    marginBottom = 2
                }
            };
            ApplyDefaultFont(actionsRow2);

            var migrateBtn = WalletUiCommon.CreateSecondaryButton("Migrate", OnMigrate, 14, 32);
            var setNameBtn = WalletUiCommon.CreateSecondaryButton("Set Name", OnSetName, 14, 32);
            var proofBtn = WalletUiCommon.CreateSecondaryButton("Proof of Addresses", OnProofOfAddresses, 14, 32);
            migrateBtn.style.minWidth = 140;
            setNameBtn.style.minWidth = 140;
            proofBtn.style.minWidth = 180;
            setNameBtn.style.marginLeft = 8;
            proofBtn.style.marginLeft = 8;
            actionsRow2.Add(migrateBtn);
            actionsRow2.Add(setNameBtn);
            actionsRow2.Add(proofBtn);
            content.Add(actionsRow2);

            var actionsRow3 = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    alignSelf = Align.Center,
                    marginTop = 2,
                    marginBottom = 6
                }
            };
            ApplyDefaultFont(actionsRow3);

            var signBtn = WalletUiCommon.CreateSecondaryButton("Sign Message", OnSignMessage, 14, 32);
            var verifyBtn = WalletUiCommon.CreateSecondaryButton("Verify Signature", OnVerifySignature, 14, 32);
            signBtn.style.minWidth = 160;
            verifyBtn.style.minWidth = 170;
            verifyBtn.style.marginLeft = 8;
            actionsRow3.Add(signBtn);
            actionsRow3.Add(verifyBtn);
            content.Add(actionsRow3);

            actionButtons.AddRange(new[] { ethExplorerBtn, bscExplorerBtn, neoExplorerBtn, exportWifBtn, exportHexBtn, migrateBtn, setNameBtn, proofBtn, signBtn, verifyBtn });

            statusLabel = new Label
            {
                text = "Initializing account view...",
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 14,
                    marginTop = 2,
                    marginBottom = 6,
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
            content.Add(statusLabel);

            var footer = WalletUiCommon.BuildWalletNavBar(out navBalances, out navHistory, out navAccount, out navExit, () => onShowBalances?.Invoke(), () => onShowHistory?.Invoke(), () => onShowAccount?.Invoke(), () => onExit?.Invoke());
            content.Add(footer);

            root.Add(content);
            BuildModal(root);
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

            var platform = accountManager.CurrentPlatform;
            var address = accountManager.GetAddress(accountManager.CurrentIndex, platform);
            if (string.IsNullOrWhiteSpace(address))
            {
                SetStatus("No address to open.");
                return;
            }

            var url = GetExplorerUrl(accountManager, platform, address);
            if (string.IsNullOrWhiteSpace(url))
            {
                SetStatus("Explorer URL is not configured.");
                return;
            }

            Application.OpenURL(url);
            SetStatus("Opening explorer...");
        }

        private void OpenExplorerFor(PlatformKind platform)
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null || !accountManager.HasSelection)
            {
                SetStatus("No wallet selected.");
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
                SetStatus("Address is not available.");
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
                SetStatus("Explorer URL is not configured.");
                return;
            }

            Application.OpenURL(url);
            SetStatus("Opening explorer...");
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

        private async void ExportWif()
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

        private async void ExportHex()
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

        private async void OnMigrate()
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

        private async void OnSetName()
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

        private async void OnSignMessage()
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

        private async void OnVerifySignature()
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

        private async void OnProofOfAddresses()
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

        private static string NormalizeChain(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            var value = input.Trim();
            if (value.Equals("phantasma", StringComparison.OrdinalIgnoreCase))
            {
                return "Phantasma";
            }

            if (value.Equals("ethereum", StringComparison.OrdinalIgnoreCase) || value.Equals("eth", StringComparison.OrdinalIgnoreCase))
            {
                return "Ethereum";
            }

            if (value.Equals("neo legacy", StringComparison.OrdinalIgnoreCase) || value.Equals("neo", StringComparison.OrdinalIgnoreCase))
            {
                return "Neo Legacy";
            }

            return null;
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
                SetStatus(copyPanelCopyStatus);
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

            transactionDialogs = new WalletUiTransactionDialogs(modalHost, () => AccountManager.Instance, SetStatus);
            transactionDialogs.RegisterBlockingPanels(null, chainPickerPanel, copyPanel, verificationPanel);
        }

        private Task<(PromptResult result, string input)> ShowModalAsync(string title, string caption, int minLength, int maxLength, bool allowEmpty = false, bool hasInput = true, bool multiline = false, bool isPassword = false)
        {
            var effectiveAllowEmpty = allowEmpty || !hasInput || minLength <= 0;
            return modalHost.ShowPromptAsync(
                title,
                caption,
                minLength,
                maxLength,
                effectiveAllowEmpty,
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

        private void ShowSendProgressDialog(string description, int txCount, Action<PromptResult> callback)
        {
            transactionDialogs?.ShowSendProgress(description, txCount, callback);
        }

        private void StartConfirmationWait(Hash hash, bool refreshBalanceAfterConfirmation, Action<Hash, TransactionResult, string> callback)
        {
            transactionDialogs?.StartConfirmation(hash, refreshBalanceAfterConfirmation, callback);
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
            var tcs = new TaskCompletionSource<(Hash hash, TransactionResult txResult, string error)>(TaskCreationOptions.RunContinuationsAsynchronously);
            transactionOrchestrator.SendTransactionDraft(draft, refreshBalanceAfterConfirmation, (hash, txResult, error) => tcs.TrySetResult((hash, txResult, error)));
            return tcs.Task;
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

        private void SetStatus(string text)
        {
            statusLabel.text = string.IsNullOrWhiteSpace(text) ? string.Empty : text;
            statusLabel.style.visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Hidden : Visibility.Visible;
        }

        private void UpdateNavSelection()
        {
            WalletUiCommon.SetNavState(navBalances, false);
            WalletUiCommon.SetNavState(navHistory, false);
            WalletUiCommon.SetNavState(navAccount, true);
            WalletUiCommon.SetNavState(navExit, false);
        }

        private void ApplyDefaultFont(VisualElement element)
        {
            WalletUiCommon.ApplyDefaultFont(element);
        }

        private sealed class AccountTransactionUi : IWalletTransactionUi
        {
            private readonly WalletAuthService authService;
            private readonly IWalletAuthUi authUi;
            private readonly Action<string> setStatus;
            private readonly Action<bool> setEnabled;
            private readonly Action<string, int, Action<PromptResult>> showSendProgress;
            private readonly Action<Hash, bool, Action<Hash, TransactionResult, string>> showConfirmation;
            private bool sending;

            internal AccountTransactionUi(WalletAuthService authService, IWalletAuthUi authUi, Action<string> setStatus, Action<bool> setEnabled, Action<string, int, Action<PromptResult>> showSendProgress, Action<Hash, bool, Action<Hash, TransactionResult, string>> showConfirmation)
            {
                this.authService = authService ?? throw new ArgumentNullException(nameof(authService));
                this.authUi = authUi ?? throw new ArgumentNullException(nameof(authUi));
                this.setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
                this.setEnabled = setEnabled ?? throw new ArgumentNullException(nameof(setEnabled));
                this.showSendProgress = showSendProgress ?? throw new ArgumentNullException(nameof(showSendProgress));
                this.showConfirmation = showConfirmation ?? throw new ArgumentNullException(nameof(showConfirmation));
            }

            public void RequestPassword(string description, PlatformKind platform, Action<PromptResult> callback)
            {
                authService.RequestPassword(description, platform, true, false, authUi, callback, ignoreStoredPassword: false);
            }

            public void ShowSendProgress(string description, int txCount, Action<PromptResult> callback)
            {
                showSendProgress(description, txCount, callback);
            }

            public void PushSendingState()
            {
                sending = true;
                setEnabled(false);
            }

            public void PopSendingState()
            {
                sending = false;
                setEnabled(true);
            }

            public void ShowConfirmation(Hash hash, bool refreshBalanceAfterConfirmation, Action<Hash, TransactionResult, string> callback)
            {
                showConfirmation(hash, refreshBalanceAfterConfirmation, (txHash, txResult, error) =>
                {
                    sending = false;
                    setEnabled(true);
                    callback?.Invoke(txHash, txResult, error);
                });
            }

            public void ShowError(string message)
            {
                setStatus(message ?? "Error");
                if (sending)
                {
                    setEnabled(true);
                }
            }
        }
    }
}
