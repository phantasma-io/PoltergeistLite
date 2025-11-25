using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Poltergeist.Wallet;
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
        private readonly WalletTransactionOrchestrator transactionOrchestrator;
        private readonly IWalletAuthUi sharedAuthUi;
        private readonly Action onShowBalances;
        private readonly Action onShowHistory;
        private readonly Action onShowAccount;
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
        private VisualElement modalOverlay;
        private Label modalTitle;
        private Label modalCaption;
        private TextField modalInput;
        private Button modalPrimary;
        private Button modalSecondary;
        private Action<PromptResult, string> modalCallback;
        private int modalMinLength;
        private int modalMaxLength;
        private bool modalHasInput;
        private bool modalAllowEmpty;
        private HeaderElements header;
        private SubHeaderElements subHeader;
        private List<Button> actionButtons = new List<Button>();

        public WalletAccountView(VisualElement host, WalletApplicationContext context, IWalletAuthUi sharedAuthUi, Action onShowBalances, Action onShowHistory, Action onShowAccount, Action onExit)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            authService = context.AuthService ?? throw new ArgumentNullException(nameof(context.AuthService));
            accountAdminService = context.AccountAdminService ?? throw new ArgumentNullException(nameof(context.AccountAdminService));
            transactionOrchestrator = new WalletTransactionOrchestrator(() => AccountManager.Instance, new AccountTransactionUi(authService, sharedAuthUi ?? throw new ArgumentNullException(nameof(sharedAuthUi)), SetStatus, SetActionsEnabled));
            this.sharedAuthUi = sharedAuthUi ?? throw new ArgumentNullException(nameof(sharedAuthUi));
            this.onShowBalances = onShowBalances ?? throw new ArgumentNullException(nameof(onShowBalances));
            this.onShowHistory = onShowHistory ?? throw new ArgumentNullException(nameof(onShowHistory));
            this.onShowAccount = onShowAccount ?? (() => { });
            this.onExit = onExit ?? throw new ArgumentNullException(nameof(onExit));

            BuildLayout(host ?? throw new ArgumentNullException(nameof(host)));
            RefreshView();
            UpdateNavSelection();
        }

        public void Dispose()
        {
            ClearQrTexture();
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

            subtitleLabel.text = "Account";
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

            qrImage = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
                style =
                {
                    width = 220,
                    height = 220,
                    alignSelf = Align.Center,
                    marginBottom = 12,
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
                    paddingTop = 6,
                    paddingBottom = 6,
                    paddingLeft = 6,
                    paddingRight = 6
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

            var exportWifBtn = WalletUiCommon.CreatePrimaryButton("Copy WIF", ExportWif, 14, 36);
            exportWifBtn.style.minWidth = 140;
            var exportHexBtn = WalletUiCommon.CreatePrimaryButton("Copy HEX", ExportHex, 14, 36);
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

            actionButtons.AddRange(new[] { exportWifBtn, exportHexBtn, migrateBtn, setNameBtn, proofBtn, signBtn, verifyBtn });

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

            var footer = WalletUiCommon.BuildNavBar(out navBalances, out navHistory, out navAccount, out navExit, () => onShowBalances?.Invoke(), () => onShowHistory?.Invoke(), () => onShowAccount?.Invoke(), () => onExit?.Invoke());
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

        private void ExportWif()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null || !accountManager.HasSelection)
            {
                SetStatus("No wallet selected.");
                return;
            }

            authService.RequestPassword("Export private key (WIF)", accountManager.CurrentPlatform, true, false, sharedAuthUi, result =>
            {
                if (result != PromptResult.Success)
                {
                    SetStatus("Password required to export key.");
                    return;
                }

                try
                {
                    var wif = accountManager.CurrentWif;
                    GUIUtility.systemCopyBuffer = wif;
                    SetStatus("WIF copied to clipboard.");
                }
                catch (Exception e)
                {
                    SetStatus("Failed to export WIF.");
                    Log.WriteWarning($"{LogPrefix}ExportWif failed: {e}");
                }
            }, ignoreStoredPassword: true);
        }

        private void ExportHex()
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null || !accountManager.HasSelection)
            {
                SetStatus("No wallet selected.");
                return;
            }

            authService.RequestPassword("Export private key (HEX)", accountManager.CurrentPlatform, true, false, sharedAuthUi, result =>
            {
                if (result != PromptResult.Success)
                {
                    SetStatus("Password required to export key.");
                    return;
                }

                try
                {
                    var keys = EthereumKey.FromWIF(accountManager.CurrentWif);
                    var hexKey = HexByteConvertorExtensions.ToHex(keys.PrivateKey);
                    GUIUtility.systemCopyBuffer = hexKey;
                    SetStatus("HEX key copied to clipboard.");
                }
                catch (Exception e)
                {
                    SetStatus("Failed to export HEX key.");
                    Log.WriteWarning($"{LogPrefix}ExportHex failed: {e}");
                }
            }, ignoreStoredPassword: true);
        }

        private void OnMigrate()
        {
            ShowModal("Account migration", "Insert WIF of the target account", 32, 128, result =>
            {
                if (result.result != PromptResult.Success)
                {
                    return;
                }

                var wif = result.input;
                var accountManager = AccountManager.Instance;
                if (accountManager == null || accountManager.CurrentState == null)
                {
                    SetStatus("Account is not ready.");
                    return;
                }

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

                transactionOrchestrator.SendTransactionDraft(plan.Draft, true, (hash, txResult, error) =>
                {
                    if (string.IsNullOrEmpty(error) && hash != Hash.Null)
                    {
                        accountManager.ReplaceAccountWIF(accountManager.CurrentIndex, wif, accountManager.CurrentPasswordHash, out var deletedDuplicateWallet);
                        SetStatus(string.IsNullOrEmpty(deletedDuplicateWallet) ? "Account migrated." : $"Account migrated. Duplicate '{deletedDuplicateWallet}' removed.");
                    }
                    else
                    {
                        SetStatus(string.IsNullOrEmpty(error) ? "Migration failed." : error);
                    }
                });
            }, allowEmpty: false);
        }

        private void OnSetName()
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

            ShowModal("Register name", "Enter a name for this address", AccountManager.MinAccountNameLength, AccountManager.MaxAccountNameLength, result =>
            {
                if (result.result != PromptResult.Success)
                {
                    return;
                }

                var name = result.input;
                if (!ValidationUtils.IsValidIdentifier(name))
                {
                    SetStatus("Invalid name. Only lowercase letters/numbers, 3-15 chars.");
                    return;
                }

                var draft = accountAdminService.BuildRegisterNameDraft(name, accountManager.CurrentState.address);
                if (!draft.Success)
                {
                    SetStatus(draft.Error);
                    return;
                }

                transactionOrchestrator.SendTransactionDraft(draft.Draft, true, (hash, txResult, error) =>
                {
                    if (string.IsNullOrEmpty(error) && hash != Hash.Null)
                    {
                        SetStatus("Name registration submitted.");
                    }
                    else
                    {
                        SetStatus(string.IsNullOrEmpty(error) ? "Name registration failed." : error);
                    }
                });
            }, allowEmpty: false);
        }

        private void OnSignMessage()
        {
            PromptChainAndMessage((chain, message) =>
            {
                RequirePasswordThen(() =>
                {
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

                    GUIUtility.systemCopyBuffer = signature;
                    SetStatus("Signature copied to clipboard.");
                });
            });
        }

        private void OnVerifySignature()
        {
            PromptChainMessageAndSignature((chain, message, signatureHex) =>
            {
                RequirePasswordThen(() =>
                {
                    var accountManager = AccountManager.Instance;
                    if (accountManager == null)
                    {
                        SetStatus("Account is not ready.");
                        return;
                    }

                    var wif = accountManager.CurrentAccount.GetWif(accountManager.CurrentPasswordHash);
                    var messageBytes = Encoding.ASCII.GetBytes(message);
                    var signatureBytes = Base16.Decode(signatureHex);
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

                    SetStatus(valid ? "Signature is correct." : "Signature is incorrect.");
                });
            });
        }

        private void OnProofOfAddresses()
        {
            RequirePasswordThen(() =>
            {
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
                ShowModal("Proof of addresses", proofMessage, 0, 0, first =>
                {
                    if (first.result != PromptResult.Success)
                    {
                        SetStatus("POA cancelled.");
                        return;
                    }

                    // Step 2: show signed proof, ask to send
                    var signedMessage = signer.GenerateSignedMessage();
                    ShowModal("Signed proof of addresses", signedMessage, 0, 0, second =>
                    {
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
                    }, allowEmpty: true, hasInput: false);
                }, allowEmpty: true, hasInput: false);
            });
        }

        private void PromptChainAndMessage(Action<string, string> callback)
        {
            ShowModal("Select chain", "Phantasma / Ethereum / Neo Legacy", 3, 20, chainResult =>
            {
                if (chainResult.result != PromptResult.Success)
                {
                    return;
                }

                var chain = NormalizeChain(chainResult.input);
                if (chain == null)
                {
                    SetStatus("Unsupported chain.");
                    return;
                }

                ShowModal("Enter message", "Message to sign", 1, -1, messageResult =>
                {
                    if (messageResult.result != PromptResult.Success)
                    {
                        return;
                    }

                    callback(chain, messageResult.input);
                }, allowEmpty: false);
            }, allowEmpty: false);
        }

        private void PromptChainMessageAndSignature(Action<string, string, string> callback)
        {
            ShowModal("Select chain", "Phantasma / Ethereum / Neo Legacy", 3, 20, chainResult =>
            {
                if (chainResult.result != PromptResult.Success)
                {
                    return;
                }

                var chain = NormalizeChain(chainResult.input);
                if (chain == null)
                {
                    SetStatus("Unsupported chain.");
                    return;
                }

                ShowModal("Enter message", "Message that was signed", 1, -1, messageResult =>
                {
                    if (messageResult.result != PromptResult.Success)
                    {
                        return;
                    }

                    ShowModal("Enter signature", "Hex signature", 1, -1, sigResult =>
                    {
                        if (sigResult.result != PromptResult.Success)
                        {
                            return;
                        }

                        callback(chain, messageResult.input, sigResult.input);
                    }, allowEmpty: false);
                }, allowEmpty: false);
            }, allowEmpty: false);
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

        private void RequirePasswordThen(Action onAuthorized)
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null || !accountManager.HasSelection)
            {
                SetStatus("No wallet selected.");
                return;
            }

            if (!accountManager.CurrentAccount.passwordProtected || !string.IsNullOrEmpty(accountManager.CurrentPasswordHash))
            {
                onAuthorized?.Invoke();
                return;
            }

            authService.RequestPassword("Authorization", accountManager.CurrentPlatform, true, false, sharedAuthUi, result =>
            {
                if (result == PromptResult.Success)
                {
                    onAuthorized?.Invoke();
                }
                else
                {
                    SetStatus("Password required.");
                }
            }, ignoreStoredPassword: false);
        }

        private void BuildModal(VisualElement parent)
        {
            modalOverlay = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    right = 0,
                    top = 0,
                    bottom = 0,
                    backgroundColor = WalletUiTheme.Overlay,
                    justifyContent = Justify.Center,
                    alignItems = Align.Center,
                    display = DisplayStyle.None
                }
            };
            modalOverlay.pickingMode = PickingMode.Position;
            ApplyDefaultFont(modalOverlay);

            var modalWindow = new VisualElement
            {
                style =
                {
                    width = 540,
                    maxWidth = new Length(95, LengthUnit.Percent),
                    backgroundColor = WalletUiTheme.ModalBackground,
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
                    borderTopColor = WalletUiTheme.ModalBorder,
                    borderBottomColor = WalletUiTheme.ModalBorder,
                    paddingLeft = 18,
                    paddingRight = 18,
                    paddingTop = 14,
                    paddingBottom = 14,
                    flexDirection = FlexDirection.Column,
                    alignItems = Align.Stretch
                }
            };
            ApplyDefaultFont(modalWindow);

            modalTitle = new Label("Input")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 18,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginBottom = 8
                }
            };
            ApplyDefaultFont(modalTitle);
            modalWindow.Add(modalTitle);

            modalCaption = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 14,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    whiteSpace = WhiteSpace.Normal,
                    marginBottom = 8
                }
            };
            ApplyDefaultFont(modalCaption);
            modalWindow.Add(modalCaption);

            modalInput = new TextField
            {
                multiline = true,
                isPasswordField = false,
                maskChar = '*',
                style =
                {
                    fontSize = 14,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 10,
                    minHeight = 60,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 6,
                    paddingBottom = 6,
                    backgroundColor = WalletUiTheme.InputBackground,
                    color = WalletUiTheme.TextPrimary,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.InputBorder,
                    borderRightColor = WalletUiTheme.InputBorder,
                    borderTopColor = WalletUiTheme.InputBorder,
                    borderBottomColor = WalletUiTheme.InputBorder,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall
                }
            };
            ApplyDefaultFont(modalInput);
            modalWindow.Add(modalInput);

            var modalButtons = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexEnd,
                    marginTop = 4
                }
            };
            ApplyDefaultFont(modalButtons);

            modalSecondary = WalletUiCommon.CreateSecondaryButton("Cancel", OnModalSecondary, 14, 32);
            modalSecondary.style.minWidth = 100;
            modalPrimary = WalletUiCommon.CreatePrimaryButton("Confirm", OnModalPrimary, 14, 32);
            modalPrimary.style.minWidth = 100;
            modalPrimary.style.marginLeft = 8;
            modalButtons.Add(modalSecondary);
            modalButtons.Add(modalPrimary);
            modalWindow.Add(modalButtons);

            modalOverlay.Add(modalWindow);
            parent.Add(modalOverlay);
        }

        private void ShowModal(string title, string caption, int minLength, int maxLength, Action<(PromptResult result, string input)> callback, bool allowEmpty = false, bool hasInput = true)
        {
            modalCallback = (r, input) => callback((r, input));
            modalMinLength = minLength;
            modalMaxLength = maxLength;
            modalHasInput = hasInput;
            modalAllowEmpty = allowEmpty;

            modalTitle.text = title ?? string.Empty;
            modalCaption.text = caption ?? string.Empty;
            modalInput.value = string.Empty;
            modalInput.visible = hasInput;
            modalInput.SetEnabled(hasInput);
            modalOverlay.style.display = DisplayStyle.Flex;
            if (hasInput)
            {
                modalInput.Focus();
            }
        }

        private void OnModalPrimary()
        {
            var input = modalHasInput ? modalInput.text ?? string.Empty : string.Empty;
            if (!modalAllowEmpty)
            {
                if (modalMaxLength > 0 && input.Length > modalMaxLength)
                {
                    modalCaption.text = $"Input must be <= {modalMaxLength} characters.";
                    return;
                }

                if (input.Length < modalMinLength)
                {
                    modalCaption.text = $"Input must be >= {modalMinLength} characters.";
                    return;
                }
            }

            var cb = modalCallback;
            HideModal();
            cb?.Invoke(PromptResult.Success, input);
        }

        private void OnModalSecondary()
        {
            var cb = modalCallback;
            HideModal();
            cb?.Invoke(PromptResult.Failure, string.Empty);
        }

        private void HideModal()
        {
            modalOverlay.style.display = DisplayStyle.None;
            modalInput.value = string.Empty;
            modalCallback = null;
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
            private bool sending;

            internal AccountTransactionUi(WalletAuthService authService, IWalletAuthUi authUi, Action<string> setStatus, Action<bool> setEnabled)
            {
                this.authService = authService ?? throw new ArgumentNullException(nameof(authService));
                this.authUi = authUi ?? throw new ArgumentNullException(nameof(authUi));
                this.setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
                this.setEnabled = setEnabled ?? throw new ArgumentNullException(nameof(setEnabled));
            }

            public void RequestPassword(string description, PlatformKind platform, Action<PromptResult> callback)
            {
                authService.RequestPassword(description, platform, true, false, authUi, callback, ignoreStoredPassword: false);
            }

            public void ShowSendProgress(string description, int txCount, Action<PromptResult> callback)
            {
                setStatus($"{description}");
                callback?.Invoke(PromptResult.Success);
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
                setStatus($"Transaction sent: {hash}");
                callback?.Invoke(hash, null, null);
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
