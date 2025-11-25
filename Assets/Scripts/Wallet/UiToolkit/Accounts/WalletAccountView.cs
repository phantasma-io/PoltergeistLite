using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poltergeist;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Unity.Core.Logging;
using PhantasmaPhoenix.InteropChains.Legacy.Ethereum;
using PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Hex.HexConvertors.Extensions;

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
        private HeaderElements header;
        private SubHeaderElements subHeader;

        public WalletAccountView(VisualElement host, WalletApplicationContext context, IWalletAuthUi sharedAuthUi, Action onShowBalances, Action onShowHistory, Action onShowAccount, Action onExit)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            authService = context.AuthService ?? throw new ArgumentNullException(nameof(context.AuthService));
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
            root.style.backgroundColor = WalletUiTheme.ScreenBackground;
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
    }
}
