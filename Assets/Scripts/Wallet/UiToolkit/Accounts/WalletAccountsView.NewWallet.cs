using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poltergeist.Wallet;
using Poltergeist;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist.UiToolkit;

namespace Poltergeist.UiToolkit.Accounts
{
    public sealed partial class WalletAccountsView
    {
        private string newWalletSeedPhrase;
        private readonly string[] commonPasswords = new string[]
        {
            "password", "123456", "1234567", "12345678", "baseball", "football","letmein","monkey","696969",
            "abc123","mustang","michael","shadow","master","jennifer","111111","jordan","superman","fuckme","hunter",
            "fuckyou", "trustno1", "ranger","buster","thomas","robert","bitcoin","phantasma","wallet","crypto"
        };

        private void OnNewWallet()
        {
            StartNewWalletFlow();
        }

        private void StartNewWalletFlow()
        {
            var am = AccountManager.Instance;
            if (am == null)
            {
                SetStatus("Account manager is not available yet.");
                Log.WriteWarning($"{LogPrefix}Cannot start new wallet flow, account manager is null.");
                return;
            }

            var settings = am.Settings;
            if (settings == null)
            {
                SetStatus("Wallet settings are not available yet.");
                Log.WriteWarning($"{LogPrefix}Cannot start new wallet flow, wallet settings are missing.");
                return;
            }

            // Mirrors the legacy flow: warn user, generate phrase, force backup, then derive requested wallets.
            const string attentionMessage = "For your own safety, write down generated seed words on a piece of paper and store it safely and hidden.\n\nThese words serve as a back-up of your wallet.\n\nWithout a backup, it is impossible to recover your private key,\nand any funds in the account will be lost if something happens to this device.";
            ShowModal("Attention!", attentionMessage, 0, 0, (result, _) =>
            {
                if (result == PromptResult.Success)
                {
                    GenerateNewWalletSeed(settings.mnemonicPhraseLength);
                }
                else
                {
                    SetStatus("New wallet creation was canceled.");
                }
            }, isError: false, showInput: false, isPassword: false, primaryLabel: "Confirm", secondaryLabel: "Cancel");
        }

        private void GenerateNewWalletSeed(MnemonicPhraseLength mnemonicLength)
        {
            try
            {
                ResetNewWalletState();
                newWalletSeedPhrase = Mnemonics.GenerateMnemonic(mnemonicLength);
                Log.Write($"{LogPrefix}Generated new wallet seed phrase ({mnemonicLength} words).");
                ShowBackupModal();
            }
            catch (Exception e)
            {
                ResetNewWalletState();
                Log.WriteWarning($"{LogPrefix}Failed to generate seed phrase: {e}");
                ShowError("Error creating account.\n" + e.Message, () => SetStatus("Could not generate new wallet."));
            }
        }

        private void ShowBackupModal()
        {
            if (string.IsNullOrWhiteSpace(newWalletSeedPhrase))
            {
                SetStatus("Seed phrase is not available.");
                return;
            }

            var overlay = BeginModalSession(null);
            if (overlay == null)
            {
                SetStatus("Cannot display backup dialog.");
                return;
            }

            var panel = WalletUiCommon.CreateModalPanel(840, 1020);
            panel.style.maxWidth = new Length(98, LengthUnit.Percent);

            var title = new Label("Backup your seed phrase!")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 20,
                    color = WalletUiTheme.TextPrimary,
                    marginBottom = 8
                }
            };
            ApplyDefaultFont(title);
            panel.Add(title);

            var hint = new Label("Write down these seed words in order and keep them in a safe place. Never share them with anyone.")
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 15,
                    whiteSpace = WhiteSpace.Normal,
                    marginBottom = 12
                }
            };
            ApplyDefaultFont(hint);
            panel.Add(hint);

            var wordsContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    justifyContent = Justify.FlexStart,
                    alignItems = Align.Stretch,
                    marginBottom = 10
                }
            };
            ApplyDefaultFont(wordsContainer);

            var words = newWalletSeedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < words.Length; i++)
            {
                var wordElement = BuildSeedWordElement(i, words[i]);
                wordElement.style.marginRight = 8;
                wordElement.style.marginBottom = 8;
                wordsContainer.Add(wordElement);
            }
            panel.Add(wordsContainer);

            var backupStatusLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 13,
                    marginBottom = 4
                }
            };
            ApplyDefaultFont(backupStatusLabel);
            panel.Add(backupStatusLabel);

            var actions = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.FlexEnd,
                    marginTop = 4
                }
            };
            ApplyDefaultFont(actions);

            void SetBackupStatus(string text)
            {
                backupStatusLabel.text = text ?? string.Empty;
            }

            var copyBtn = WalletUiCommon.CreateSecondaryButton("Copy to clipboard", () =>
            {
                GUIUtility.systemCopyBuffer = newWalletSeedPhrase;
                SetStatus("Seed phrase copied to the clipboard.");
                SetBackupStatus("Seed phrase copied to the clipboard.");
                Log.Write($"{LogPrefix}Seed phrase copied to clipboard (new wallet).");
            }, 16, 36);
            copyBtn.style.minWidth = 170;
            actions.Add(copyBtn);

            var continueBtn = WalletUiCommon.CreateOutlineButton("Continue", () =>
            {
                var seedWords = newWalletSeedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                HideModal();
                TrySeedVerification(seedWords, success =>
                {
                    if (success)
                    {
                        PromptWalletDerivation(newWalletSeedPhrase);
                    }
                    else
                    {
                        ShowBackupModal();
                    }
                });
            }, 16, 36);
            continueBtn.style.marginLeft = 10;
            continueBtn.style.minWidth = 120;
            actions.Add(continueBtn);

            var cancelBtn = WalletUiCommon.CreateSecondaryButton("Cancel", () =>
            {
                ResetNewWalletState();
                HideModal();
                SetStatus("New wallet creation canceled.");
            }, 16, 36);
            cancelBtn.style.marginLeft = 10;
            cancelBtn.style.minWidth = 120;
            actions.Add(cancelBtn);

            panel.Add(actions);
            overlay.Add(panel);
            Log.Write($"{LogPrefix}Backup modal shown for new wallet.");
        }

        private VisualElement BuildSeedWordElement(int index, string word)
        {
            var container = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexStart,
                    paddingLeft = 12,
                    paddingRight = 12,
                    paddingTop = 10,
                    paddingBottom = 10,
                    backgroundColor = WalletUiTheme.CardBackground,
                    backgroundImage = new StyleBackground(WalletUiTheme.GetCardGradientTexture()),
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.CardBorder,
                    borderRightColor = WalletUiTheme.CardBorder,
                    borderTopColor = WalletUiTheme.HighlightEdge,
                    borderBottomColor = WalletUiTheme.CardBorder,
                    minWidth = 180
                }
            };
            ApplyDefaultFont(container);

            var indexLabel = new Label($"{index + 1:00}")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 13,
                    marginRight = 10
                }
            };
            ApplyDefaultFont(indexLabel);

            var wordLabel = new Label(word ?? string.Empty)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 16
                }
            };
            ApplyDefaultFont(wordLabel);

            container.Add(indexLabel);
            container.Add(wordLabel);
            return container;
        }

        private void TrySeedVerification(string[] seed, Action<bool> callback)
        {
            if (seed == null || seed.Length == 0)
            {
                callback?.Invoke(false);
                return;
            }

            if (seed.Length < 3)
            {
                SetStatus("Seed phrase is incomplete.");
                callback?.Invoke(false);
                return;
            }

            var indices = Enumerable.Range(0, seed.Length)
                .OrderBy(_ => UnityEngine.Random.value)
                .Take(3)
                .OrderBy(i => i)
                .ToArray();

            var prompt = $"To confirm that you have backed up your seed phrase, enter your seed words {string.Join(", ", indices.Select(i => $"#{i + 1}"))}, using space to separate them:";
            ShowModal("Seed verification", prompt, 5, -1, (result, input) =>
            {
                if (result == PromptResult.Success)
                {
                    try
                    {
                        var wordsToVerify = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (wordsToVerify.Length >= 3 &&
                            seed[indices[0]] == wordsToVerify[0] &&
                            seed[indices[1]] == wordsToVerify[1] &&
                            seed[indices[2]] == wordsToVerify[2])
                        {
                            callback?.Invoke(true);
                        }
                        else
                        {
                            ShowError("Seed phrase is incorrect!", () => TrySeedVerification(seed, callback));
                        }
                    }
                    catch (Exception e)
                    {
                        Log.WriteWarning($"{LogPrefix}TrySeedVerification exception: {e}");
                        ShowError("Seed phrase is incorrect!\n" + e.Message, () => TrySeedVerification(seed, callback));
                    }
                }
                else
                {
                    callback?.Invoke(false);
                }
            }, isError: false, showInput: true, isPassword: false, primaryLabel: "Confirm", secondaryLabel: "Cancel");
        }

        private void PromptWalletDerivation(string mnemonicPhrase)
        {
            if (string.IsNullOrWhiteSpace(mnemonicPhrase))
            {
                SetStatus("Seed phrase is not available.");
                return;
            }

            Log.Write($"{LogPrefix}PromptWalletDerivation requested.");

            ShowModal("Number of created wallets", "Enter number of wallets to derive from this seed phrase.\n\nUse \"1\" if unsure.", 1, -1, (success, input) =>
            {
                var sanitizedInput = input?.Trim() ?? string.Empty;
                Log.Write($"{LogPrefix}PromptWalletDerivation result={success} input='{sanitizedInput}'");

                if (success == PromptResult.Success)
                {
                    if (UInt32.TryParse(sanitizedInput, out var numberOfWallets) && numberOfWallets > 0)
                    {
                        Log.Write($"{LogPrefix}Starting derivation for {numberOfWallets} wallet(s).");
                        DeriveAccountFromSeed(mnemonicPhrase, 0, numberOfWallets);
                    }
                    else
                    {
                        Log.WriteWarning($"{LogPrefix}Wallet derivation count parse failed for input '{sanitizedInput}'.");
                        ShowError("Incorrect number", () => PromptWalletDerivation(mnemonicPhrase));
                    }
                }
                else
                {
                    ShowBackupModal();
                }
            }, isError: false, showInput: true, isPassword: false, primaryLabel: "Confirm", secondaryLabel: "Cancel", initialValue: "1");
        }

        private void DeriveAccountFromSeed(string mnemonicPhrase, uint derivationIndex, uint overallDerivationCount)
        {
            try
            {
                if (overallDerivationCount == 0)
                {
                    ShowError("Incorrect number", () => PromptWalletDerivation(mnemonicPhrase));
                    return;
                }

                SetStatus($"Creating wallet {derivationIndex + 1} of {overallDerivationCount}...");
                var (wif, incorrectWord) = Mnemonics.MnemonicToWif(mnemonicPhrase, derivationIndex);

                if (wif == null)
                {
                    if (incorrectWord != null)
                    {
                        ShowError($"Seed phrase that you entered is incorrect.\nIncorrect word: '{incorrectWord}'.", ResetNewWalletState);
                    }
                    else
                    {
                        ShowError("Seed phrase that you entered is incorrect.\nPlease check your spelling carefully, and try again.\n\nEnsure that:\n* If copy / pasting - That you've selected the entire set of characters.\n* If copy / pasting - That the characters have been copied into your clipboard correctly.\n* If typing it - Take care to check that you're using English keyboard layout and the correct case for each letter.", ResetNewWalletState);
                    }
                    return;
                }

                ImportWallet(wif, (int)derivationIndex, overallDerivationCount, null, false, walletIndex =>
                {
                    if (walletIndex < 0)
                    {
                        Log.Write($"{LogPrefix}Derivation canceled at index {derivationIndex} / {overallDerivationCount}.");
                        ResetNewWalletState();
                        SetStatus("New wallet creation canceled.");
                        return;
                    }

                    if (derivationIndex == overallDerivationCount - 1)
                    {
                        if (derivationIndex == 0 && walletIndex >= 0)
                        {
                            OpenAccountAtIndex(walletIndex, true);
                        }
                        ResetNewWalletState();
                    }
                    else
                    {
                        DeriveAccountFromSeed(mnemonicPhrase, derivationIndex + 1, overallDerivationCount);
                    }
                });
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{LogPrefix}DeriveAccountFromSeed error: {e}");
                ResetNewWalletState();
                ShowError("Error creating account.\n" + e.Message, () => SetStatus("New wallet creation failed."));
            }
        }

        private void ImportWallet(string wif, int pkIndex, uint overallDerivationCount, string password, bool legacySeed, Action<int> callback)
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                SetStatus("Account manager is not available yet.");
                callback?.Invoke(-1);
                return;
            }

            if (accountManager.Accounts == null)
            {
                ShowError("Wallet storage is not ready yet.", () => callback?.Invoke(-1));
                return;
            }

            var walletNumberString = overallDerivationCount > 1 ? $" #{pkIndex + 1}" : "";
            Log.Write($"{LogPrefix}ImportWallet start{walletNumberString} legacy={legacySeed} passwordProvided={password != null}");

            if (wif != null)
            {
                PhantasmaKeys keys = null;
                try
                {
                    keys = PhantasmaKeys.FromWIF(wif);
                }
                catch (Exception e)
                {
                    Log.Write($"{LogPrefix}ImportWallet() exception: {e}");
                    ShowError("Incorrect WIF format.", () => callback?.Invoke(-1));
                    return;
                }

                foreach (var account in accountManager.Accounts)
                {
                    if (account.phaAddress == keys.Address.ToString())
                    {
                        ShowError($"Private key{walletNumberString} is already imported in a different account: {account.name}.", () => callback?.Invoke(-1));
                        return;
                    }
                }
            }

            SetStatus($"Name your wallet{walletNumberString} to continue...");
            Log.Write($"{LogPrefix}Prompting for wallet name{walletNumberString} (derivation {pkIndex + 1}/{overallDerivationCount}).");

            ShowModal("Wallet Name", $"Enter a name for your wallet{walletNumberString}", AccountManager.MinAccountNameLength, AccountManager.MaxAccountNameLength, (result, name) =>
            {
                if (result == PromptResult.Success)
                {
                    var nameAlreadyTaken = false;
                    for (int i = 0; i < accountManager.Accounts.Count(); i++)
                    {
                        if (accountManager.Accounts[i].name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            nameAlreadyTaken = true;
                        }
                    }

                    if (nameAlreadyTaken)
                    {
                        ShowError("An account with this name already exists.", () => ImportWallet(wif, pkIndex, overallDerivationCount, password, legacySeed, callback));
                    }
                    else
                    {
                        if (password == null)
                        {
                            ShowModal($"Wallet Password{walletNumberString}", $"Do you want to add a password to wallet{walletNumberString}?\nThe password will be required to open the wallet.\nIt will also be prompted every time you do a transaction", 0, 0, (wantsPass, _) =>
                            {
                                if (wantsPass == PromptResult.Success)
                                {
                                    TrySettingWalletPassword(name, wif, legacySeed, callback);
                                }
                                else
                                {
                                    FinishCreateAccount(name, wif, string.Empty, legacySeed, callback);
                                }
                            }, isError: false, showInput: false, isPassword: false, primaryLabel: "Yes", secondaryLabel: "No");
                        }
                        else
                        {
                            FinishCreateAccount(name, wif, password, legacySeed, callback);
                        }
                    }
                }
                else
                {
                    callback?.Invoke(-1);
                }
            }, isError: false, showInput: true, isPassword: false);
        }

        private void TrySettingWalletPassword(string name, string wif, bool legacySeed, Action<int> callback)
        {
            ShowModal("Wallet Password", "Enter a password for your wallet", AccountManager.MinPasswordLength, AccountManager.MaxPasswordLength, (passResult, password) =>
            {
                if (passResult == PromptResult.Success)
                {
                    if (IsGoodPassword(name, password))
                    {
                        FinishCreateAccount(name, wif, password, legacySeed, callback);
                    }
                    else
                    {
                        ShowModal(
                            "Error",
                            $"That password is either too short or too weak.\nNeeds at least {AccountManager.MinPasswordLength} characters and can't be easy to guess.",
                            0,
                            0,
                            (result, _) =>
                            {
                                if (result == PromptResult.Success)
                                {
                                    TrySettingWalletPassword(name, wif, legacySeed, callback);
                                }
                            },
                            isError: false,
                            showInput: false,
                            isPassword: false,
                            primaryLabel: "Try again",
                            secondaryLabel: "Cancel");
                    }
                }
                else
                {
                    FinishCreateAccount(name, wif, string.Empty, legacySeed, callback);
                }
            }, isError: false, showInput: true, isPassword: true);
        }

        private void FinishCreateAccount(string name, string wif, string password, bool legacySeed, Action<int> callback)
        {
            try
            {
                var accountManager = AccountManager.Instance;
                if (accountManager == null)
                {
                    ShowError("Account manager is not available yet.", () => callback?.Invoke(-1));
                    return;
                }

                int walletIndex = accountManager.AddWallet(name, wif, password, legacySeed);
                accountManager.SaveAccounts();
                Refresh();
                SetStatus($"Wallet '{name}' created.");
                Log.Write($"{LogPrefix}Wallet '{name}' created at index {walletIndex}.");

                if (callback == null)
                {
                    OpenAccountAtIndex(walletIndex, !string.IsNullOrEmpty(newWalletSeedPhrase));
                }
                else
                {
                    callback(walletIndex);
                }
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{LogPrefix}Error creating account '{name}': {e}");
                ResetNewWalletState();
                ShowError("Error creating account.\n" + e.Message, () => callback?.Invoke(-1));
            }
        }

        private void ResetNewWalletState()
        {
            newWalletSeedPhrase = null;
        }

        private bool IsGoodPassword(string name, string password)
        {
            if (password == null || password.Length < AccountManager.MinPasswordLength)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(name) && password.ToLowerInvariant().Contains(name.ToLowerInvariant()))
            {
                return false;
            }

            foreach (var common in commonPasswords)
            {
                if (password.Equals(common, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
