using System;
using System.Linq;
using UnityEngine;
using PhantasmaPhoenix.VM;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Cryptography.Legacy;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist.Wallet;
using System.Threading;
using System.Threading.Tasks;

namespace Poltergeist
{
    public partial class WalletGUI : MonoBehaviour
    {
        private ComboBox currencyComboBox = new ComboBox();

        private ComboBox nexusComboBox = new ComboBox();

        private ComboBox mnemonicPhraseLengthComboBox = new ComboBox();

        private ComboBox passwordModeComboBox = new ComboBox();

        private ComboBox logLevelComboBox = new ComboBox();

        private ComboBox uiThemeComboBox = new ComboBox();

        private void DoSettingsScreen()
        {
            var accountManager = AccountManager.Instance;
            var settings = accountManager.Settings;
            var snapshot = settingsPresenter.BuildSnapshot();
            var state = settingsPresenter.State;

            int curY = Units(7);

            var labelWidth = Units(10);
            var labelHeight = Units(2) + 4;
            var fieldX = Units(13); // X for fields.
            var fieldComboX = fieldX + 6; // X for combos.
            var fieldWidth = Units(20); // Width of text fields.
            var comboWidth = Units(8); // Width of combo fields.

            int dropHeight;

            // startX, startY: Starting position of "Settings" box.
            int startX = Border;
            int startY = (int)(curY - Border);
            // boxWidth, boxHeight: Size of "Settings" box.
            int boxWidth = (int)(windowRect.width - (Border * 2));
            int boxHeight = (int)(windowRect.height - curY);

            fieldWidth = Math.Min(fieldWidth, boxWidth - fieldX - Units(3));
            comboWidth = Math.Min(comboWidth, boxWidth - fieldX - Units(3));

            GUI.Box(new Rect(startX, startY, boxWidth, boxHeight), "");

            // Height calculation:
            // 1) 27 elements with total height of (element height + spacing) * 27 = Units(3) * 27.
            // 2) Dropdown space for log level combo: Units(2) * 3.
            // 3) Last element has additional Units(1) spacing before it.
            int elementsNumber;
            switch (settings.nexusKind)
            {
                case NexusKind.Main_Net:
                    elementsNumber = 23;
                    break;
                case NexusKind.Test_Net:
                case NexusKind.Dev_Net:
                    elementsNumber = VerticalLayout ? 27 : 26;
                    break;
                case NexusKind.Local_Net:
                    elementsNumber = VerticalLayout ? 33 : 32;
                    break;
                default:
                    elementsNumber = 32;
                    break;
            }
            var insideRect = new Rect(0, 0, boxWidth, Units(3) * elementsNumber + Units(2) * 3 + Units(1));
            // Height calculation: Units(4) space in the bottom of box is occupied by buttons row.
            var outsideRect = new Rect(startX, startY, boxWidth, boxHeight - ((VerticalLayout) ? Units(10) : Units(4)));

            bool needsScroll = insideRect.height > outsideRect.height;
            if (needsScroll)
            {
                insideRect.width -= Border;
            }

            var scroll = new Vector2(0, state.ScrollY);
            scroll = GUI.BeginScrollView(outsideRect, scroll, insideRect);
            state.ScrollY = scroll.y;

            var posX = Units(3);

            curY = Units(1); // Vertical position inside scroll view.

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Currency");
            var currencyOptions = snapshot.CurrencyOptions;
            var currentCurrencyIndex = Math.Max(0, Array.IndexOf(currencyOptions, state.Currency));
            state.Currency = currencyOptions.Length > 0 && currentCurrencyIndex >= 0 ? currencyOptions[currentCurrencyIndex] : state.Currency;
            var selectedCurrency = currencyComboBox.Show(new Rect(fieldComboX, curY, comboWidth, Units(2)), currencyOptions, 0, out dropHeight);
            if (currencyOptions.Length > 0)
            {
                settingsPresenter.SetCurrencyIndex(selectedCurrency);
                state.Currency = currencyOptions[selectedCurrency];
            }
            curY += dropHeight + Units(1);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Nexus");
            var nexusList = snapshot.NexusDisplayOptions;
            var prevNexusKind = settings.nexusKind;
            var currentNexusIndex = Math.Max(0, Array.IndexOf(snapshot.NexusOptions, state.NexusKind));
            var nexusSelection = nexusComboBox.Show(new Rect(fieldComboX, curY, comboWidth, Units(2)), nexusList, 0, out dropHeight, null, 1);
            settingsPresenter.SetNexusIndex(nexusSelection);
            state.NexusKind = snapshot.NexusOptions[Math.Max(0, nexusSelection)];
            if (settings.nexusKind != prevNexusKind)
            {
                snapshot = settingsPresenter.BuildSnapshot();
                state = settingsPresenter.State;
            }
            curY += dropHeight + Units(1);

            if (snapshot.ShouldShowNetworkWarning)
            {
                var style = GUI.skin.label;
                var tempStyle = style.fontStyle;
                style.fontStyle = FontStyle.Italic;
                var warningHeight = Units(VerticalLayout ? 6 : 4);
                GUI.Label(new Rect(posX, curY, boxWidth - (posX + Border * 2), warningHeight), "WARNING - Use this network only if you are a developer or tester.\nAll assets used here are only for development, not real.");
                style.fontStyle = tempStyle;
                curY += warningHeight + Units(1);
            }

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Seed length");
            var mnemonicPhraseLengthsList = snapshot.MnemonicDisplayOptions;
            var mnemonicIndex = Math.Max(0, Array.IndexOf(snapshot.MnemonicOptions, state.MnemonicLength));
            var selectedMnemonic = mnemonicPhraseLengthComboBox.Show(new Rect(fieldComboX, curY, comboWidth, Units(2)), mnemonicPhraseLengthsList, 0, out dropHeight, null, mnemonicIndex);
            settingsPresenter.SetMnemonicIndex(selectedMnemonic);
            state.MnemonicLength = snapshot.MnemonicOptions[Math.Max(0, selectedMnemonic)];
            curY += dropHeight + Units(1);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Password mode");
            var passwordModesList = snapshot.PasswordDisplayOptions;
            var prevPasswordModeIndex = snapshot.PasswordModeIndex;
            var passwordModeIndex = Math.Max(0, Array.IndexOf(snapshot.PasswordModes, state.PasswordMode));
            var selectedPasswordMode = passwordModeComboBox.Show(new Rect(fieldComboX, curY, comboWidth, Units(2)), passwordModesList, 0, out dropHeight, null, passwordModeIndex);
            settingsPresenter.SetPasswordModeIndex(selectedPasswordMode);
            state.PasswordMode = snapshot.PasswordModes[Math.Max(0, selectedPasswordMode)];
            curY += dropHeight + Units(1);

            if (prevPasswordModeIndex != passwordModeIndex)
            {
                // Password mode is changed.
                authService.ClearCachedMasterPassword();
            }

            bool hasCustomEndPoints = snapshot.HasCustomEndpoints;
            bool hasCustomName = snapshot.HasCustomName;

            if (hasCustomEndPoints)
            {
                GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Phantasma RPC URL");
                var phantasmaRpc = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), snapshot.PhantasmaRpcUrl);
                settingsPresenter.SetPhantasmaRpcUrl(phantasmaRpc);
                curY += Units(3);

                GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Phantasma Explorer URL");
                var explorerUrl = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), snapshot.PhantasmaExplorerUrl);
                settingsPresenter.SetPhantasmaExplorerUrl(explorerUrl);
                curY += Units(3);

                GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Phantasma NFT URL");
                var nftExplorerUrl = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), snapshot.PhantasmaNftExplorerUrl);
                settingsPresenter.SetPhantasmaNftExplorerUrl(nftExplorerUrl);
                curY += Units(3);

                GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Phantasma POA URL");
                var poaUrl = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), snapshot.PhantasmaPoaUrl);
                settingsPresenter.SetPhantasmaPoaUrl(poaUrl);
                curY += Units(3);
            }

            if (hasCustomName)
            {
                GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Nexus Name");
                var nexusName = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), snapshot.NexusName);
                settingsPresenter.SetNexusName(nexusName);
                curY += Units(3);
            }

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Phantasma fee price");
            var fee = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), snapshot.FeePriceText);
            settingsPresenter.SetFeePrice(fee);
            curY += Units(3);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Phantasma fee limit");
            var limit = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), snapshot.FeeLimitText);
            settingsPresenter.SetFeeLimit(limit);
            curY += Units(3);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Bal. threshold (0: Off)");
            var minBalanceText = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), snapshot.BalanceDisplayThresholdText);
            settingsPresenter.SetBalanceDisplayThreshold(minBalanceText);
            curY += Units(3);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Bal. decimals (0-18)");
            var balancePrecisionText = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), snapshot.BalanceDisplayPrecisionText);
            settingsPresenter.SetBalanceDisplayPrecision(balancePrecisionText);
            curY += Units(3);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Log level");
            var logLevelIndex = Math.Max(0, Array.IndexOf(snapshot.LogLevels, state.LogLevel));
            var selectedLogLevel = logLevelComboBox.Show(new Rect(fieldComboX, curY, comboWidth, Units(2)), snapshot.LogLevelDisplayOptions, WalletGUI.Units(2) * 3, out dropHeight, null, logLevelIndex);
            settingsPresenter.SetLogLevelIndex(selectedLogLevel);
            state.LogLevel = snapshot.LogLevels[Math.Max(0, selectedLogLevel)];
            curY += dropHeight + Units(1);

            var overwriteMode = GUI.Toggle(new Rect(posX, curY, Units(2), Units(2)), snapshot.LogOverwriteMode, "");
            settingsPresenter.SetLogOverwriteMode(overwriteMode);
            GUI.Label(new Rect(posX + Units(2), curY, Units(9), labelHeight), "Overwrite log");
            curY += Units(3);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "UI theme");
            var uiThemeIndex = Math.Max(0, Array.IndexOf(snapshot.UiThemes, state.UiTheme));
            var selectedUiTheme = uiThemeComboBox.Show(new Rect(fieldComboX, curY, comboWidth, Units(2)), snapshot.UiThemeDisplayOptions, WalletGUI.Units(2) * 2, out dropHeight, null, uiThemeIndex);
            settingsPresenter.SetUiThemeIndex(selectedUiTheme);
            state.UiTheme = snapshot.UiThemes[Math.Max(0, selectedUiTheme)];
            curY += dropHeight + Units(1);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "UI framerate");
            var uiFramerate = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), snapshot.UiFramerateText);
            settingsPresenter.SetUiFramerate(uiFramerate);
            curY += Units(3);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Initial width");
            var initialWindowWidth = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), snapshot.InitialWindowWidthText);
            settingsPresenter.SetInitialWindowWidth(initialWindowWidth);
            curY += Units(3);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Initial height");
            var initialWindowHeight = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), snapshot.InitialWindowHeightText);
            settingsPresenter.SetInitialWindowHeight(initialWindowHeight);
            curY += Units(3);

            var devMode = GUI.Toggle(new Rect(posX, curY, Units(2), Units(2)), settings.devMode, "");
            settingsPresenter.SetDevMode(devMode);
            GUI.Label(new Rect(posX + Units(2), curY, Units(9), labelHeight), "Developer mode");
            curY += Units(3);

            if (settings.devMode)
            {
                var noValidation = GUI.Toggle(new Rect(posX, curY, Units(2), Units(2)), settings.devMode_NoValidation, "");
                settingsPresenter.SetDevModeNoValidation(noValidation);
                GUI.Label(new Rect(posX + Units(2), curY, Units(9), labelHeight), "No validation mode");
                curY += Units(3);

                GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Scriptless: Max gas");
                var scriptlessMaxGas = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), settings.scriptlessMaxGas.ToString());
                settingsPresenter.SetScriptlessMaxGas(scriptlessMaxGas);
                curY += Units(3);

                GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Scriptless: Max data");
                var scriptlessMaxData = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), settings.scriptlessMaxData.ToString());
                settingsPresenter.SetScriptlessMaxData(scriptlessMaxData);
                curY += Units(3);
            }

            DoButton(settings.devMode, new Rect(posX, curY, Units(16), Units(2)), "Phantasma staking info", () =>
            {
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
                    modalActions.Error("Something went wrong!\n" + e.Message + "\n\n" + e.StackTrace);
                    return;
                }

                accountManager.InvokeScriptPhantasma("main", scriptMasterClaimDate, (masterClaimDateResult, masterClaimInvokeError) =>
                {
                    if (!string.IsNullOrEmpty(masterClaimInvokeError))
                    {
                        modalActions.Error("Script invocation error!\n\n" + masterClaimInvokeError);
                        return;
                    }
                    else
                    {
                        {
                            var sb = new ScriptBuilder();
                            sb.CallContract("stake", "GetClaimMasterCount", VMObject.FromBytes(masterClaimDateResult).AsTimestamp());
                            scriptClaimMasterCount = sb.EndScript();
                        }

                        accountManager.InvokeScriptPhantasma("main", scriptClaimMasterCount, (claimMasterCountResult, claimMasterCountInvokeError) =>
                        {
                            if (!string.IsNullOrEmpty(claimMasterCountInvokeError))
                            {
                                modalActions.Error("Script invocation error!\n\n" + claimMasterCountInvokeError);
                                return;
                            }
                            else
                            {
                                accountManager.InvokeScriptPhantasma("main", scriptMasterCount, (masterCountResult, masterCountInvokeError) =>
                                {
                                    if (!string.IsNullOrEmpty(masterCountInvokeError))
                                    {
                                        modalActions.Error("Script invocation error!\n\n" + masterCountInvokeError);
                                        return;
                                    }
                                    else
                                    {
                                        accountManager.InvokeScriptPhantasma("main", scriptMasterThreshold, (masterThresholdResult, masterThresholdInvokeError) =>
                                        {
                                            if (!string.IsNullOrEmpty(masterThresholdInvokeError))
                                            {
                                                modalActions.Error("Script invocation error!\n\n" + masterThresholdInvokeError);
                                                return;
                                            }
                                            else
                                            {
                                                var masterClaimDate = VMObject.FromBytes(masterClaimDateResult).AsTimestamp();
                                                var claimMasterCount = VMObject.FromBytes(claimMasterCountResult).AsNumber();
                                                var masterCount = VMObject.FromBytes(masterCountResult).AsNumber();
                                                var masterThreshold = WalletAmountFormatter.Format(VMObject.FromBytes(masterThresholdResult).AsNumber(), 8);

                                                modalActions.CopyableMessage("Account information",
                                                    $"Phantasma staking information:\n\n" +
                                                    $"All SMs: {masterCount}\n" +
                                                    $"SMs eligible for next rewards distribution: {claimMasterCount}\n" +
                                                    $"SM reward prediction: {125000 / claimMasterCount} SOUL\n" +
                                                    $"Next SM rewards distribution date: {masterClaimDate}\n" +
                                                    $"SM threshold: {masterThreshold} SOUL\n",
                                                    closeOnCopy: false);
                                            }
                                        });
                                    }
                                });
                            }
                        });
                    }

                });
            });
            curY += Units(3);

            DoButton(settings.devMode, new Rect(posX, curY, Units(16), Units(2)), "Phantasma address info", () =>
            {
                ShowModal("Address", "Enter an address", ModalState.Input, 2, -1, modalActions.ConfirmCancelOptions, 1, (result, input) =>
                {
                    if (result == PromptResult.Success)
                    {
                        accountManager.GetPhantasmaAddressInfo(input, null, (result2, error) =>
                        {
                            if (!string.IsNullOrEmpty(error))
                            {
                                modalActions.Error("Something went wrong!\n" + error);
                                return;
                            }
                            else
                            {
                                modalActions.CopyableMessage("Account information", result2, closeOnCopy: false);
                                return;
                            }
                        });
                    }
                });
            });
            curY += Units(3);

            DoButton(true, new Rect(posX, curY, Units(16), Units(2)), "Get tx description from script", () =>
            {
                ShowModal("Transaction script", "Enter transaction script in Base16 encoding", ModalState.Input, 2, -1, modalActions.ConfirmCancelOptions, 4, (result, input) =>
                {
                    if (result == PromptResult.Success)
                    {
                        var script = Base16.Decode(input.CleanHex(), false);
                        if (script == null)
                        {
                            modalActions.Error($"Cannot parse script '{input}'");
                        }
                        else
                        {
                            async Task DescribeScriptAsync()
                            {
                                try
                                {
                                    (string description, string error) = await DescriptionUtils.GetDescriptionAsync(script, true, CancellationToken.None);
                                    if (!string.IsNullOrEmpty(error))
                                    {
                                        modalActions.Error("Error during script parsing.\nDetails: " + error);
                                    }
                                    else
                                    {
                                        modalActions.CopyableMessage("Script description", description, closeOnCopy: false);
                                    }
                                }
                                catch (Exception e)
                                {
                                    modalActions.Error("Error during script parsing.\nDetails: " + e.ToString());
                                }
                            }

                            DescribeScriptAsync().Forget(ex => Log.WriteWarning(ex.ToString()));
                        }
                    }
                });
            });
            curY += Units(3);

            DoButton(true, new Rect(posX, curY, Units(16), Units(2)), "Decode tx", () =>
            {
                ShowModal("Encoded transaction", "Enter transaction in Base16 encoding", ModalState.Input, 2, -1, modalActions.ConfirmCancelOptions, 4, (result, input) =>
                {
                    if (result == PromptResult.Success)
                    {
                        PhantasmaPhoenix.Protocol.Transaction tx = null;
                        try
                        {
                            tx = PhantasmaPhoenix.Protocol.Transaction.Unserialize(Base16.Decode(input.CleanHex(), false));
                        }
                        catch (Exception e)
                        {
                            modalActions.Error($"Cannot parse transaction '{input}'.\nDetails: " + e.ToString());
                            return;
                        }

                        if (tx == null)
                        {
                            modalActions.Error($"Cannot parse transaction '{input}'");
                        }
                        else
                        {
                            async Task DescribeTransactionAsync()
                            {
                                try
                                {
                                    (string description, string error) = await DescriptionUtils.GetDescriptionAsync(tx.Script, true, CancellationToken.None);
                                    if (!string.IsNullOrEmpty(error))
                                    {
                                        modalActions.Error("Error during tx parsing.\nDetails: " + error);
                                    }
                                    else
                                    {
                                        string signatures = "";
                                        if (tx.HasSignatures)
                                        {
                                            foreach (var s in tx.Signatures)
                                            {
                                                signatures += s.ToString() + "\n";
                                            }
                                        }

                                        var message = "Nexus name: " + tx.NexusName + "\n" +
                                            "Chain name: " + tx.ChainName + "\n" +
                                            "Expiration: " + tx.Expiration + "\n" +
                                            "Payload: " + System.Text.Encoding.UTF8.GetString(tx.Payload) + "\n" +
                                            "Hash: " + tx.Hash + "\n" +
                                            "Signatures count: " + (tx.HasSignatures ? tx.Signatures.Length : "0") + "\n" +
                                            "Signatures: " + signatures + "\n" +
                                            "\n" +
                                            description;

                                        modalActions.CopyableMessage("Tx description", message, closeOnCopy: false);
                                    }
                                }
                                catch (Exception e)
                                {
                                    modalActions.Error("Error during script parsing.\nDetails: " + e.ToString());
                                }
                            }

                            DescribeTransactionAsync().Forget(ex => Log.WriteWarning(ex.ToString()));
                        }
                    }
                });
            });
            curY += Units(3);

            DoButton(true, new Rect(posX, curY, Units(16), Units(2)), "Verify proof of addresses", () =>
            {
                ShowModal("Verify proof of addresses", settingsActions.ProofOfAddressesPrompt, ModalState.Input, 2, -1, modalActions.ConfirmCancelOptions, 4, (result, input) =>
                {
                    if (result == PromptResult.Success)
                    {
                        var verifyResult = settingsActions.VerifyProofOfAddresses(input, settings.devMode);
                        if (!verifyResult.Success)
                        {
                            modalActions.Error(verifyResult.Error);
                            return;
                        }

                        modalActions.Info(verifyResult.Data);
                    }
                });
            });
            curY += Units(3);

            DoButton(true, new Rect(posX, curY, Units(16), Units(2)), "Old seed to WIF", () =>
            {
                ShowModal("Old seed to WIF", settingsActions.LegacySeedPrompt, ModalState.Input, 2, -1, modalActions.ConfirmCancelOptions, 4, (result, legacySeed) =>
                {
                    if (result != PromptResult.Success || string.IsNullOrWhiteSpace(legacySeed))
                    {
                        return;
                    }

                    ShowModal("Legacy seed password",
                        settingsActions.LegacySeedPasswordPrompt,
                        ModalState.Input, 0, 64, modalActions.ConfirmCancelOptions, 1, (pwdResult, legacySeedPassword) =>
                        {
                            if (pwdResult != PromptResult.Success)
                            {
                                return;
                            }

                            var conversionResult = settingsActions.ConvertLegacySeedToWif(legacySeed, legacySeedPassword);
                            if (!conversionResult.Success)
                            {
                                modalActions.Error(conversionResult.Error);
                                return;
                            }

                            modalActions.CopyableMessage("WIF", conversionResult.Data, (copyResult, input) =>
                            {
                                if (copyResult != PromptResult.Success)
                                {
                                    modalActions.Info("WIF copied to the clipboard.");
                                }
                            });
                        });
                });
            });
            curY += Units(3);

            curY += Units(1);
            DoButton(true, new Rect(posX, curY, Units(16), Units(2)), "Clear cache", () =>
            {
                modalActions.ConfirmCancel(settingsActions.ClearCacheConfirmation, (result) =>
                {
                    if (result == PromptResult.Success)
                    {
                        settingsActions.ClearCache();
                        modalActions.Info(settingsActions.ClearCacheSuccess);
                    }
                });
            });
            curY += Units(3);

            curY += Units(1);
            DoButton(true, new Rect(posX, curY, Units(16), Units(2)), "Reset notifications", () =>
            {
                var resetResult = settingsActions.ResetNotifications();
                if (!resetResult.Success)
                {
                    modalActions.Error(resetResult.Error);
                    return;
                }

                modalActions.Info(settingsActions.ResetNotificationsSuccess);
            });
            curY += Units(3);

            DoButton(true, new Rect(posX, curY, Units(16), Units(2)), "Reset settings", () =>
            {
                modalActions.ConfirmCancel(settingsActions.ResetSettingsConfirmation, (result) =>
                {
                    if (result == PromptResult.Success)
                    {
                        var resetResult = settingsActions.ResetSettingsToDefaults();
                        if (!resetResult.Success)
                        {
                            modalActions.Error(resetResult.Error);
                            return;
                        }

                        // Restoring combos' selected items.
                        // If they are not restored, following calls of DoSettingsScreen() will change them again.
                        settingsPresenter.ResetStateFromSettings();
                        SetState(GUIState.Settings);

                        modalActions.Info(settingsActions.ResetSettingsSuccess, () =>
                        {
                            CloseCurrentStack();
                        });
                    }
                }, 0);
            });
            curY += Units(3);

            if (accountManager.Accounts.Count() > 0)
            {
                curY += Units(1);
                DoButton(true, new Rect(posX, curY, Units(16), Units(2)), "Delete everything", () =>
                {
                    modalActions.ConfirmCancel(settingsActions.DeleteEverythingConfirmation, (result) =>
                    {
                        if (result == PromptResult.Success)
                        {
                            var deleteResult = settingsActions.DeleteEverything();
                            if (!deleteResult.Success)
                            {
                                modalActions.Error(deleteResult.Error);
                                return;
                            }

                            modalActions.Info(settingsActions.DeleteEverythingSuccess, () =>
                            {
                                CloseCurrentStack();
                            });
                        }
                    }, 10);
                });

                curY += Units(3);
            }

            GUI.EndScrollView();

            var btnWidth = Units(10);
            var btnHeight = Units(2);
            var btnVerticalSpacing = 4;
            curY = (int)(windowRect.height - Units(4));

            Rect cancelBtnRect;
            Rect confirmBtnRect;

            if (VerticalLayout)
            {
                cancelBtnRect = new Rect(startX + Border * 2, startY + boxHeight - btnHeight - Border, boxWidth - Border * 4, btnHeight);
                confirmBtnRect = new Rect(startX + Border * 2, startY + boxHeight - btnHeight * 2 - Border - btnVerticalSpacing, boxWidth - Border * 4, btnHeight);
            }
            else
            {
                cancelBtnRect = new Rect(windowRect.width / 3 - btnWidth / 2, curY, btnWidth, btnHeight);
                confirmBtnRect = new Rect((windowRect.width / 3) * 2 - btnWidth / 2, curY, btnWidth, btnHeight);
            }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            string[] settingsMenu = new string[] { "View", "Open log location", "Cancel", "Confirm" };
#else
            string[] settingsMenu = new string[] { "View", "Show log location", "Cancel", "Confirm" };
#endif
            int posY;
            DoButtonGrid<int>(false, settingsMenu.Length, (VerticalLayout) ? 0 : Units(2), 0, out posY, (index) =>
            {
                return new MenuEntry(index, settingsMenu[index], true);
            },
            (selected) =>
            {
                switch (selected)
                {
                    case 0:
                        {
                            var currentSettings = settingsActions.GetDisplaySettings();
                            modalActions.CopyableMessage("Display Settings", currentSettings, closeOnCopy: false, copyValue: currentSettings);

                            break;
                        }
                    case 1:
                        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                            var path = settingsActions.GetLogFolderPath();
                            if (string.IsNullOrWhiteSpace(path))
                            {
                                modalActions.Error("Log file path is not available.");
                                break;
                            }

                            System.Diagnostics.Process.Start(path);
#else
                            var logPath = settingsActions.GetLogFolderPath();
                            if (string.IsNullOrWhiteSpace(logPath))
                            {
                                modalActions.Error("Log file path is not available.");
                                break;
                            }

                            modalActions.CopyableMessage("Log file path", logPath, closeOnCopy: false, copyValue: logPath);
#endif
                            break;
                        }

                    case 2:
                        {
                            // Resetting changes by restoring current settings.
                            settings.Load();

                            // Restoring combos' selected items.
                            // If they are not restored, following calls of DoSettingsScreen() will change them again.
                            SetState(GUIState.Settings);

                            CloseCurrentStack();
                            break;
                        }

                    case 3:
                        {
                            if (ValidateSettings())
                            {
                                ResourceManager.Instance.UnloadTokens();
                                CloseCurrentStack();
                            }
                            break;
                        }
                }
            });
        }

        private bool ValidateSettings()
        {
            return settingsPresenter.ValidateAndApply(error => modalActions.Error(error));
        }
    }
}
