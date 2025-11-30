using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Poltergeist.Wallet;
using Poltergeist;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist.UiToolkit;

namespace Poltergeist.UiToolkit.Accounts
{
    /// <summary>
    /// Wallet picker + password prompt for UITK.
    /// </summary>
    public sealed partial class WalletAccountsView : IWalletAuthUi, IDisposable
    {
        private const string LogPrefix = "[UITK] ";

        private readonly WalletApplicationContext context;
        private readonly WalletAuthService authService;
        private readonly Action onLoginSuccess;
        private readonly Action onShowSettings;
        private HeaderElements header;
        private SubHeaderElements subHeader;
        private Label subtitleLabel;
        private Label subtitleNetworkLabel;
        private Label walletsLabel;
        private VisualElement root;
        private ScrollView list;
        private VisualElement listWrapper;
        private Label statusLabel;
        private VisualElement mainFooter;
        private VisualElement manageRoot;
        private WalletUiSignals uiSignals;
        private readonly WalletUiModalHost modalHost;
        private bool listWasEnabled = true;
        private bool rootWheelHooked;
        private bool listTemporarilyHidden;
        private bool listDetachedForModal;
        private int listIndexBeforeDetach = -1;
        private PickingMode listPickingModeBeforeModal;
        private EventCallback<KeyDownEvent> tabBlockHandler;

        public WalletAccountsView(VisualElement host, WalletApplicationContext context, WalletUiModalHost modalHost, Action onLoginSuccess, Action onShowSettings)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            authService = context.AuthService ?? throw new ArgumentNullException(nameof(context.AuthService));
            this.onLoginSuccess = onLoginSuccess;
            this.onShowSettings = onShowSettings ?? throw new ArgumentNullException(nameof(onShowSettings));
            this.modalHost = modalHost ?? throw new ArgumentNullException(nameof(modalHost));
            uiSignals = context.UiSignals;

            BuildLayout(host ?? throw new ArgumentNullException(nameof(host)));
            Subscribe();
        }

        public void Dispose()
        {
            Unsubscribe();
            HideModal();
            WalletUiCommon.UnblockTabNavigation(root, tabBlockHandler);
            tabBlockHandler = null;
        }

        public void Refresh()
        {
            var am = AccountManager.Instance;
            list.Clear();
            var isManageMode = manageRoot != null && manageRoot.style.display == DisplayStyle.Flex;

            subtitleLabel.text = "Wallet List";
            var headerSettings = AccountManager.Instance?.Settings;
            if (headerSettings != null)
            {
                WalletUiCommon.ApplyNetworkBadge(subtitleNetworkLabel, headerSettings.nexusName, headerSettings.nexusKind);
            }
            else
            {
                subtitleNetworkLabel.text = string.Empty;
                subtitleNetworkLabel.style.color = WalletUiTheme.TextSecondary;
            }

            if (am == null || am.Accounts == null || am.Accounts.Count == 0)
            {
                walletsLabel.text = "0 wallets";
                if (!isManageMode)
                {
                    SetStatus("No wallets found. Import or create one first.");
                }
                var empty = new Label("No wallets available.")
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        unityTextAlign = TextAnchor.MiddleCenter,
                        fontSize = 16,
                        marginTop = 10
                    }
                };
                ApplyDefaultFont(empty);
                list.Add(empty);
                return;
            }

            var hiddenSet = new HashSet<string>(am.HiddenPhantasmaAddresses ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var hiddenCount = 0;
            var visibleCount = 0;
            walletsLabel.text = $"{am.Accounts.Count} wallet(s)";
            var settings = am.Settings;
            if (settings != null && header != null)
            {
                WalletUiCommon.ApplyNetworkBadge(header.NetworkLabel, settings.nexusName, settings.nexusKind);
            }
            else if (header != null)
            {
                header.NetworkLabel.text = string.Empty;
            }

            for (var i = 0; i < am.Accounts.Count; i++)
            {
                var account = am.Accounts[i];
                if (!string.IsNullOrWhiteSpace(account.phaAddress) && hiddenSet.Contains(account.phaAddress))
                {
                    hiddenCount++;
                    continue;
                }

                list.Add(CreateRow(account, i));
                visibleCount++;
            }

            var hiddenSuffix = hiddenCount > 0 ? $" ({hiddenCount} hidden)" : string.Empty;
            walletsLabel.text = $"{visibleCount} wallet(s){hiddenSuffix}";

            if (visibleCount == 0)
            {
                var message = hiddenCount > 0 ? "All wallets are hidden. Use Wallet Management to show them." : "No wallets found. Import or create one first.";
                if (!isManageMode)
                {
                    SetStatus(message);
                }
                var empty = new Label(hiddenCount > 0 ? "All wallets are hidden.\nUse Wallet Management to show them." : "No wallets available.")
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        unityTextAlign = TextAnchor.MiddleCenter,
                        fontSize = 16,
                        marginTop = 10,
                        whiteSpace = WhiteSpace.Normal
                    }
                };
                ApplyDefaultFont(empty);
                list.Add(empty);
                return;
            }

                if (!isManageMode)
                {
                    SetStatus(string.Empty);
                }
            }

        private void Subscribe()
        {
            uiSignals?.EnsureSubscribed();
            if (uiSignals != null)
            {
                uiSignals.SettingsChanged += OnSettingsChanged;
            }
        }

        private void Unsubscribe()
        {
            if (uiSignals != null)
            {
                uiSignals.SettingsChanged -= OnSettingsChanged;
            }
        }

        private void OnSettingsChanged()
        {
            var headerSettings = AccountManager.Instance?.Settings;
            if (headerSettings != null)
            {
                WalletUiCommon.ApplyNetworkBadge(subtitleNetworkLabel, headerSettings.nexusName, headerSettings.nexusKind);
                WalletUiCommon.ApplyNetworkBadge(header.NetworkLabel, headerSettings.nexusName, headerSettings.nexusKind);
            }
        }

        private void BuildLayout(VisualElement host)
        {
            root = host;
            root.Clear();
            // Layout hygiene (keep this to avoid regressions):
            // - minHeight=0 + flexBasis=0 + overflow hidden on wrappers/scroll prevents the list from pushing the footer off-screen.
            // - Keep content centered to mirror the legacy layout proportions.
            WalletUiCommon.ConfigureScreenRoot(root);
            root.style.position = Position.Relative;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;
            root.style.paddingTop = 14;
            root.style.paddingBottom = 14;
            root.style.backgroundColor = Color.clear;
            root.style.color = WalletUiTheme.TextPrimary;
            root.style.alignItems = Align.Stretch;
            root.style.overflow = Overflow.Hidden;
            ApplyDefaultFont(root);
            tabBlockHandler = WalletUiCommon.BlockTabNavigation(root);

            var content = WalletUiCommon.CreateScreenContent(paddingLeft: 8, paddingRight: 8);

            header = WalletUiCommon.BuildHeader("Wallet List");
            var topBar = header.Root;
            topBar.style.flexShrink = 0;
            topBar.style.marginBottom = 12;

            subHeader = WalletUiCommon.BuildSubHeader("Wallet List");
            subtitleLabel = subHeader.SubtitleLabel;
            subtitleNetworkLabel = subHeader.NetworkLabel;
            walletsLabel = subHeader.LeftLabel;
            subHeader.Root.style.marginBottom = 6;
            subHeader.Root.style.flexShrink = 0;

            statusLabel = WalletUiCommon.CreateStatusLabel();
            statusLabel.style.flexShrink = 0;

            listWrapper = WalletUiCommon.BuildScrollContainer(
                out list,
                onScrollChanged: null,
                shouldBlockWheel: () => modalHost?.Overlay != null && modalHost.Overlay.style.display == DisplayStyle.Flex,
                paddingLeft: 6f,
                paddingRight: 6f,
                paddingTop: 8f,
                paddingBottom: 80f,
                marginTop: 4f,
                marginBottom: 12f,
                maxWidth: 0f,
                alignSelf: Align.Stretch);
            list.style.display = DisplayStyle.Flex;
            list.pickingMode = PickingMode.Position;
            list.visible = true;

            if (!rootWheelHooked && root != null)
            {
                // Swallow wheel events during modal display (Unity 1.0 UITK sends wheel to ScrollView even if disabled).
                root.RegisterCallback<WheelEvent>(evt =>
                {
                    if (modalHost?.Overlay != null && modalHost.Overlay.style.display == DisplayStyle.Flex)
                    {
                        evt.StopImmediatePropagation();
                    }
                }, TrickleDown.TrickleDown);
                rootWheelHooked = true;
            }

            content.Add(topBar);
            content.Add(subHeader.Root);
            content.Add(statusLabel);
            content.Add(listWrapper);
            manageRoot = BuildManageRoot();
            manageRoot.style.display = DisplayStyle.None;
            content.Add(manageRoot);
            mainFooter = WalletUiCommon.BuildMainFooter(OnNewWallet, OnManageWallets, OnSettings);
            mainFooter.style.flexShrink = 0;
            content.Add(mainFooter);

            root.Add(content);
        }

        private VisualElement BuildDivider(float height = 8)
        {
            return new VisualElement { style = { height = height } };
        }

        // TODO: remove once main actions are implemented; kept to avoid accidental reuse.

        private VisualElement CreateRow(Account account, int index)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 22,
                    paddingRight = 22,
                    paddingTop = 18,
                    paddingBottom = 18,
                    marginBottom = 18,
                    minHeight = 150,
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
            ApplyDefaultFont(row);

            var text = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1,
                    justifyContent = Justify.Center
                }
            };

            var displayName = string.IsNullOrWhiteSpace(account.name) ? $"Account {index + 1}" : account.name;
            var nameLabel = new Label(displayName.ToUpperInvariant())
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 26,
                    color = WalletUiTheme.TextPrimary
                }
            };
            ApplyDefaultFont(nameLabel);
            text.Add(nameLabel);

            var address = string.IsNullOrWhiteSpace(account.phaAddress) ? "(no address)" : account.phaAddress;
            var addressLabel = new Label(address)
            {
                style =
                {
                    fontSize = 14,
                    marginTop = 6,
                    color = WalletUiTheme.TextMuted
                }
            };
            ApplyDefaultFont(addressLabel);
            text.Add(addressLabel);

            var quickActions = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginTop = 6
                }
            };

            quickActions.Add(MakePillButton("Copy", () => CopyAddress(address)));
            var explorerBtn = MakePillButton("Explorer", () => OpenExplorer(address));
            explorerBtn.style.marginLeft = 6;
            quickActions.Add(explorerBtn);
            text.Add(quickActions);

            var openButton = WalletUiCommon.CreateOutlineButton("Open", () => OnOpenClicked(index), 22, 54);
            openButton.style.minWidth = 140;
            openButton.style.maxWidth = 200;
            openButton.style.paddingLeft = 24;
            openButton.style.paddingRight = 24;
            openButton.style.paddingTop = 14;
            openButton.style.paddingBottom = 14;
            openButton.style.alignSelf = Align.Center;

            row.Add(text);
            row.Add(openButton);

            return row;
        }

        private Button MakePillButton(string text, Action onClick)
        {
            var btn = new Button
            {
                text = text,
                style =
                {
                    fontSize = 12,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 6,
                    paddingBottom = 6,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall,
                    backgroundColor = WalletUiTheme.SecondaryButton,
                    color = WalletUiTheme.TextPrimary,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.SecondaryButtonBorder,
                    borderRightColor = WalletUiTheme.SecondaryButtonBorder,
                    borderTopColor = WalletUiTheme.SecondaryButtonBorder,
                    borderBottomColor = WalletUiTheme.SecondaryButtonBorder,
                    minHeight = 26
                }
            };
            ApplyDefaultFont(btn);
            btn.clicked += () => onClick?.Invoke();
            return btn;
        }

        private void CopyAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address) || address == "(no address)")
            {
                SetStatus("No address to copy.");
                return;
            }

            GUIUtility.systemCopyBuffer = address;
            SetStatus($"Copied {address}.");
            Log.Write($"{LogPrefix}Copied address to clipboard.");
        }

        private void OpenExplorer(string address)
        {
            if (string.IsNullOrWhiteSpace(address) || address == "(no address)")
            {
                SetStatus("No address to open.");
                return;
            }

            var am = AccountManager.Instance;
            var url = am?.GetPhantasmaAddressURL(address);
            if (string.IsNullOrWhiteSpace(url))
            {
                SetStatus("Explorer URL is not configured.");
                Log.WriteWarning($"{LogPrefix}Explorer URL missing for address {address}");
                return;
            }

            Application.OpenURL(url);
            SetStatus("Opening explorer...");
            Log.Write($"{LogPrefix}Opening explorer for {address}: {url}");
        }

        private void OnSettings()
        {
            onShowSettings?.Invoke();
        }

        private async void OnOpenClicked(int index)
        {
            await OpenAccountAtIndexAsync(index, false);
        }

        private async Task OpenAccountAtIndexAsync(int index, bool isNewWallet)
        {
            var am = AccountManager.Instance;
            if (am == null || am.Accounts == null || index < 0 || index >= am.Accounts.Count)
            {
                SetStatus("Account list is not ready.");
                return;
            }

            am.SelectAccount(index);
            context.ViewState.ResetSnapshots();
            context.ViewState.MarkBalancesDirty();

            var promptResult = await authService.RequestPasswordAsync("Open wallet", am.CurrentAccount.platforms, true, true, this);
            Log.Write($"{LogPrefix}Password prompt returned {promptResult} for account '{am.CurrentAccount.name}' (newWallet={isNewWallet}).");
            if (promptResult == PromptResult.Success)
            {
                Log.Write($"{LogPrefix}Account '{am.CurrentAccount.name}' opened, refreshing balances + switching view.");
                if (isNewWallet)
                {
                    am.BlankState();
                }
                else
                {
                    am.RefreshTokenPrices();
                }
                context.BalancePresenter.Refresh(true);
                onLoginSuccess?.Invoke();
            }
            else
            {
                SetStatus($"Failed to open '{am.CurrentAccount.name}'.");
            }
        }

        public async Task<(PromptResult result, string password)> PromptPasswordAsync(string title, string caption, int minLength, int maxLength)
        {
            try
            {
                return await ShowModalAsync(title, caption, minLength, maxLength, isError: false, showInput: true, isPassword: true);
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{LogPrefix}PromptPassword failed for '{title}': {e}");
                return (PromptResult.Failure, string.Empty);
            }
        }

        public async Task ShowErrorAsync(string message)
        {
            await WalletUiModalHelper.ShowErrorAsync(modalHost, "Error", message, DetachListForModal, RestoreListAfterModal);
        }

        private void DetachListForModal()
        {
            if (list == null)
            {
                return;
            }

            listWasEnabled = list.enabledSelf;
            list.SetEnabled(false);
            list.focusable = false;
            if (list.parent != null)
            {
                listDetachedForModal = true;
                listIndexBeforeDetach = list.parent.IndexOf(list);
                Log.Write($"{LogPrefix}Detaching list for modal. parentChildren={list.parent.childCount} idx={listIndexBeforeDetach}");
                list.RemoveFromHierarchy();
            }
            listPickingModeBeforeModal = list.pickingMode;
            list.style.display = DisplayStyle.None;
        }

        private void RestoreListAfterModal()
        {
            if (list == null)
            {
                return;
            }

            list.SetEnabled(listWasEnabled);
            list.focusable = true;
            if (listDetachedForModal && listWrapper != null)
            {
                var idx = listIndexBeforeDetach >= 0 ? Mathf.Min(listIndexBeforeDetach, listWrapper.childCount) : listWrapper.childCount;
                listWrapper.Insert(idx, list);
                Log.Write($"{LogPrefix}Reattached list after modal. targetIdx={idx} children={listWrapper.childCount}");
            }
            listDetachedForModal = false;
            listIndexBeforeDetach = -1;
            list.pickingMode = listPickingModeBeforeModal;
            list.visible = true;
            list.style.display = DisplayStyle.Flex;
        }

        private Task<(PromptResult result, string input)> ShowModalAsync(string title, string caption, int minLength, int maxLength, bool isError = false, bool showInput = true, bool isPassword = true, bool multiline = false, string primaryLabel = null, string secondaryLabel = null, string initialValue = "")
        {
            var primary = string.IsNullOrWhiteSpace(primaryLabel) ? (isError ? "Close" : "OK") : primaryLabel;
            var secondary = string.IsNullOrWhiteSpace(secondaryLabel) ? "Cancel" : secondaryLabel;

            return WalletUiModalHelper.ShowPromptAsync(
                modalHost,
                title,
                caption,
                minLength,
                maxLength,
                allowEmpty: isError,
                hasInput: showInput,
                isPassword: isPassword,
                multiline: multiline,
                primaryLabel: primary,
                secondaryLabel: secondary,
                showSecondary: true,
                initialValue: initialValue ?? string.Empty,
                successResult: isError ? PromptResult.Failure : PromptResult.Success,
                cancelResult: PromptResult.Failure,
                onBeforeShow: DetachListForModal,
                onAfterHide: RestoreListAfterModal);
        }

        private async Task ShowErrorWithStatusAsync(string message, string statusAfterClose = null, bool singleButton = true)
        {
            await WalletUiModalHelper.ShowErrorAsync(modalHost, "Error", message, DetachListForModal, RestoreListAfterModal, showSecondary: !singleButton);
            if (!string.IsNullOrWhiteSpace(statusAfterClose))
            {
                SetStatus(statusAfterClose);
            }
        }

        private void ShowPanel(VisualElement panel)
        {
            modalHost.ShowPanel(panel, DetachListForModal);
        }

        private void HidePanel()
        {
            modalHost.HidePanel(RestoreListAfterModal);
        }

        private void HideModal()
        {
            modalHost.HideAll();
            RestoreListAfterModal();
        }

        private void EnterManageMode()
        {
            manageSelection.Clear();
            manageDirty = false;
            manageOriginalAccounts = CloneAccounts(AccountManager.Instance?.Accounts);
            manageOriginalHidden = CloneHiddenAddresses(AccountManager.Instance?.HiddenPhantasmaAddresses);
            manageHiddenWorking = CloneHiddenAddresses(AccountManager.Instance?.HiddenPhantasmaAddresses);
            if (manageOriginalAccounts == null || manageOriginalHidden == null || manageHiddenWorking == null)
            {
                Log.WriteWarning($"{LogPrefix}Cannot open wallet management: failed to snapshot accounts or hidden list.");
                SetStatus("Cannot open wallet management right now. Please try again.");
                return;
            }
            if (statusLabel != null)
            {
                WalletUiCommon.UpdateStatusLabel(statusLabel, string.Empty);
            }
            if (listWrapper != null)
            {
                listWrapper.style.display = DisplayStyle.None;
            }
            if (mainFooter != null)
            {
                mainFooter.style.display = DisplayStyle.None;
            }
            if (manageRoot != null)
            {
                manageRoot.style.display = DisplayStyle.Flex;
            }

            subtitleLabel.text = "Wallet Management";
            RefreshManagePanel();
        }

        private void ExitManageMode()
        {
            if (manageDirty && manageOriginalAccounts != null && AccountManager.Instance != null)
            {
                AccountManager.Instance.Accounts.Clear();
                AccountManager.Instance.Accounts.AddRange(CloneAccounts(manageOriginalAccounts));
                if (manageOriginalHidden != null)
                {
                    AccountManager.Instance.ApplyHiddenWallets(manageOriginalHidden, false);
                    manageHiddenWorking = CloneHiddenAddresses(manageOriginalHidden);
                }
                manageDirty = false;
                Refresh();
                SetStatus("Changes discarded.");
            }
            manageSelection.Clear();
            UpdateManageStatus(string.Empty);
            manageHiddenWorking = null;
            manageOriginalHidden = null;
            if (manageRoot != null)
            {
                manageRoot.style.display = DisplayStyle.None;
            }
            if (listWrapper != null)
            {
                listWrapper.style.display = DisplayStyle.Flex;
            }
            if (mainFooter != null)
            {
                mainFooter.style.display = DisplayStyle.Flex;
            }

            subtitleLabel.text = "Wallet List";
            Refresh();
        }

        private void SetStatus(string text)
        {
            if (manageRoot != null && manageRoot.style.display == DisplayStyle.Flex && manageStatusLabel != null)
            {
                WalletUiCommon.UpdateStatusLabel(manageStatusLabel, text);
                return;
            }

            WalletUiCommon.UpdateStatusLabel(statusLabel, text);
        }

        private void ApplyDefaultFont(VisualElement element)
        {
            WalletUiCommon.ApplyDefaultFont(element);
        }
    }
}



