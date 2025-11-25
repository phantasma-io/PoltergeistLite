using System;
using System.Linq;
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
    public sealed class WalletAccountsView : IWalletAuthUi, IDisposable
    {
        private const string LogPrefix = "[UITK] ";

        private readonly WalletApplicationContext context;
        private readonly WalletAuthService authService;
        private readonly Action onLoginSuccess;
        private HeaderElements header;
        private SubHeaderElements subHeader;
        private Label subtitleLabel;
        private Label subtitleNetworkLabel;
        private Label walletsLabel;
        private VisualElement root;
        private ScrollView list;
        private VisualElement listWrapper;
        private Label statusLabel;
        private VisualElement modalOverlay;
        private VisualElement footerBar;
        private Action<PromptResult, string> modalCallback;
        private bool listWasEnabled = true;
        private bool rootWheelHooked;
        private bool listTemporarilyHidden;
        private bool listDetachedForModal;
        private int listIndexBeforeDetach = -1;
        private PickingMode listPickingModeBeforeModal;
        private bool listVisibilityBeforeModal;
        private EventCallback<KeyUpEvent> modalKeyHandler;

        public WalletAccountsView(VisualElement host, WalletApplicationContext context, Action onLoginSuccess)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            authService = context.AuthService ?? throw new ArgumentNullException(nameof(context.AuthService));
            this.onLoginSuccess = onLoginSuccess;

            BuildLayout(host ?? throw new ArgumentNullException(nameof(host)));
            Refresh();
        }

        public void Dispose()
        {
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
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1;
            root.style.height = new Length(100, LengthUnit.Percent);
            root.style.minHeight = 0;
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

            var content = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    width = new Length(100, LengthUnit.Percent),
                    maxWidth = 1680,
                    alignSelf = Align.Center,
                    paddingLeft = 8,
                    paddingRight = 8,
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = 0,
                    minHeight = 0
                }
            };
            ApplyDefaultFont(content);

            header = WalletUiCommon.BuildHeader("Wallet List");
            var topBar = header.Root;
            topBar.style.marginBottom = 12;

            subHeader = WalletUiCommon.BuildSubHeader("Wallet List");
            subtitleLabel = subHeader.SubtitleLabel;
            subtitleNetworkLabel = subHeader.NetworkLabel;
            walletsLabel = subHeader.LeftLabel;
            subHeader.Root.style.marginBottom = 6;

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

            list = new ScrollView(ScrollViewMode.Vertical)
            {
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = 0,
                    minHeight = 0,
                    backgroundColor = Color.clear,
                    backgroundImage = new StyleBackground(),
                    paddingLeft = 6,
                    paddingRight = 6,
                    paddingTop = 8,
                    paddingBottom = 8,
                    marginTop = 4,
                    marginBottom = 12,
                    overflow = Overflow.Hidden
                }
            };
            list.style.display = DisplayStyle.Flex;
            list.pickingMode = PickingMode.Position;
            list.visible = true;
            list.verticalScrollerVisibility = ScrollerVisibility.Auto;
            list.contentContainer.style.paddingBottom = 80;
            // Manual wheel handling avoids UITK's ScrollView null deref in ReadSingleLineHeight and keeps modal wheel swallowed.
            list.RegisterCallback<WheelEvent>(evt =>
            {
                if (modalOverlay != null && modalOverlay.style.display == DisplayStyle.Flex)
                {
                    evt.StopImmediatePropagation();
                    evt.PreventDefault();
                    return;
                }

                var scroller = list.verticalScroller;
                if (scroller == null || list.contentContainer == null)
                {
                    evt.StopImmediatePropagation();
                    evt.PreventDefault();
                    return;
                }

                const float scrollStep = 120f;
                var delta = Mathf.Clamp(evt.delta.y, -1f, 1f);
                var low = scroller.lowValue;
                var high = scroller.highValue;
                // High value can be stale before layout; recompute a safer fallback using bounds when available.
                if ((double)high <= (double)low)
                {
                    var viewportHeight = list.contentViewport?.worldBound.height ?? 0f;
                    var contentHeight = list.contentContainer.worldBound.height;
                    if (viewportHeight > 0f && contentHeight > viewportHeight)
                    {
                        high = contentHeight - viewportHeight;
                    }
                }

                if (high < low)
                {
                    high = low;
                }

                // UI Toolkit delta is positive when scrolling down; add delta to move the view in the same direction.
                var target = Mathf.Clamp(scroller.value + delta * scrollStep, low, high);
                scroller.value = target;
                var offset = list.scrollOffset;
                offset.y = target;
                list.scrollOffset = offset;
                evt.StopImmediatePropagation();
                evt.PreventDefault();
            }, TrickleDown.TrickleDown);
            ApplyDefaultFont(list);

            listWrapper = new VisualElement
            {
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = 0,
                    minHeight = 0,
                    flexDirection = FlexDirection.Column,
                    overflow = Overflow.Hidden
                }
            };
            listWrapper.Add(list);

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
            footerBar = BuildFooterBar();
            content.Add(footerBar);

            root.Add(content);
            root.Add(modalOverlay);
        }

        private VisualElement BuildFooterBar()
        {
            var bar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.SpaceBetween,
                    alignItems = Align.Center,
                    paddingTop = 14,
                    paddingBottom = 14,
                    paddingLeft = 12,
                    paddingRight = 12,
                    marginTop = 14,
                    marginBottom = 6,
                    minHeight = 72,
                    backgroundColor = WalletUiTheme.HeaderBackground,
                    borderTopWidth = 1,
                    borderTopColor = WalletUiTheme.HeaderBorder,
                    borderBottomWidth = 1,
                    borderBottomColor = WalletUiTheme.HeaderBorder,
                    borderLeftWidth = 1,
                    borderLeftColor = WalletUiTheme.HeaderBorder,
                    borderRightWidth = 1,
                    borderRightColor = WalletUiTheme.HeaderBorder,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium,
                    flexShrink = 0
                }
            };

            bar.Add(MakeActionButton("New wallet", OnNewWallet));
            bar.Add(MakeActionButton("Import", OnImportWallet));
            bar.Add(MakeActionButton("Manage", OnManageWallets));
            bar.Add(MakeActionButton("Settings", OnSettings));

            var children = bar.Children().ToList();
            for (var i = 0; i < children.Count; i++)
            {
                children[i].style.flexGrow = 1;
                children[i].style.marginLeft = i == 0 ? 0 : 8;
            }

            return bar;
        }

        private VisualElement BuildDivider(float height = 8)
        {
            return new VisualElement { style = { height = height } };
        }

        private Button MakeActionButton(string text, Action onClick)
        {
            var btn = new Button
            {
                text = text,
                style =
                {
                    backgroundColor = WalletUiTheme.SecondaryButton,
                    color = Color.white,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 22,
                    minHeight = 56,
                    paddingLeft = 20,
                    paddingRight = 20,
                    paddingTop = 14,
                    paddingBottom = 14,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.SecondaryButtonBorder,
                    borderRightColor = WalletUiTheme.SecondaryButtonBorder,
                    borderTopColor = WalletUiTheme.SecondaryButtonBorder,
                    borderBottomColor = WalletUiTheme.SecondaryButtonBorder,
                    flexGrow = 1
                }
            };
            ApplyDefaultFont(btn);
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.clicked += () => onClick?.Invoke();
            return btn;
        }

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

        private void OnNewWallet()
        {
            SetStatus("New wallet flow is not yet available in UITK.");
            Log.Write($"{LogPrefix}New wallet action pressed (not implemented).");
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
            SetStatus("Settings are not yet available in UITK.");
            Log.Write($"{LogPrefix}Settings action pressed (not implemented).");
        }

        private void OnOpenClicked(int index)
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
                Log.Write($"{LogPrefix}Password prompt returned {result} for account '{am.CurrentAccount.name}'.");
                if (result == PromptResult.Success)
                {
                    Log.Write($"{LogPrefix}Account '{am.CurrentAccount.name}' opened, refreshing balances + switching view.");
                    am.RefreshTokenPrices();
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

        private void ShowModal(string title, string caption, int minLength, int maxLength, Action<PromptResult, string> callback, bool isError = false)
        {
            HideModal();
            modalCallback = callback;

            if (modalOverlay != null && root?.parent != null && modalOverlay.parent != root.parent)
            {
                modalOverlay.RemoveFromHierarchy();
                root.parent.Add(modalOverlay);
            }

            if (list != null)
            {
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
            modalOverlay.style.display = DisplayStyle.Flex;
            modalOverlay.Clear();

            void CloseAs(PromptResult result, string input)
            {
                var cb = modalCallback;
                HideModal(keepCallback: true);
                cb?.Invoke(result, input);
                modalCallback = null;
            }

            var panel = new VisualElement
            {
                style =
                {
                    width = 720,
                    maxWidth = 900,
                    backgroundColor = WalletUiTheme.ModalBackground,
                    paddingLeft = 22,
                    paddingRight = 22,
                    paddingTop = 18,
                    paddingBottom = 18,
                    borderTopLeftRadius = WalletUiTheme.RadiusLarge,
                    borderTopRightRadius = WalletUiTheme.RadiusLarge,
                    borderBottomLeftRadius = WalletUiTheme.RadiusLarge,
                    borderBottomRightRadius = WalletUiTheme.RadiusLarge,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.ModalBorder,
                    borderRightColor = WalletUiTheme.ModalBorder,
                    borderTopColor = WalletUiTheme.ModalBorder,
                    borderBottomColor = WalletUiTheme.ModalBorder
                }
            };

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

            TextField passwordField = null;
            if (!isError)
            {
                passwordField = new TextField
                {
                    isPasswordField = true,
                    maskChar = '*',
                    maxLength = maxLength > 0 ? maxLength : int.MaxValue,
                    style =
                    {
                        marginBottom = 12,
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
                        paddingLeft = 8,
                        paddingRight = 8,
                        paddingTop = 6,
                        paddingBottom = 6
                    }
                };
                ApplyDefaultFont(passwordField);
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

            var cancel = new Button { text = "Cancel" };
            ApplyDefaultFont(cancel);
            cancel.style.backgroundColor = WalletUiTheme.SecondaryButton;
            cancel.style.color = WalletUiTheme.TextPrimary;
            cancel.style.borderLeftWidth = 1;
            cancel.style.borderRightWidth = 1;
            cancel.style.borderTopWidth = 1;
            cancel.style.borderBottomWidth = 1;
            cancel.style.borderLeftColor = WalletUiTheme.SecondaryButtonBorder;
            cancel.style.borderRightColor = WalletUiTheme.SecondaryButtonBorder;
            cancel.style.borderTopColor = WalletUiTheme.SecondaryButtonBorder;
            cancel.style.borderBottomColor = WalletUiTheme.SecondaryButtonBorder;
            cancel.style.paddingLeft = 14;
            cancel.style.paddingRight = 14;
            cancel.style.paddingTop = 8;
            cancel.style.paddingBottom = 8;
            Action cancelAction = () => CloseAs(PromptResult.Failure, string.Empty);
            cancel.clicked += () =>
            {
                cancelAction();
            };

            var ok = new Button { text = isError ? "Close" : "OK" };
            ApplyDefaultFont(ok);
            ok.style.marginLeft = 8;
            ok.style.backgroundColor = WalletUiTheme.ActionButton;
            ok.style.color = WalletUiTheme.ActionButtonText;
            ok.style.borderLeftWidth = 1;
            ok.style.borderRightWidth = 1;
            ok.style.borderTopWidth = 1;
            ok.style.borderBottomWidth = 1;
            ok.style.borderLeftColor = WalletUiTheme.ActionButtonBorder;
            ok.style.borderRightColor = WalletUiTheme.ActionButtonBorder;
            ok.style.borderTopColor = WalletUiTheme.ActionButtonBorder;
            ok.style.borderBottomColor = WalletUiTheme.ActionButtonBorder;
            ok.style.paddingLeft = 16;
            ok.style.paddingRight = 16;
            ok.style.paddingTop = 8;
            ok.style.paddingBottom = 8;
            Action submitAction = () =>
            {
                var input = passwordField?.text ?? string.Empty;
                if (!isError && input.Length < minLength)
                {
                    Log.Write($"{LogPrefix}Submit rejected: len={input.Length} minLen={minLength}");
                    SetStatus($"Password must be at least {minLength} chars.");
                    return;
                }

                CloseAs(isError ? PromptResult.Failure : PromptResult.Success, input);
            };
            ok.clicked += () => submitAction();

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
                    cancelAction();
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

            modalOverlay.Add(panel);
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



