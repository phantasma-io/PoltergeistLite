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

            var title = new Label("Manage wallets")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 20,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 6
                }
            };
            ApplyDefaultFont(title);
            panel.Add(title);

            var caption = new Label("Reorder, rename, export/import or delete wallets on this device.")
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

            manageStatusLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 13,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 6,
                    display = DisplayStyle.None
                }
            };
            ApplyDefaultFont(manageStatusLabel);
            panel.Add(manageStatusLabel);

            var scrollWrapper = WalletUiCommon.BuildScrollContainer(
                out manageList,
                onScrollChanged: null,
                shouldBlockWheel: () => modalHost?.Overlay != null && modalHost.Overlay.style.display == DisplayStyle.Flex,
                paddingLeft: 8f,
                paddingRight: 8f,
                paddingTop: 6f,
                paddingBottom: 80f,
                marginTop: 6f,
                marginBottom: 12f,
                maxWidth: 0f,
                alignSelf: Align.Stretch);
            manageList.style.flexGrow = 1;
            manageList.style.flexShrink = 1;
            manageList.style.minHeight = 0; // Allow the list to stretch so the footer sits at the same height as other screens.
            panel.Add(scrollWrapper);

            root.Add(panel);

            manageFooter = WalletUiCommon.BuildFooter(
                out _,
                ("Export", () => ExportSelectedWalletsAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Export failed: {ex}"))),
                ("Import", () => ImportWalletsAsync(true).Forget(ex => Log.WriteWarning($"{LogPrefix}Import failed: {ex}"))),
                ("Delete", () => DeleteSelectedWalletsAsync().Forget(ex => Log.WriteWarning($"{LogPrefix}Delete failed: {ex}"))),
                ("Save", SaveAccounts),
                ("Close", HideManagePanel)
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
                return;
            }

            UpdateManageStatus(string.Empty);
            for (var i = 0; i < am.Accounts.Count; i++)
            {
                var account = am.Accounts[i];
                manageList.Add(CreateManageRow(account, i, am.Accounts.Count));
            }
        }

        private VisualElement CreateManageRow(Account account, int index, int totalCount)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.SpaceBetween,
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

            var toggle = new Toggle
            {
                value = manageSelection.Contains(account.phaAddress),
                style =
                {
                    flexShrink = 0
                }
            };
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    manageSelection.Add(account.phaAddress);
                }
                else
                {
                    manageSelection.RemoveWhere(x => string.Equals(x, account.phaAddress, StringComparison.OrdinalIgnoreCase));
                }
            });
            row.Add(toggle);

            var nameLabel = new Label($"{index + 1}. {account.name}")
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 15,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            ApplyDefaultFont(nameLabel);
            info.Add(nameLabel);

            var addressLabel = new Label(account.phaAddress)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 13,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    whiteSpace = WhiteSpace.Normal
                }
            };
            ApplyDefaultFont(addressLabel);
            info.Add(addressLabel);

            row.Add(info);

            var buttons = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexEnd,
                    flexShrink = 0,
                    minWidth = 240
                }
            };

            var renameBtn = WalletUiCommon.CreateOutlineButton("Rename", () => RenameAccountAsync(account, index).Forget(ex => Log.WriteWarning($"{LogPrefix}Rename failed: {ex}")), 14, 30);
            renameBtn.style.minWidth = 90;
            buttons.Add(renameBtn);

            var upBtn = WalletUiCommon.CreateSecondaryButton("Up", () => MoveAccountAsync(index, -1).Forget(ex => Log.WriteWarning($"{LogPrefix}Move up failed: {ex}")), 14, 30);
            upBtn.style.minWidth = 70;
            upBtn.SetEnabled(index > 0);
            upBtn.style.marginLeft = 6;
            buttons.Add(upBtn);

            var downBtn = WalletUiCommon.CreateSecondaryButton("Down", () => MoveAccountAsync(index, 1).Forget(ex => Log.WriteWarning($"{LogPrefix}Move down failed: {ex}")), 14, 30);
            downBtn.style.minWidth = 70;
            downBtn.SetEnabled(index < totalCount - 1);
            downBtn.style.marginLeft = 6;
            buttons.Add(downBtn);

            row.Add(buttons);
            return row;
        }

        private async Task MoveAccountAsync(int index, int delta)
        {
            var am = AccountManager.Instance;
            if (am?.Accounts == null)
            {
                UpdateManageStatus("Account list is not ready.");
                return;
            }

            var newIndex = index + delta;
            if (newIndex < 0 || newIndex >= am.Accounts.Count)
            {
                return;
            }

            var accountToMove = am.Accounts[index];
            am.Accounts.RemoveAt(index);
            am.Accounts.Insert(newIndex, accountToMove);
            UpdateManageStatus($"Moved '{accountToMove.name}' to position {newIndex + 1}.");
            RefreshManagePanel();
            Refresh();
            await Task.CompletedTask;
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
            am.SaveAccounts();
            RefreshManagePanel();
            Refresh();
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

            am.SaveAccounts();
            manageSelection.Clear();
            UpdateManageStatus($"{accountsToImport.Count} wallet(s) imported.");
            SetStatus($"{accountsToImport.Count} wallet(s) imported.");
            RefreshManagePanel();
            Refresh();
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
                    am.Accounts.RemoveAt(idx);
                    removed++;
                }
            }

            manageSelection.Clear();
            am.SaveAccounts();
            UpdateManageStatus($"{removed} wallet(s) removed from this device.");
            SetStatus($"{removed} wallet(s) removed.");
            RefreshManagePanel();
            Refresh();
        }

        private void SaveAccounts()
        {
            var am = AccountManager.Instance;
            if (am == null)
            {
                UpdateManageStatus("Account manager is not ready.");
                return;
            }

            am.SaveAccounts();
            UpdateManageStatus("Changes saved.");
            SetStatus("Changes saved.");
        }

        private void UpdateManageStatus(string message)
        {
            if (manageStatusLabel == null)
            {
                return;
            }

            manageStatusLabel.text = message ?? string.Empty;
            manageStatusLabel.style.display = string.IsNullOrWhiteSpace(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

    }
}
