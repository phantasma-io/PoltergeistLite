using System;
using System.Linq;
using System.Threading.Tasks;
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

        private async void OnNewWallet()
        {
            await StartNewWalletFlowAsync();
        }

        private async Task StartNewWalletFlowAsync()
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
            var attention = await ShowModalAsync("Attention!", attentionMessage, 0, 0, isError: false, showInput: false, isPassword: false, primaryLabel: "Confirm", secondaryLabel: "Cancel");
            if (attention.result != PromptResult.Success)
            {
                SetStatus("New wallet creation was canceled.");
                return;
            }

            if (!await TryGenerateNewWalletSeedAsync(settings.mnemonicPhraseLength))
            {
                return;
            }

            var seedWords = newWalletSeedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            while (true)
            {
                var backupConfirmed = await ShowBackupModalAsync();
                if (!backupConfirmed)
                {
                    ResetNewWalletState();
                    SetStatus("New wallet creation canceled.");
                    return;
                }

                var verified = await TrySeedVerificationAsync(seedWords);
                if (!verified)
                {
                    continue;
                }

                var derivationCount = await PromptWalletDerivationAsync(newWalletSeedPhrase);
                if (!derivationCount.HasValue)
                {
                    continue;
                }

                var deriveSuccess = await DeriveAccountsFromSeedAsync(newWalletSeedPhrase, derivationCount.Value);
                if (!deriveSuccess)
                {
                    SetStatus("New wallet creation failed.");
                }
                return;
            }
        }

        private async Task<bool> TryGenerateNewWalletSeedAsync(MnemonicPhraseLength mnemonicLength)
        {
            try
            {
                ResetNewWalletState();
                newWalletSeedPhrase = Mnemonics.GenerateMnemonic(mnemonicLength);
                Log.Write($"{LogPrefix}Generated new wallet seed phrase ({mnemonicLength} words).");
                return true;
            }
            catch (Exception e)
            {
                ResetNewWalletState();
                Log.WriteWarning($"{LogPrefix}Failed to generate seed phrase: {e}");
                await ShowErrorWithStatusAsync("Error creating account.\n" + e.Message, "Could not generate new wallet.");
                return false;
            }
        }

        private async Task<bool> ShowBackupModalAsync()
        {
            if (string.IsNullOrWhiteSpace(newWalletSeedPhrase))
            {
                SetStatus("Seed phrase is not available.");
                return false;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

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
                HidePanel();
                tcs.TrySetResult(true);
            }, 16, 36);
            continueBtn.style.marginLeft = 10;
            continueBtn.style.minWidth = 120;
            actions.Add(continueBtn);

            var cancelBtn = WalletUiCommon.CreateSecondaryButton("Cancel", () =>
            {
                ResetNewWalletState();
                HidePanel();
                SetStatus("New wallet creation canceled.");
                tcs.TrySetResult(false);
            }, 16, 36);
            cancelBtn.style.marginLeft = 10;
            cancelBtn.style.minWidth = 120;
            actions.Add(cancelBtn);

            panel.Add(actions);
            ShowPanel(panel);
            Log.Write($"{LogPrefix}Backup modal shown for new wallet.");

            return await tcs.Task;
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
                    minWidth = 180
                }
            };
            ApplyDefaultFont(container);
            WalletUiCommon.ApplyCardStyle(container, WalletUiTheme.GetCardGradientTexture(), WalletUiTheme.RadiusSmall);

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

        private async Task<bool> TrySeedVerificationAsync(string[] seed)
        {
            if (seed == null || seed.Length == 0)
            {
                return false;
            }

            if (seed.Length < 3)
            {
                SetStatus("Seed phrase is incomplete.");
                return false;
            }

            while (true)
            {
                var indices = Enumerable.Range(0, seed.Length)
                    .OrderBy(_ => UnityEngine.Random.value)
                    .Take(3)
                    .OrderBy(i => i)
                    .ToArray();

                var prompt = $"To confirm that you have backed up your seed phrase, enter your seed words {string.Join(", ", indices.Select(i => $"#{i + 1}"))}, using space to separate them:";
                var result = await ShowModalAsync("Seed verification", prompt, 5, -1, isError: false, showInput: true, isPassword: false, primaryLabel: "Confirm", secondaryLabel: "Back");
                if (result.result != PromptResult.Success)
                {
                    return false;
                }

                try
                {
                    var wordsToVerify = result.input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (wordsToVerify.Length >= 3 &&
                        seed[indices[0]] == wordsToVerify[0] &&
                        seed[indices[1]] == wordsToVerify[1] &&
                        seed[indices[2]] == wordsToVerify[2])
                    {
                        return true;
                    }

                    await ShowErrorWithStatusAsync("Seed phrase is incorrect!");
                }
                catch (Exception e)
                {
                    Log.WriteWarning($"{LogPrefix}TrySeedVerification exception: {e}");
                    await ShowErrorWithStatusAsync("Seed phrase is incorrect!\n" + e.Message);
                }
            }
        }

        private async Task<uint?> PromptWalletDerivationAsync(string mnemonicPhrase)
        {
            if (string.IsNullOrWhiteSpace(mnemonicPhrase))
            {
                SetStatus("Seed phrase is not available.");
                return null;
            }

            Log.Write($"{LogPrefix}PromptWalletDerivation requested.");

            while (true)
            {
                var modalResult = await ShowModalAsync(
                    "Number of created wallets",
                    "Enter number of wallets to derive from this seed phrase.\n\nUse \"1\" if unsure.",
                    1,
                    -1,
                    isError: false,
                    showInput: true,
                    isPassword: false,
                    primaryLabel: "Confirm",
                    secondaryLabel: "Back",
                    initialValue: "1");

                var sanitizedInput = modalResult.input?.Trim() ?? string.Empty;
                Log.Write($"{LogPrefix}PromptWalletDerivation result={modalResult.result} input='{sanitizedInput}'");

                if (modalResult.result != PromptResult.Success)
                {
                    return null;
                }

                if (UInt32.TryParse(sanitizedInput, out var numberOfWallets) && numberOfWallets > 0)
                {
                    Log.Write($"{LogPrefix}Starting derivation for {numberOfWallets} wallet(s).");
                    return numberOfWallets;
                }

                Log.WriteWarning($"{LogPrefix}Wallet derivation count parse failed for input '{sanitizedInput}'.");
                await ShowErrorWithStatusAsync("Incorrect number");
            }
        }

        private async Task<bool> DeriveAccountsFromSeedAsync(string mnemonicPhrase, uint overallDerivationCount, bool openAfterImport = true, bool saveAccounts = true)
        {
            try
            {
                if (overallDerivationCount == 0)
                {
                    await ShowErrorWithStatusAsync("Incorrect number");
                    return false;
                }

                for (uint derivationIndex = 0; derivationIndex < overallDerivationCount; derivationIndex++)
                {
                    SetStatus($"Creating wallet {derivationIndex + 1} of {overallDerivationCount}...");
                    var (wif, incorrectWord) = Mnemonics.MnemonicToWif(mnemonicPhrase, derivationIndex);

                    if (wif == null)
                    {
                        var errorText = incorrectWord != null
                            ? $"Seed phrase that you entered is incorrect.\nIncorrect word: '{incorrectWord}'."
                            : "Seed phrase that you entered is incorrect.\nPlease check your spelling carefully, and try again.\n\nEnsure that:\n* If copy / pasting - That you've selected the entire set of characters.\n* If copy / pasting - That the characters have been copied into your clipboard correctly.\n* If typing it - Take care to check that you're using English keyboard layout and the correct case for each letter.";
                        await ShowErrorWithStatusAsync(errorText);
                        ResetNewWalletState();
                        return false;
                    }

                    var walletIndex = await ImportWalletAsync(wif, (int)derivationIndex, overallDerivationCount, null, false, saveAccounts);
                    if (walletIndex < 0)
                    {
                        Log.Write($"{LogPrefix}Derivation canceled at index {derivationIndex} / {overallDerivationCount}.");
                        ResetNewWalletState();
                        SetStatus(openAfterImport ? "New wallet creation canceled." : "Wallet import canceled.");
                        return false;
                    }

                    if (derivationIndex == overallDerivationCount - 1)
                    {
                        if (openAfterImport && derivationIndex == 0 && walletIndex >= 0)
                        {
                            await OpenAccountAtIndexAsync(walletIndex, true);
                        }
                        ResetNewWalletState();
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{LogPrefix}DeriveAccountsFromSeedAsync error: {e}");
                ResetNewWalletState();
                await ShowErrorWithStatusAsync("Error creating account.\n" + e.Message);
            }

            return false;
        }

        private async Task<int> ImportWalletAsync(string wif, int pkIndex, uint overallDerivationCount, string password, bool legacySeed, bool saveAccounts = true)
        {
            var accountManager = AccountManager.Instance;
            if (accountManager == null)
            {
                SetStatus("Account manager is not available yet.");
                return -1;
            }

            if (accountManager.Accounts == null)
            {
                await ShowErrorWithStatusAsync("Wallet storage is not ready yet.");
                return -1;
            }

            var walletNumberString = overallDerivationCount > 1 ? $" #{pkIndex + 1}" : string.Empty;
            Log.Write($"{LogPrefix}ImportWallet start{walletNumberString} legacy={legacySeed} passwordProvided={password != null} saveAccounts={saveAccounts}");

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
                    await ShowErrorWithStatusAsync("Incorrect WIF format.");
                    return -1;
                }

                foreach (var account in accountManager.Accounts)
                {
                    if (account.phaAddress == keys.Address.ToString())
                    {
                        await ShowErrorWithStatusAsync($"Private key{walletNumberString} is already imported in a different account: {account.name}.", null, singleButton: true);
                        return -1;
                    }
                }
            }

            while (true)
            {
                SetStatus($"Name your wallet{walletNumberString} to continue...");
                Log.Write($"{LogPrefix}Prompting for wallet name{walletNumberString} (derivation {pkIndex + 1}/{overallDerivationCount}).");

                var nameResult = await ShowModalAsync(
                    "Wallet Name",
                    $"Enter a name for your wallet{walletNumberString}",
                    AccountManager.MinAccountNameLength,
                    AccountManager.MaxAccountNameLength,
                    isError: false,
                    showInput: true,
                    isPassword: false);

                if (nameResult.result != PromptResult.Success)
                {
                    return -1;
                }

                var name = nameResult.input;
                var nameAlreadyTaken = false;
                for (int i = 0; i < accountManager.Accounts.Count(); i++)
                {
                    if (accountManager.Accounts[i].name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        nameAlreadyTaken = true;
                        break;
                    }
                }

                if (nameAlreadyTaken)
                {
                    await ShowErrorWithStatusAsync("An account with this name already exists.");
                    continue;
                }

                var finalPassword = password ?? await PromptWalletPasswordAsync(walletNumberString, name);
                if (finalPassword == null)
                {
                    return -1;
                }

                return await FinishCreateAccountAsync(name, wif, finalPassword, legacySeed, saveAccounts);
            }
        }

        private async Task<string> PromptWalletPasswordAsync(string walletNumberString, string walletName)
        {
            var wantsPassword = await ShowModalAsync(
                $"Wallet Password{walletNumberString}",
                $"Do you want to add a password to wallet{walletNumberString}?\nThe password will be required to open the wallet.\nIt will also be prompted every time you do a transaction",
                0,
                0,
                isError: false,
                showInput: false,
                isPassword: false,
                primaryLabel: "Yes",
                secondaryLabel: "No");

            if (wantsPassword.result != PromptResult.Success)
            {
                return string.Empty;
            }

            while (true)
            {
                var passResult = await ShowModalAsync(
                    "Wallet Password",
                    "Enter a password for your wallet",
                    AccountManager.MinPasswordLength,
                    AccountManager.MaxPasswordLength,
                    isError: false,
                    showInput: true,
                    isPassword: true);

                if (passResult.result != PromptResult.Success)
                {
                    return string.Empty;
                }

                if (IsGoodPassword(walletName, passResult.input))
                {
                    return passResult.input;
                }

                var retry = await ShowModalAsync(
                    "Error",
                    $"That password is either too short or too weak.\nNeeds at least {AccountManager.MinPasswordLength} characters and can't be easy to guess.",
                    0,
                    0,
                    isError: false,
                    showInput: false,
                    isPassword: false,
                    primaryLabel: "Try again",
                    secondaryLabel: "Cancel");

                if (retry.result != PromptResult.Success)
                {
                    return string.Empty;
                }
            }
        }

        private async Task<int> FinishCreateAccountAsync(string name, string wif, string password, bool legacySeed, bool saveAccounts)
        {
            try
            {
                var accountManager = AccountManager.Instance;
                if (accountManager == null)
                {
                    await ShowErrorWithStatusAsync("Account manager is not available yet.");
                    return -1;
                }

                int walletIndex = accountManager.AddWallet(name, wif, password, legacySeed);
                if (saveAccounts)
                {
                    accountManager.SaveAccounts();
                }
                Refresh();
                var statusMessage = saveAccounts
                    ? $"Wallet '{name}' created."
                    : $"Wallet '{name}' imported. Apply to save changes.";
                SetStatus(statusMessage);
                Log.Write($"{LogPrefix}Wallet '{name}' created at index {walletIndex}. saveAccounts={saveAccounts}");

                return walletIndex;
            }
            catch (Exception e)
            {
                Log.WriteWarning($"{LogPrefix}Error creating account '{name}': {e}");
                ResetNewWalletState();
                await ShowErrorWithStatusAsync("Error creating account.\n" + e.Message);
                return -1;
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
