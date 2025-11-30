using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using PhantasmaPhoenix.Core;
using Poltergeist;
using Poltergeist.Wallet;
using Poltergeist.UiToolkit;
using PhantasmaPhoenix.Unity.Core.Logging;

namespace Poltergeist.UiToolkit.Accounts
{
    /// <summary>
    /// Wallet management actions for UITK (export/import/reorder/rename/delete) full-screen mode.
    /// Kept separate from the main view for readability.
    /// </summary>
    public sealed partial class WalletAccountsView
    {
        private VisualElement managePanel;
        private ScrollView manageList;
        private Label manageStatusLabel;
        private VisualElement manageFooter;
        private VisualElement manageActionsCloud;
        private Button manageRenameButton;
        private Button manageMoveUpButton;
        private Button manageMoveDownButton;
        private Button manageHideButton;
        private Button manageUnhideButton;
        private Button manageDeleteButton;
        private List<Account> manageOriginalAccounts;
        private HashSet<string> manageHiddenWorking;
        private HashSet<string> manageOriginalHidden;
        private bool manageDirty;
        private readonly HashSet<string> manageSelection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void OnManageWallets()
        {
            var am = AccountManager.Instance;
            if (am == null || am.Accounts == null)
            {
                SetStatus("Account list is not ready.");
                return;
            }

            ShowManagePanel();
        }

        private void ShowManagePanel()
        {
            EnterManageMode();
        }

        private void HideManagePanel()
        {
            ExitManageMode();
        }

        private VisualElement BuildManageRoot()
        {
            // Full-height container keeps manage layout aligned with other screens (scroll + footer positions).
            var root = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = 0,
                    minHeight = 0,
                    width = new Length(100, LengthUnit.Percent),
                    alignItems = Align.Stretch,
                    backgroundColor = Color.clear
                }
            };
            ApplyDefaultFont(root);

            // Body card with scroll area; keeps footer separate from scrolling just like other screens.
            var panel = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = 0,
                    minHeight = 0,
                    alignSelf = Align.Center,
                    width = new Length(100, LengthUnit.Percent),
                    maxWidth = 1680,
                    paddingLeft = 18,
                    paddingRight = 18,
                    paddingTop = 12,
                    paddingBottom = 12,
                    backgroundColor = WalletUiTheme.CardBackground,
                    backgroundImage = new StyleBackground(WalletUiTheme.GetCardGradientTexture()),
                    unityBackgroundScaleMode = ScaleMode.StretchToFill,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium
                }
            };
            ApplyDefaultFont(panel);

            manageStatusLabel = WalletUiCommon.CreateStatusLabel(TextAnchor.MiddleCenter, Align.Center);
            manageStatusLabel.style.width = new Length(100, LengthUnit.Percent);
            manageStatusLabel.style.maxWidth = 1680;

            var caption = new Label("Rename, reorder, import/export or delete wallets on this device.")
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 14,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 8,
                    whiteSpace = WhiteSpace.Normal
                }
            };
            ApplyDefaultFont(caption);
            panel.Add(caption);

            var scrollWrapper = WalletUiCommon.BuildScrollContainer(
                out manageList,
                onScrollChanged: null,
                shouldBlockWheel: () => modalHost?.Overlay != null && modalHost.Overlay.style.display == DisplayStyle.Flex,
                paddingLeft: 8f,
                paddingRight: 8f,
                paddingTop: 6f,
                paddingBottom: 0f,
                marginTop: 6f,
                marginBottom: 0f,
                maxWidth: 0f,
                alignSelf: Align.Stretch);
            manageList.style.flexGrow = 1;
            manageList.style.flexShrink = 1;
            manageList.style.minHeight = 0; // Allow the list to stretch so the footer sits at the same height as other screens.
            panel.Add(scrollWrapper);

            root.Add(manageStatusLabel);
            root.Add(panel);

            var actionsContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    width = new Length(100, LengthUnit.Percent),
                    maxWidth = 1680,
                    alignSelf = Align.Center,
                    flexShrink = 0,
                    marginTop = 10,
                    marginBottom = 8
                }
            };
            WalletUiCommon.ApplyDefaultFont(actionsContainer);

            var renameBtn = WalletUiCommon.CreateSecondaryButton("Rename", () => RenameSelectedAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Rename selected failed: {ex}")), 14, 32);
            manageRenameButton = renameBtn;
            var moveUpBtn = WalletUiCommon.CreateSecondaryButton("Up", () => MoveSelectedAsync(-1).Forget(ex => Log.WriteWarning($"{LogPrefix}Move up failed: {ex}")), 14, 32);
            manageMoveUpButton = moveUpBtn;
            var moveDownBtn = WalletUiCommon.CreateSecondaryButton("Down", () => MoveSelectedAsync(1).Forget(ex => Log.WriteWarning($"{LogPrefix}Move down failed: {ex}")), 14, 32);
            manageMoveDownButton = moveDownBtn;
            var hideBtn = WalletUiCommon.CreateSecondaryButton("Hide", () => ApplyHiddenSelection(true), 14, 32);
            manageHideButton = hideBtn;
            var unhideBtn = WalletUiCommon.CreateSecondaryButton("Unhide", () => ApplyHiddenSelection(false), 14, 32);
            manageUnhideButton = unhideBtn;
            var deleteBtn = WalletUiCommon.CreateSecondaryButton("Delete", () => DeleteSelectedWalletsAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Delete failed: {ex}")), 14, 32);
            manageDeleteButton = deleteBtn;
            manageActionsCloud = WalletUiCommon.CreateButtonRow(8f, renameBtn, moveUpBtn, moveDownBtn, hideBtn, unhideBtn, deleteBtn);
            actionsContainer.Add(manageActionsCloud);

            root.Add(actionsContainer);

            manageFooter = WalletUiCommon.BuildFooter(
                out _,
                ("Import", () => OnManageImportClickedAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Import failed: {ex}"))),
                ("Export", () => ExportSelectedWalletsAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Export failed: {ex}"))),
                ("Revert", HideManagePanel),
                ("Apply", SaveAccounts)
            );
            manageFooter.style.alignSelf = Align.Center;
            manageFooter.style.width = new Length(100, LengthUnit.Percent);
            manageFooter.style.maxWidth = 1680;
            manageFooter.style.marginTop = 12;

            root.Add(manageFooter);
            return root;
        }

        private void RefreshManagePanel()
        {
            var am = AccountManager.Instance;
            manageList?.Clear();
            if (am == null || am.Accounts == null || am.Accounts.Count == 0)
            {
                UpdateManageStatus("No wallets to manage.");
                UpdateManageActionsState();
                return;
            }

            UpdateManageStatus(string.Empty);
            for (var i = 0; i < am.Accounts.Count; i++)
            {
                var account = am.Accounts[i];
                manageList.Add(CreateManageRow(account, i));
            }
            UpdateManageActionsState();
        }

        private VisualElement CreateManageRow(Account account, int index)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexStart,
                    flexShrink = 0,
                    paddingTop = 8,
                    paddingBottom = 8,
                    paddingLeft = 10,
                    paddingRight = 10,
                    marginBottom = 4,
                    backgroundColor = WalletUiTheme.CardBackground,
                    borderBottomWidth = 1,
                    borderBottomColor = WalletUiTheme.HeaderBorder
                }
            };
            ApplyDefaultFont(row);
            var isHidden = manageHiddenWorking != null && manageHiddenWorking.Contains(account.phaAddress);

            var info = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1,
                    marginLeft = 10
                }
            };
            ApplyDefaultFont(info);
            if (isHidden)
            {
                row.style.opacity = 0.7f;
            }

            var toggle = WalletUiFormFactory.CreateToggle(string.Empty, manageSelection.Contains(account.phaAddress), newValue =>
            {
                if (newValue)
                {
                    manageSelection.Add(account.phaAddress);
                }
                else
                {
                    manageSelection.RemoveWhere(x => string.Equals(x, account.phaAddress, StringComparison.OrdinalIgnoreCase));
                }
                UpdateManageActionsState();
            });
            toggle.style.marginLeft = 4;
            row.Add(toggle);

            var nameLabel = new Label($"{index + 1}. {account.name}")
            {
                style =
                {
                    color = isHidden ? WalletUiTheme.TextMuted : WalletUiTheme.TextPrimary,
                    fontSize = 15,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            ApplyDefaultFont(nameLabel);
            var nameRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexStart
                }
            };
            ApplyDefaultFont(nameRow);
            nameRow.Add(nameLabel);
            if (isHidden)
            {
                var hiddenBadge = CreateHiddenBadge();
                hiddenBadge.style.marginLeft = 8;
                nameRow.Add(hiddenBadge);
            }
            info.Add(nameRow);

            var addressLabel = new Label(account.phaAddress)
            {
                style =
                {
                    color = isHidden ? WalletUiTheme.TextMuted : WalletUiTheme.TextSecondary,
                    fontSize = 13,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    whiteSpace = WhiteSpace.Normal,
                    marginTop = 4
                }
            };
            ApplyDefaultFont(addressLabel);
            info.Add(addressLabel);

            var quickActions = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginTop = 6
                }
            };
            var copyBtn = MakePillButton("Copy", () => CopyAddress(account.phaAddress));
            quickActions.Add(copyBtn);
            var explorerBtn = MakePillButton("Explorer", () => OpenExplorer(account.phaAddress));
            explorerBtn.style.marginLeft = 6;
            quickActions.Add(explorerBtn);
            info.Add(quickActions);

            row.Add(info);
            return row;
        }

        private async Task RenameAccountAsync(Account account, int index)
        {
            var am = AccountManager.Instance;
            if (am?.Accounts == null)
            {
                UpdateManageStatus("Account list is not ready.");
                return;
            }

            var prompt = await ShowModalAsync(
                    "Rename wallet",
                    $"Current local name: {account.name}\nAddress: {account.phaAddress}\n\nEnter new local account name:",
                    AccountManager.MinAccountNameLength,
                    AccountManager.MaxAccountNameLength,
                    isError: false,
                    showInput: true,
                    isPassword: false,
                    multiline: false,
                    primaryLabel: "Confirm",
                    secondaryLabel: "Cancel",
                    initialValue: account.name);

            if (prompt.result != PromptResult.Success)
            {
                return;
            }

            var newName = prompt.input?.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                await ShowErrorWithStatusAsync("Invalid account name.", "Rename cancelled.");
                return;
            }

            for (var i = 0; i < am.Accounts.Count; i++)
            {
                if (i == index)
                {
                    continue;
                }

                if (string.Equals(am.Accounts[i].name, newName, StringComparison.OrdinalIgnoreCase))
                {
                    await ShowErrorWithStatusAsync("Account with this name already exists.", "Rename cancelled.");
                    return;
                }
            }

            account.name = newName;
            am.Accounts[index] = account;
            UpdateManageStatus($"Wallet renamed to '{newName}'.");
            RefreshManagePanel();
            Refresh();
            UpdateManageActionsState();
            manageDirty = true;
        }

        private async Task ExportSelectedWalletsAsync()
        {
            var am = AccountManager.Instance;
            if (am?.Accounts == null || am.Accounts.Count == 0)
            {
                SetStatus("No wallets to export.");
                UpdateManageStatus("No wallets to export.");
                return;
            }

            var targets = manageSelection.Count > 0
                ? am.Accounts.Where(x => manageSelection.Contains(x.phaAddress)).ToList()
                : am.Accounts.ToList();

            if (targets.Count == 0)
            {
                SetStatus("Select at least one wallet to export.");
                UpdateManageStatus("Select at least one wallet to export.");
                return;
            }

            var exportCaption = (manageSelection.Count == 0
                    ? $"All {targets.Count} wallets will be exported.\n\n"
                    : $"Selected {targets.Count} wallet(s) will be exported.\n\n") +
                "Set a password to protect exported data (leave empty for no password).";

            var protectPrompt = await ShowModalAsync(
                    "Wallets export",
                    exportCaption,
                    0,
                    AccountManager.MaxPasswordLength,
                    isError: false,
                    showInput: true,
                    isPassword: true,
                    multiline: false,
                    primaryLabel: "Export",
                    secondaryLabel: "Cancel",
                    initialValue: string.Empty);

            if (protectPrompt.result != PromptResult.Success)
            {
                UpdateManageStatus("Export cancelled.");
                return;
            }

            var accountsExport = new AccountsExport
            {
                walletIdentifier = am.WalletIdentifier,
                accountsVersion = PlayerPrefs.GetInt(AccountManager.WalletVersionTag, 1)
            };

            if (!string.IsNullOrEmpty(protectPrompt.input))
            {
                accountsExport.passwordProtected = true;
                accountsExport.passwordIterations = AccountManager.PasswordIterations;
                var bytes = Serialization.Serialize(targets.ToArray());
                AccountManager.GetPasswordHash(protectPrompt.input, accountsExport.passwordIterations, out accountsExport.salt, out var passwordHash);
                accountsExport.accounts = AccountManager.EncryptString(Convert.ToBase64String(bytes), passwordHash, out accountsExport.iv);

                // Validate immediately to fail fast on incorrect params.
                AccountManager.DecryptString(accountsExport.accounts, passwordHash, accountsExport.iv);
            }
            else
            {
                accountsExport.passwordProtected = false;
                var bytes = Serialization.Serialize(targets.ToArray());
                accountsExport.accounts = Convert.ToBase64String(bytes);
            }

            var serializedExportData = Convert.ToBase64String(Serialization.Serialize(accountsExport));

            var copyPrompt = await WalletUiModalHelper.ShowConfirmAsync(
                    modalHost,
                    "Wallets export",
                    "Copy wallets export data to the clipboard?",
                    "Copy",
                    "Cancel",
                    DetachListForModal,
                    RestoreListAfterModal);

            if (copyPrompt == PromptResult.Success)
            {
                GUIUtility.systemCopyBuffer = serializedExportData;
                SetStatus("Wallet export copied to clipboard.");
                UpdateManageStatus("Export data copied to clipboard.");
            }
            else
            {
                UpdateManageStatus("Export cancelled.");
            }
        }

        private async Task OnManageImportClickedAsync()
        {
            var am = AccountManager.Instance;
            if (am == null)
            {
                UpdateManageStatus("Account manager is not ready.");
                return;
            }

            var choice = await WalletUiModalHelper.ShowChoiceDialogAsync(
                modalHost,
                "Wallet import",
                "Choose how you want to add wallets to this device.",
                new (string title, string description)[]
                {
                    ("Seed / Private key", "Enter a seed phrase or private key manually."),
                    ("Wallets export data", "Paste data exported from Wallet Management.")
                },
                cancelLabel: "Cancel",
                onBeforeShow: DetachListForModal,
                onAfterHide: RestoreListAfterModal);

            if (choice < 0)
            {
                UpdateManageStatus("Import cancelled.");
                return;
            }

            if (choice == 0)
            {
                var imported = await ImportSingleWalletAsync(false, false);
                if (imported)
                {
                    RefreshManagePanel();
                    Refresh();
                    manageDirty = true;
                }
                return;
            }

            await ImportWalletsAsync(true);
        }

        private async Task ImportWalletsAsync(bool fromManage = false)
        {
            var am = AccountManager.Instance;
            if (am == null)
            {
                SetStatus("Account manager is not ready.");
                UpdateManageStatus("Account manager is not ready.");
                return;
            }

            var dataPrompt = await ShowModalAsync(
                    "Wallets import",
                    "Paste wallets export data:",
                    1,
                    -1,
                    isError: false,
                    showInput: true,
                    isPassword: false,
                    multiline: true,
                    primaryLabel: "Import",
                    secondaryLabel: "Cancel",
                    initialValue: string.Empty);

            if (dataPrompt.result != PromptResult.Success || string.IsNullOrWhiteSpace(dataPrompt.input))
            {
                UpdateManageStatus(fromManage ? "Import cancelled." : string.Empty);
                return;
            }

            AccountsExport accountsExport;
            try
            {
                accountsExport = Serialization.Unserialize<AccountsExport>(Convert.FromBase64String(dataPrompt.input));
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{LogPrefix}Cannot open wallets data: {e}");
                await ShowErrorWithStatusAsync("Cannot open wallets data.");
                return;
            }

            if (accountsExport.passwordProtected)
            {
                var passPrompt = await ShowModalAsync(
                        "Wallets import",
                        "Enter password used during export:",
                        AccountManager.MinPasswordLength,
                        AccountManager.MaxPasswordLength,
                        isError: false,
                        showInput: true,
                        isPassword: true,
                        multiline: false,
                        primaryLabel: "Confirm",
                        secondaryLabel: "Cancel",
                        initialValue: string.Empty);

                if (passPrompt.result != PromptResult.Success || string.IsNullOrEmpty(passPrompt.input))
                {
                    UpdateManageStatus("Import cancelled.");
                    return;
                }

                try
                {
                    AccountManager.GetPasswordHashBySalt(passPrompt.input, accountsExport.passwordIterations, accountsExport.salt, out var passwordHash);
                    accountsExport.accounts = AccountManager.DecryptString(accountsExport.accounts, passwordHash, accountsExport.iv);
                }
                catch (Exception e)
                {
                    Log.WriteWarning($"{LogPrefix}Cannot decrypt wallets data: {e}");
                    await ShowErrorWithStatusAsync("Cannot decrypt wallets data.");
                    return;
                }
            }

            List<Account> accounts;
            try
            {
                accounts = Serialization.Unserialize<Account[]>(Convert.FromBase64String(accountsExport.accounts)).ToList();
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{LogPrefix}Cannot parse wallets data: {e}");
                await ShowErrorWithStatusAsync("Cannot parse wallets data.");
                return;
            }

            if (accountsExport.accountsVersion == 2)
            {
                for (var i = 0; i < accounts.Count; i++)
                {
                    var acc = accounts[i];
                    acc.misc = "legacy-seed";
                    accounts[i] = acc;
                }
            }

            var willImport = new StringBuilder();
            var accountsToImport = new List<Account>();
            var skippedAccounts = new List<Account>();

            foreach (var account in accounts)
            {
                if (am.Accounts.Any(x => string.Equals(x.phaAddress, account.phaAddress, StringComparison.OrdinalIgnoreCase)))
                {
                    skippedAccounts.Add(account);
                }
                else
                {
                    willImport.AppendLine($"+ {account.name} [{account.phaAddress}]");
                    accountsToImport.Add(account);
                }
            }

            if (skippedAccounts.Count > 0)
            {
                var skipItems = new List<(string title, string subtitle)>(skippedAccounts.Count);
                for (var i = 0; i < skippedAccounts.Count; i++)
                {
                    var skipped = skippedAccounts[i];
                    skipItems.Add((skipped.name, skipped.phaAddress));
                }

                await WalletUiModalHelper.ShowListDialogAsync(
                    modalHost,
                    "Already exist",
                    "These wallets already exist on this device and will be skipped.",
                    skipItems,
                    closeLabel: "Close",
                    onBeforeShow: DetachListForModal,
                    onAfterHide: RestoreListAfterModal);
            }

            if (accountsToImport.Count == 0)
            {
                UpdateManageStatus("Nothing to import.");
                return;
            }

            var summary = new StringBuilder();
            summary.AppendLine("Accounts to import:");
            summary.AppendLine(willImport.ToString());

            var confirm = await WalletUiModalHelper.ShowConfirmAsync(
                    modalHost,
                    "Wallets import",
                    summary.Length > 0 ? summary.ToString() : "Nothing to import.",
                    "Import",
                    "Cancel",
                    DetachListForModal,
                    RestoreListAfterModal);

            if (confirm != PromptResult.Success || accountsToImport.Count == 0)
            {
                UpdateManageStatus("Import cancelled.");
                return;
            }

            foreach (var account in accountsToImport)
            {
                am.Accounts.Add(account);
            }

            manageSelection.Clear();
            UpdateManageStatus($"{accountsToImport.Count} wallet(s) imported.");
            SetStatus($"{accountsToImport.Count} wallet(s) imported.");
            RefreshManagePanel();
            Refresh();
            UpdateManageActionsState();
            manageDirty = true;
        }

        private async Task RenameSelectedAsync()
        {
            var am = AccountManager.Instance;
            if (am?.Accounts == null)
            {
                UpdateManageStatus("Account manager is not ready.");
                return;
            }

            if (manageSelection.Count != 1)
            {
                UpdateManageStatus("Select exactly one wallet to rename.");
                return;
            }

            var address = manageSelection.First();
            var index = am.Accounts.FindIndex(x => string.Equals(x.phaAddress, address, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                UpdateManageStatus("Selected wallet is not available.");
                return;
            }

            await RenameAccountAsync(am.Accounts[index], index);
        }

        private async Task MoveSelectedAsync(int delta)
        {
            var am = AccountManager.Instance;
            if (am?.Accounts == null)
            {
                UpdateManageStatus("Account list is not ready.");
                return;
            }

            if (manageSelection.Count == 0)
            {
                UpdateManageStatus("Select wallets to move.");
                return;
            }

            var targets = am.Accounts
                .Select((acct, idx) => (acct, idx))
                .Where(t => manageSelection.Contains(t.acct.phaAddress))
                .ToList();

            if (targets.Count == 0)
            {
                UpdateManageStatus("Selected wallets are not available.");
                return;
            }

            var ordered = delta < 0
                ? targets.OrderBy(t => t.idx).ToList()
                : targets.OrderByDescending(t => t.idx).ToList();

            var moved = 0;
            foreach (var target in ordered)
            {
                var currentIndex = am.Accounts.FindIndex(x => string.Equals(x.phaAddress, target.acct.phaAddress, StringComparison.OrdinalIgnoreCase));
                if (currentIndex < 0)
                {
                    continue;
                }

                var newIndex = Mathf.Clamp(currentIndex + delta, 0, am.Accounts.Count - 1);
                if (newIndex == currentIndex)
                {
                    continue;
                }

                am.Accounts.RemoveAt(currentIndex);
                am.Accounts.Insert(newIndex, target.acct);
                moved++;
            }

            if (moved == 0)
            {
                UpdateManageStatus("Nothing to move.");
                return;
            }

            UpdateManageStatus($"Moved {moved} wallet(s) {(delta < 0 ? "up" : "down")}.");
            RefreshManagePanel();
            Refresh();
            UpdateManageActionsState();
            manageDirty = true;
            await Task.CompletedTask;
        }

        private void ApplyHiddenSelection(bool hide)
        {
            var am = AccountManager.Instance;
            if (am?.Accounts == null)
            {
                UpdateManageStatus("Account list is not ready.");
                return;
            }

            if (manageSelection.Count == 0)
            {
                UpdateManageStatus("Select wallets to hide or unhide.");
                return;
            }

            manageHiddenWorking ??= CloneHiddenAddresses(am.HiddenPhantasmaAddresses) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var selectedAddresses = am.Accounts
                .Where(x => manageSelection.Contains(x.phaAddress) && !string.IsNullOrWhiteSpace(x.phaAddress))
                .Select(x => x.phaAddress)
                .ToList();
            if (selectedAddresses.Count == 0)
            {
                UpdateManageStatus("Selected wallet is not available.");
                return;
            }

            var changed = 0;
            foreach (var address in selectedAddresses)
            {
                var isHidden = manageHiddenWorking.Contains(address);
                if (hide && !isHidden)
                {
                    manageHiddenWorking.Add(address);
                    changed++;
                }
                else if (!hide && isHidden)
                {
                    manageHiddenWorking.Remove(address);
                    changed++;
                }
            }

            if (changed == 0)
            {
                UpdateManageStatus(hide ? "Selected wallets are already hidden." : "Selected wallets are already visible.");
                return;
            }

            manageDirty = true;
            RefreshManagePanel();
            UpdateManageActionsState();
            UpdateManageStatus($"{(hide ? "Hidden" : "Unhidden")} {changed} wallet(s).");
        }

        private async Task DeleteSelectedWalletsAsync()
        {
            var am = AccountManager.Instance;
            if (am?.Accounts == null || am.Accounts.Count == 0)
            {
                UpdateManageStatus("No wallets to delete.");
                return;
            }

            if (manageSelection.Count == 0)
            {
                UpdateManageStatus("Select at least one wallet to delete.");
                return;
            }

            var confirm = await WalletUiModalHelper.ShowConfirmAsync(
                    modalHost,
                    "Delete wallets",
                    $"{manageSelection.Count} selected wallet(s) will be deleted.\nMake sure you have backups of your private keys!",
                    "Delete",
                    "Cancel",
                    DetachListForModal,
                    RestoreListAfterModal);

            if (confirm != PromptResult.Success)
            {
                UpdateManageStatus("Deletion cancelled.");
                return;
            }

            var removed = 0;
            foreach (var address in manageSelection.ToList())
            {
                var idx = am.Accounts.FindIndex(x => string.Equals(x.phaAddress, address, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    manageHiddenWorking?.Remove(address);
                    am.Accounts.RemoveAt(idx);
                    removed++;
                }
            }

            manageSelection.Clear();
            UpdateManageStatus($"{removed} wallet(s) removed from this device.");
            SetStatus($"{removed} wallet(s) removed.");
            RefreshManagePanel();
            Refresh();
            UpdateManageActionsState();
            manageDirty = true;
        }

        private void SaveAccounts()
        {
            var am = AccountManager.Instance;
            if (am == null)
            {
                UpdateManageStatus("Account manager is not ready.");
                return;
            }

            am.ApplyHiddenWallets(manageHiddenWorking ?? CloneHiddenAddresses(am.HiddenPhantasmaAddresses) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase), false);
            am.SaveAccounts();
            manageOriginalAccounts = CloneAccounts(am.Accounts);
            manageOriginalHidden = CloneHiddenAddresses(am.HiddenPhantasmaAddresses);
            manageHiddenWorking = CloneHiddenAddresses(am.HiddenPhantasmaAddresses);
            manageDirty = manageOriginalAccounts == null || manageOriginalHidden == null || manageHiddenWorking == null;
            UpdateManageStatus("Changes saved.");
            SetStatus("Changes saved.");
            HideManagePanel();
        }

        private void UpdateManageActionsState()
        {
            var am = AccountManager.Instance;
            var indices = new List<int>();
            var total = am?.Accounts?.Count ?? 0;

            if (am?.Accounts != null)
            {
                for (var i = 0; i < am.Accounts.Count; i++)
                {
                    if (manageSelection.Contains(am.Accounts[i].phaAddress))
                    {
                        indices.Add(i);
                    }
                }
            }

            var hasSelection = indices.Count > 0;
            WalletUiCommon.SetButtonEnabledVisual(manageRenameButton, manageSelection.Count == 1, Color.white);
            WalletUiCommon.SetButtonEnabledVisual(manageMoveUpButton, hasSelection && indices.Min() > 0, Color.white);
            WalletUiCommon.SetButtonEnabledVisual(manageMoveDownButton, hasSelection && indices.Max() < total - 1, Color.white);
            var hiddenSet = manageHiddenWorking ?? (am?.HiddenPhantasmaAddresses != null
                ? new HashSet<string>(am.HiddenPhantasmaAddresses, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            var hiddenSelectionCount = hasSelection ? manageSelection.Count(addr => hiddenSet.Contains(addr)) : 0;
            var visibleSelectionCount = hasSelection ? manageSelection.Count - hiddenSelectionCount : 0;
            WalletUiCommon.SetButtonEnabledVisual(manageHideButton, hasSelection && visibleSelectionCount > 0, Color.white);
            WalletUiCommon.SetButtonEnabledVisual(manageUnhideButton, hasSelection && hiddenSelectionCount > 0, Color.white);
            WalletUiCommon.SetButtonEnabledVisual(manageDeleteButton, hasSelection, Color.white);
        }

        private void UpdateManageStatus(string message)
        {
            if (manageStatusLabel == null)
            {
                return;
            }

            WalletUiCommon.UpdateStatusLabel(manageStatusLabel, message);
        }

        private Label CreateHiddenBadge()
        {
            // Small badge to make hidden wallets stand out without changing the row layout.
            var badge = new Label("HIDDEN")
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 11,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    backgroundColor = WalletUiTheme.ScreenGlassStrong,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.HighlightEdge,
                    borderRightColor = WalletUiTheme.HighlightEdge,
                    borderTopColor = WalletUiTheme.HighlightEdge,
                    borderBottomColor = WalletUiTheme.HighlightEdge,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 4,
                    paddingBottom = 4,
                    minHeight = 20
                }
            };
            ApplyDefaultFont(badge);
            return badge;
        }

        private List<Account> CloneAccounts(IReadOnlyCollection<Account> source)
        {
            if (source == null)
            {
                return new List<Account>();
            }

            try
            {
                var bytes = Serialization.Serialize(source.ToArray());
                return Serialization.Unserialize<Account[]>(bytes).ToList();
            }
            catch
            {
                Log.WriteWarning($"{LogPrefix}Failed to clone accounts snapshot; revert will be unavailable for this session.");
                return null;
            }
        }

        private HashSet<string> CloneHiddenAddresses(IEnumerable<string> source)
        {
            try
            {
                return source == null
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(source.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{LogPrefix}Failed to clone hidden wallets snapshot: {e}");
                return null;
            }
        }

    }
}
