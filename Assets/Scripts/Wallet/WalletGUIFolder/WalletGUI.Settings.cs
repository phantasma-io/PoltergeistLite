using System;
using System.Linq;
using System.Numerics;
using UnityEngine;
using PhantasmaPhoenix.VM;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Cryptography.Legacy;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist.Wallet;

namespace Poltergeist
{
    public partial class WalletGUI : MonoBehaviour
    {
        private int currencyIndex;
        private ComboBox currencyComboBox = new ComboBox();

        private int nexusIndex;
        private ComboBox nexusComboBox = new ComboBox();

        private int mnemonicPhraseLengthIndex;
        private ComboBox mnemonicPhraseLengthComboBox = new ComboBox();

        private int passwordModeIndex;
        private ComboBox passwordModeComboBox = new ComboBox();

        private int logLevelIndex;
        private ComboBox logLevelComboBox = new ComboBox();

        private int uiThemeIndex;
        private ComboBox uiThemeComboBox = new ComboBox();

        private WalletSettingsOptions settingsOptions;

        private void DoSettingsScreen()
        {
            var accountManager = AccountManager.Instance;
            var settings = accountManager.Settings;

            settingsOptions ??= new WalletSettingsOptions(accountManager);

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

            settingsScroll = GUI.BeginScrollView(outsideRect, settingsScroll, insideRect);

            var posX = Units(3);

            curY = Units(1); // Vertical position inside scroll view.

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Currency");
            var currencyOptions = settingsOptions.CurrencyOptions;
            currencyIndex = currencyComboBox.Show(new Rect(fieldComboX, curY, comboWidth, Units(2)), currencyOptions, 0, out dropHeight);
            if (currencyOptions.Length > 0)
            {
                settings.currency = currencyOptions[currencyIndex];
            }
            curY += dropHeight + Units(1);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Nexus");
            var nexusList = settingsOptions.NexusDisplayOptions;
            var prevNexus = nexusIndex;
            nexusIndex = nexusComboBox.Show(new Rect(fieldComboX, curY, comboWidth, Units(2)), nexusList, 0, out dropHeight, null, 1);
            settings.nexusKind = settingsOptions.NexusOptions[nexusIndex];
            curY += dropHeight + Units(1);

            if (settings.nexusKind != NexusKind.Main_Net && settings.nexusKind != NexusKind.Custom && settings.nexusKind != NexusKind.Unknown)
            {
                var style = GUI.skin.label;
                var tempStyle = style.fontStyle;
                style.fontStyle = FontStyle.Italic;
                var warningHeight = Units(VerticalLayout ? 6 : 4);
                GUI.Label(new Rect(posX, curY, boxWidth - (posX + Border * 2), warningHeight), "WARNING - Use this network only if you are a developer or tester.\nAll assets used here are only for development, not real.");
                style.fontStyle = tempStyle;
                curY += warningHeight + Units(1);
            }

            if (prevNexus != nexusIndex && settings.nexusKind != NexusKind.Custom)
            {
                settings.RestoreEndpoints(true);
            }

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Seed length");
            var mnemonicPhraseLengthsList = settingsOptions.MnemonicDisplayOptions;
            mnemonicPhraseLengthIndex = mnemonicPhraseLengthComboBox.Show(new Rect(fieldComboX, curY, comboWidth, Units(2)), mnemonicPhraseLengthsList, 0, out dropHeight, null, 0);
            settings.mnemonicPhraseLength = settingsOptions.MnemonicOptions[mnemonicPhraseLengthIndex];
            curY += dropHeight + Units(1);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Password mode");
            var passwordModesList = settingsOptions.PasswordDisplayOptions;
            var prevPasswordModeIndex = passwordModeIndex;
            passwordModeIndex = passwordModeComboBox.Show(new Rect(fieldComboX, curY, comboWidth, Units(2)), passwordModesList, 0, out dropHeight, null, 0);
            settings.passwordMode = settingsOptions.PasswordModes[passwordModeIndex];
            curY += dropHeight + Units(1);

            if (prevPasswordModeIndex != passwordModeIndex)
            {
                // Password mode is changed.
                masterPassword = null;
            }

            bool hasCustomEndPoints = false;
            bool hasCustomName = settings.nexusKind == NexusKind.Custom;

            switch (settings.nexusKind)
            {
                case NexusKind.Custom:
                case NexusKind.Local_Net:
                    {
                        hasCustomEndPoints = true;
                        break;
                    }

                case NexusKind.Test_Net:
                case NexusKind.Dev_Net:
                    {
                        break;
                    }

                default:
                    {
                        hasCustomEndPoints = false;
                        hasCustomName = false;
                        break;
                    }
            }

            if (hasCustomEndPoints)
            {
                GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Phantasma RPC URL");
                settings.phantasmaRPCURL = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), settings.phantasmaRPCURL);
                curY += Units(3);

                GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Phantasma Explorer URL");
                settings.phantasmaExplorer = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), settings.phantasmaExplorer);
                curY += Units(3);

                GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Phantasma NFT URL");
                settings.phantasmaNftExplorer = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), settings.phantasmaNftExplorer);
                curY += Units(3);

                GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Phantasma POA URL");
                settings.phantasmaPoaUrl = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), settings.phantasmaPoaUrl);
                curY += Units(3);
            }
            else
            {
                settings.RestoreEndpoints(!hasCustomName);
            }

            if (hasCustomName)
            {
                GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Nexus Name");
                settings.nexusName = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), settings.nexusName);
                curY += Units(3);
            }

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Phantasma fee price");
            var fee = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), settings.feePrice.ToString());
            BigInteger.TryParse(fee, out settings.feePrice);
            curY += Units(3);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Phantasma fee limit");
            var limit = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), settings.feeLimit.ToString());
            BigInteger.TryParse(limit, out settings.feeLimit);
            curY += Units(3);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Log level");
            logLevelIndex = logLevelComboBox.Show(new Rect(fieldComboX, curY, comboWidth, Units(2)), settingsOptions.LogLevelDisplayOptions, WalletGUI.Units(2) * 3, out dropHeight);
            settings.logLevel = settingsOptions.LogLevels[logLevelIndex];
            curY += dropHeight + Units(1);

            settings.logOverwriteMode = GUI.Toggle(new Rect(posX, curY, Units(2), Units(2)), settings.logOverwriteMode, "");
            GUI.Label(new Rect(posX + Units(2), curY, Units(9), labelHeight), "Overwrite log");
            curY += Units(3);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "UI theme");
            uiThemeIndex = uiThemeComboBox.Show(new Rect(fieldComboX, curY, comboWidth, Units(2)), settingsOptions.UiThemeDisplayOptions, WalletGUI.Units(2) * 2, out dropHeight);
            settings.uiThemeName = settingsOptions.UiThemes[uiThemeIndex].ToString();
            curY += dropHeight + Units(1);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "UI framerate");
            var uiFramerate = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), settings.uiFramerate.ToString());
            if (int.TryParse(uiFramerate, out var uiFramerateInt))
            {
                if (uiFramerateInt is -1 or (>= 1 and <= 120))
                {
                    settings.uiFramerate = uiFramerateInt;

                    if (settings.uiFramerate > 0)
                    {
                        QualitySettings.vSyncCount = 0;
                        Application.targetFrameRate = settings.uiFramerate;
                    }
                }
            }
            curY += Units(3);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Initial width");
            var initialWindowWidth = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), settings.initialWindowWidth.ToString());
            if (int.TryParse(initialWindowWidth, out var initialWindowWidthInt))
            {
                settings.initialWindowWidth = initialWindowWidthInt;
            }
            curY += Units(3);

            GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Initial height");
            var initialWindowHeight = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), settings.initialWindowHeight.ToString());
            if (int.TryParse(initialWindowHeight, out var initialWindowHeightInt))
            {
                settings.initialWindowHeight = initialWindowHeightInt;
            }
            curY += Units(3);

            settings.devMode = GUI.Toggle(new Rect(posX, curY, Units(2), Units(2)), settings.devMode, "");
            GUI.Label(new Rect(posX + Units(2), curY, Units(9), labelHeight), "Developer mode");
            curY += Units(3);

            if (settings.devMode)
            {
                settings.devMode_NoValidation = GUI.Toggle(new Rect(posX, curY, Units(2), Units(2)), settings.devMode_NoValidation, "");
                GUI.Label(new Rect(posX + Units(2), curY, Units(9), labelHeight), "No validation mode");
                curY += Units(3);

                settings.preferScriptlessTxes = GUI.Toggle(new Rect(posX, curY, Units(2), Units(2)), settings.preferScriptlessTxes, "");
                GUI.Label(new Rect(posX + Units(2), curY, Units(9), labelHeight), "Use scriptless txes");
                curY += Units(3);

                GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Scriptless: Max gas");
                var scriptlessMaxGas = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), settings.scriptlessMaxGas.ToString());
                BigInteger.TryParse(scriptlessMaxGas, out settings.scriptlessMaxGas);
                curY += Units(3);

                GUI.Label(new Rect(posX, curY, labelWidth, labelHeight), "Scriptless: Max data");
                var scriptlessMaxData = GUI.TextField(new Rect(fieldX, curY, fieldWidth, Units(2)), settings.scriptlessMaxData.ToString());
                BigInteger.TryParse(scriptlessMaxData, out settings.scriptlessMaxData);
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
                                                var masterThreshold = UnitConversion.ToDecimal(VMObject.FromBytes(masterThresholdResult).AsNumber(), 8);

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
                            try
                            {
                                WalletGUI.Instance.StartCoroutine(DescriptionUtils.GetDescription(script, true, (description, error) =>
                                {
                                    if (!string.IsNullOrEmpty(error))
                                    {
                                        modalActions.Error("Error during script parsing.\nDetails: " + error);
                                    }
                                    else
                                    {
                                        modalActions.CopyableMessage("Script description", description, closeOnCopy: false);
                                    }
                                }));
                            }
                            catch (Exception e)
                            {
                                modalActions.Error("Error during script parsing.\nDetails: " + e.ToString());
                                return;
                            }
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
                            try
                            {
                                WalletGUI.Instance.StartCoroutine(DescriptionUtils.GetDescription(tx.Script, true, (description, error) =>
                                {
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
                                }));
                            }
                            catch (Exception e)
                            {
                                modalActions.Error("Error during script parsing.\nDetails: " + e.ToString());
                                return;
                            }
                        }
                    }
                });
            });
            curY += Units(3);

            DoButton(true, new Rect(posX, curY, Units(16), Units(2)), "Verify proof of addresses", () =>
            {
                ShowModal("Verify proof of addresses", "Enter proof of addresses messages", ModalState.Input, 2, -1, modalActions.ConfirmCancelOptions, 4, (result, input) =>
                {
                    if (result == PromptResult.Success)
                    {
                        var verifier = new ProofOfAddressesVerifier(input);


                        if (settings.devMode)
                        {
                            Log.Write("signedMessage: '" + verifier.SignedMessage + "'");
                        }

                        if (settings.devMode)
                        {
                            Log.Write("phaAddress: '" + verifier.PhaAddress + "'");
                            Log.Write("ethAddress: '" + verifier.EthAddress + "'");
                            Log.Write("ethPublicKey: '" + verifier.EthPublicKey + "'");
                            Log.Write("neo2Address: '" + verifier.Neo2Address + "'");
                            Log.Write("neo2PublicKey: '" + verifier.Neo2PublicKey + "'");
                            Log.Write("phaSignature: '" + verifier.PhaSignature + "'");
                            Log.Write("ethSignature: '" + verifier.EthSignature + "'");
                            Log.Write("neo2Signature: '" + verifier.Neo2Signature + "'");
                        }

                        var (success, errorMessage) = verifier.VerifyMessage();

                        if (!success)
                        {
                            modalActions.Error(errorMessage);
                            return;
                        }

                        modalActions.Info("Proof of addresses message was validated successfully");
                    }
                });
            });
            curY += Units(3);

            DoButton(true, new Rect(posX, curY, Units(16), Units(2)), "Old seed to WIF", () =>
            {
                ShowModal("Old seed to WIF", "Enter your old seed phrase (created with Poltergeist 2.3 or older)", ModalState.Input, 2, -1, modalActions.ConfirmCancelOptions, 4, (result, legacySeed) =>
                {
                    if (result != PromptResult.Success)
                    {
                        return;
                    }

                    ShowModal("Legacy seed password",
                        "For wallets created with Poltergeist v1.0-v1.2: Enter seed password.\nIf you put a wrong password, wrong WIF will be generated.\n\nFor wallets created with v1.3 or later (without a seed password), you must leave this field blank.\n\nThis is NOT your wallet password used to log into the wallet.\n",
                        ModalState.Input, 0, 64, modalActions.ConfirmCancelOptions, 1, (pwdResult, legacySeedPassword) =>
                        {
                            if (pwdResult != PromptResult.Success)
                            {
                                return;
                            }

                            string wif;
                            try
                            {
                                wif = MnemonicsLegacy.DecodeLegacySeedToWif(legacySeed, legacySeedPassword);
                            }
                            catch (Exception e)
                            {
                                Log.Write("Legacy seed decoding exception: " + e);
                                modalActions.Error("Legacy seed cannot be decoded");
                                return;
                            }

                            modalActions.CopyableMessage("WIF", wif, (copyResult, input) =>
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
                modalActions.ConfirmCancel("Are you sure you want to clear wallet's cache?", (result) =>
                {
                    if (result == PromptResult.Success)
                    {
                        Cache.Clear();
                        modalActions.Info("Cache cleared.");
                    }
                });
            });
            curY += Units(3);

            curY += Units(1);
            DoButton(true, new Rect(posX, curY, Units(16), Units(2)), "Reset notifications", () =>
            {
                accountManager.Settings.lastShownInformationScreen = 0;
                accountManager.Settings.SaveOnExit(); // Required on Android
                modalActions.Info("Startup notifications will be shown again on next wallet start.");
            });
            curY += Units(3);

            DoButton(true, new Rect(posX, curY, Units(16), Units(2)), "Reset settings", () =>
            {
                modalActions.ConfirmCancel("All settings will be set to default values.\nMake sure you have backups of your private keys!", (result) =>
                {
                    if (result == PromptResult.Success)
                    {
                        // Saving wallets before settings reset.
                        var walletsVersion = PlayerPrefs.GetInt(AccountManager.WalletVersionTag);
                        var wallets = PlayerPrefs.GetString(AccountManager.WalletTag, "");

                        PlayerPrefs.DeleteAll();

                        // Restoring wallets before settings reset.
                        PlayerPrefs.SetInt(AccountManager.WalletVersionTag, walletsVersion);
                        PlayerPrefs.SetString(AccountManager.WalletTag, wallets);

                        // Loading default settings.
                        accountManager.Settings.Load();

                        // Finding fastest Phantasma and Neo RPCs.
                        accountManager.UpdateRPCURL();

                        // Restoring combos' selected items.
                        // If they are not restored, following calls of DoSettingsScreen() will change them again.
                        SetState(GUIState.Settings);

                        modalActions.Info("All settings set to default values.", () =>
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
                    modalActions.ConfirmCancel("All wallets and settings stored in this device will be lost.\nMake sure you have backups of your private keys!\nOtherwise you will lose access to your funds.", (result) =>
                    {
                        if (result == PromptResult.Success)
                        {
                            accountManager.DeleteAll();
                            PlayerPrefs.DeleteAll();
                            accountManager.Settings.Load();
                            modalActions.Info("All data removed from this device.", () =>
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
            string[] settingsMenu = new string[] { "Display settings", "Open log location", "Cancel", "Confirm" };
#else
            string[] settingsMenu = new string[] { "Display settings", "Show log location", "Cancel", "Confirm" };
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
                            var currentSettings = accountManager.Settings.ToString();
                            modalActions.CopyableMessage("Display Settings", currentSettings, closeOnCopy: false, copyValue: currentSettings);

                            break;
                        }
                    case 1:
                        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                            string path = System.IO.Path.GetDirectoryName(Log.FilePath).TrimEnd(new[] { '\\', '/' }); // Mac doesn't like trailing slash
                            System.Diagnostics.Process.Start(path);
#else
                            modalActions.CopyableMessage("Log file path", Log.FilePath, closeOnCopy: false, copyValue: Log.FilePath);
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
            return settingsService.ValidateAndApply(error => modalActions.Error(error));
        }
    }
}
