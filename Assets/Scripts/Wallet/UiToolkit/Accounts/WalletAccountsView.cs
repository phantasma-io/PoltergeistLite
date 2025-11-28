using System;
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
        private WalletUiSignals uiSignals;
        private VisualElement modalOverlay;
        private Action<PromptResult, string> modalCallback;
        private bool listWasEnabled = true;
        private bool rootWheelHooked;
        private bool listTemporarilyHidden;
        private bool listDetachedForModal;
        private int listIndexBeforeDetach = -1;
        private PickingMode listPickingModeBeforeModal;
        private bool listVisibilityBeforeModal;
        private EventCallback<KeyUpEvent> modalKeyHandler;

        public WalletAccountsView(VisualElement host, WalletApplicationContext context, Action onLoginSuccess, Action onShowSettings)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            authService = context.AuthService ?? throw new ArgumentNullException(nameof(context.AuthService));
            this.onLoginSuccess = onLoginSuccess;
            this.onShowSettings = onShowSettings ?? throw new ArgumentNullException(nameof(onShowSettings));
            uiSignals = context.UiSignals;

            BuildLayout(host ?? throw new ArgumentNullException(nameof(host)));
            Subscribe();
        }

        public void Dispose()
        {
            Unsubscribe();
            HideModal();
        }

        public void Refresh()
        {
            var am = AccountManager.Instance;
            list.Clear();

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
                SetStatus("No wallets found. Import or create one first.");
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

            walletsLabel.text = $"{am.Accounts.Count} wallet(s)";
            SetStatus(string.Empty);
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
                list.Add(CreateRow(am.Accounts[i], i));
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

        private void SetStatus(string text)
        {
            statusLabel.text = text ?? string.Empty;
            statusLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
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

            statusLabel = new Label(string.Empty)
            {
                style =
                {
                    fontSize = 13,
                    color = WalletUiTheme.TextSecondary,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 8
                }
            };
            ApplyDefaultFont(statusLabel);
            statusLabel.style.display = DisplayStyle.None;
            statusLabel.style.flexShrink = 0;

            listWrapper = WalletUiCommon.BuildScrollContainer(
                out list,
                onScrollChanged: null,
                shouldBlockWheel: () => modalOverlay != null && modalOverlay.style.display == DisplayStyle.Flex,
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

            modalOverlay = WalletUiCommon.CreateModalOverlay();
            modalOverlay.RegisterCallback<WheelEvent>(evt => evt.StopPropagation());
            modalOverlay.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            modalOverlay.RegisterCallback<PointerMoveEvent>(evt => evt.StopPropagation());

            if (!rootWheelHooked && root != null)
            {
                // Swallow wheel events during modal display (Unity 1.0 UITK sends wheel to ScrollView even if disabled).
                root.RegisterCallback<WheelEvent>(evt =>
                {
                    if (modalOverlay != null && modalOverlay.style.display == DisplayStyle.Flex)
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
            var footer = WalletUiCommon.BuildMainFooter(OnNewWallet, OnImportWallet, OnManageWallets, OnSettings);
            footer.style.flexShrink = 0;
            content.Add(footer);

            root.Add(content);
            root.Add(modalOverlay);
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

        private void OnImportWallet()
        {
            SetStatus("Import flow is not yet available in UITK.");
            Log.Write($"{LogPrefix}Import wallet action pressed (not implemented).");
        }

        private void OnManageWallets()
        {
            SetStatus("Manage wallets is not yet available in UITK.");
            Log.Write($"{LogPrefix}Manage wallets action pressed (not implemented).");
        }

        private void OnSettings()
        {
            onShowSettings?.Invoke();
        }

        private void OnOpenClicked(int index)
        {
            OpenAccountAtIndex(index, false);
        }

        private void OpenAccountAtIndex(int index, bool isNewWallet)
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

            authService.RequestPassword("Open wallet", am.CurrentAccount.platforms, true, true, this, result =>
            {
                Log.Write($"{LogPrefix}Password prompt returned {result} for account '{am.CurrentAccount.name}' (newWallet={isNewWallet}).");
                if (result == PromptResult.Success)
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
            });
        }

        public void PromptPassword(string title, string caption, int minLength, int maxLength, Action<PromptResult, string> callback)
        {
            ShowModal(title, caption, minLength, maxLength, callback);
        }

        public void ShowError(string message, Action onClosed)
        {
            ShowModal("Error", message, 0, 0, (result, _) => onClosed?.Invoke(), isError: true);
        }

        private void EnsureModalOverlayParent()
        {
            if (modalOverlay != null && root?.parent != null && modalOverlay.parent != root.parent)
            {
                modalOverlay.RemoveFromHierarchy();
                root.parent.Add(modalOverlay);
            }
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
            listVisibilityBeforeModal = list.visible;
            list.style.display = DisplayStyle.None; // extra guard against wheel during modal
        }

        private VisualElement BeginModalSession(Action<PromptResult, string> callback)
        {
            HideModal();
            modalCallback = callback;

            EnsureModalOverlayParent();
            DetachListForModal();

            if (modalOverlay != null)
            {
                modalOverlay.style.display = DisplayStyle.Flex;
                modalOverlay.Clear();
            }

            return modalOverlay;
        }

        private void ShowModal(string title, string caption, int minLength, int maxLength, Action<PromptResult, string> callback, bool isError = false, bool showInput = true, bool isPassword = true, bool multiline = false, string primaryLabel = null, string secondaryLabel = null, string initialValue = "")
        {
            if (callback == null)
            {
                Log.WriteWarning($"{LogPrefix}ShowModal '{title}' missing callback, aborting modal.");
                SetStatus("Could not open dialog, please try again.");
                return;
            }

            var overlay = BeginModalSession(callback);
            if (overlay == null)
            {
                callback(PromptResult.Failure, string.Empty);
                return;
            }

            var handler = callback;

            void CloseAs(PromptResult result, string input)
            {
                var cb = modalCallback;
                Log.Write($"{LogPrefix}CloseAs title='{title}' result={result} inputLen={(input?.Length ?? 0)} cbNull={cb == null}");
                HideModal(keepCallback: true);
                try
                {
                    handler(result, input);
                }
                catch (Exception e)
                {
                    Log.WriteWarning($"{LogPrefix}Modal callback exception for '{title}': {e}");
                    SetStatus("Something went wrong, please try again.");
                }
                modalCallback = null;
            }

            var panel = WalletUiCommon.CreateModalPanel(720, 900);

            var titleLabel = new Label(title ?? string.Empty)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 20,
                    marginBottom = 10,
                    color = WalletUiTheme.TextPrimary
                }
            };
            ApplyDefaultFont(titleLabel);
            panel.Add(titleLabel);

            var bodyLabel = new Label(caption ?? string.Empty)
            {
                style =
                {
                    marginBottom = 12,
                    whiteSpace = WhiteSpace.Normal,
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 16
                }
            };
            ApplyDefaultFont(bodyLabel);
            panel.Add(bodyLabel);

            var validationLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 13,
                    marginBottom = 8,
                    whiteSpace = WhiteSpace.Normal,
                    display = DisplayStyle.None
                }
            };
            ApplyDefaultFont(validationLabel);
            panel.Add(validationLabel);

            TextField passwordField = null;
            if (!isError && showInput)
            {
                passwordField = new TextField
                {
                    isPasswordField = isPassword,
                    maskChar = isPassword ? '*' : '\0',
                    maxLength = maxLength > 0 ? maxLength : int.MaxValue,
                    multiline = multiline
                };
                WalletUiCommon.StyleModalInput(passwordField, multiline, multiline ? 80 : 40);
                if (!string.IsNullOrEmpty(initialValue))
                {
                    passwordField.value = initialValue;
                }
                passwordField.schedule.Execute(() => passwordField.Focus()).StartingIn(50);
                panel.Add(passwordField);
            }
            panel.focusable = true;
            panel.pickingMode = PickingMode.Position;

            var buttonRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.FlexEnd,
                    marginTop = 6
                }
            };

            var cancel = WalletUiCommon.CreateSecondaryButton(string.IsNullOrWhiteSpace(secondaryLabel) ? "Cancel" : secondaryLabel, () => CloseAs(PromptResult.Failure, string.Empty), 16, 36);
            cancel.style.minWidth = 110;

            Action submitAction = () =>
            {
                var input = passwordField?.text ?? string.Empty;
                Log.Write($"{LogPrefix}SubmitAction title='{title}' inputLen={input.Length} minLen={minLength} showInput={showInput}");
                if (!isError && showInput && minLength > 0 && input.Length < minLength)
                {
                    Log.Write($"{LogPrefix}Submit rejected: len={input.Length} minLen={minLength}");
                    var inputKind = isPassword ? "Password" : "Input";
                    validationLabel.text = $"{inputKind} must be at least {minLength} characters.";
                    validationLabel.style.display = DisplayStyle.Flex;
                    passwordField?.Focus();
                    return;
                }

                CloseAs(isError ? PromptResult.Failure : PromptResult.Success, input);
            };
            var ok = WalletUiCommon.CreateOutlineButton(string.IsNullOrWhiteSpace(primaryLabel) ? (isError ? "Close" : "OK") : primaryLabel, submitAction, 16, 36);
            ok.style.marginLeft = 10;
            ok.style.minWidth = 110;

            EventCallback<KeyUpEvent> keyHandler = evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    Log.Write($"{LogPrefix}Enter pressed in modal. textLen={(passwordField?.text?.Length ?? 0)} minLen={minLength}");
                    submitAction();
                    evt.StopImmediatePropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    Log.Write($"{LogPrefix}Escape pressed in modal.");
                    CloseAs(PromptResult.Failure, string.Empty);
                    evt.StopImmediatePropagation();
                }
            };
            panel.RegisterCallback<KeyUpEvent>(keyHandler, TrickleDown.TrickleDown);
            UnregisterModalKeyHandler();
            modalKeyHandler = keyHandler;
            modalOverlay.RegisterCallback<KeyUpEvent>(modalKeyHandler, TrickleDown.TrickleDown);

            buttonRow.Add(cancel);
            buttonRow.Add(ok);
            panel.Add(buttonRow);

            overlay.Add(panel);
            panel.schedule.Execute(() => panel.Focus()).StartingIn(10);
            Log.Write($"{LogPrefix}ShowModal '{title}' isError={isError} minLen={minLength} maxLen={maxLength} detached={listDetachedForModal}");
        }

        private void UnregisterModalKeyHandler()
        {
            if (modalOverlay != null && modalKeyHandler != null)
            {
                modalOverlay.UnregisterCallback<KeyUpEvent>(modalKeyHandler, TrickleDown.TrickleDown);
                modalKeyHandler = null;
            }
        }

        private void HideModal()
        {
            HideModal(keepCallback: false);
        }

        private void HideModal(bool keepCallback)
        {
            UnregisterModalKeyHandler();
            modalOverlay.style.display = DisplayStyle.None;
            modalOverlay.Clear();
            if (list != null)
            {
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
                list.visible = true; // always restore visibility after modal
                list.style.display = DisplayStyle.Flex;
            }
            if (!keepCallback)
            {
                modalCallback = null;
            }
            Log.Write($"{LogPrefix}HideModal complete. modal children={modalOverlay.childCount} listEnabled={list?.enabledSelf} listVisible={list?.visible}");
        }

        private void ApplyDefaultFont(VisualElement element)
        {
            WalletUiCommon.ApplyDefaultFont(element);
        }
    }
}



