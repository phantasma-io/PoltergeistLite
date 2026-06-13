using System;
using System.Linq;
using System.Threading.Tasks;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Cryptography;
using UnityEngine;
using Poltergeist.UiToolkit;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Unity.Core.Logging;
using PhantasmaPhoenix.Unity.Core;

namespace Poltergeist.UiToolkit.Accounts
{
    /// <summary>
    /// Handles wallet import flows (seed/private key) from the main accounts screen.
    /// </summary>
    public sealed partial class WalletAccountsView
    {
        private async Task<bool> ImportSingleWalletAsync(bool openAfterImport, bool saveAccounts)
        {
            var importPrompt = await ShowModalAsync(
                "Wallet Import",
                "Supported inputs:\n12/24 word seed phrase\nPrivate key (HEX format)\nPrivate key (WIF format)",
                32,
                1024,
                isError: false,
                showInput: true,
                isPassword: false,
                multiline: true,
                primaryLabel: "Confirm",
                secondaryLabel: "Cancel");

            if (importPrompt.result != PromptResult.Success)
            {
                return false;
            }

            var key = importPrompt.input?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                await ShowErrorWithStatusAsync("Seed phrase or private key that you entered is incorrect.");
                return false;
            }

            if (PhantasmaAPI.IsValidPrivateKey(key) && !key.Contains(' '))
            {
                var legacy = await AskLegacySeedAsync("private key");
                var walletIndex = await ImportWalletAsync(key, -1, 1, null, legacy, saveAccounts);
                if (walletIndex >= 0)
                {
                    if (openAfterImport)
                    {
                        await OpenAccountAtIndexAsync(walletIndex, false);
                    }
                    return true;
                }
                return false;
            }

            if ((key.Length == 64 || (key.Length == 66 && key.ToUpper().StartsWith("0X"))) && !key.Contains(' '))
            {
                try
                {
                    var priv = Base16.Decode(key);
                    var tempKey = new PhantasmaKeys(priv);
                    var legacy = await AskLegacySeedAsync("WIF");
                    var walletIndex = await ImportWalletAsync(tempKey.ToWIF(), -1, 1, null, legacy, saveAccounts);
                    if (walletIndex >= 0)
                    {
                        if (openAfterImport)
                        {
                            await OpenAccountAtIndexAsync(walletIndex, false);
                        }
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Log.WriteWarning($"{LogPrefix}Failed to import HEX/WIF key: {e}");
                    await ShowErrorWithStatusAsync("Incorrect private key format.");
                }
                return false;
            }

            var wordCount = key.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount == 12 || wordCount == 24)
            {
                return await ImportSeedPhraseAsync(key, openAfterImport, saveAccounts);
            }

            await ShowErrorWithStatusAsync("Seed phrase or private key that you entered is incorrect.\nPlease check your spelling carefully, and try again.\n\nEnsure that:\n* If copy / pasting - That you've selected the entire set of characters.\n* If copy / pasting - That the characters have been copied into your clipboard correctly.\n* If typing it - Take care to check that you're using English keyboard layout and the correct case for each letter.");
            return false;
        }

        private async Task<bool> AskLegacySeedAsync(string kind)
        {
            var result = await WalletUiModalHelper.ShowConfirmAsync(
                modalHost,
                "Legacy import",
                $"Was this {kind} created using a Poltergeist version earlier than v2.4 (before end of April 2021)?",
                "No",
                "Yes",
                DetachListForModal,
                RestoreListAfterModal);

            // Primary defaults to "No" so Enter/Escape favor non-legacy; only return true when user explicitly picks Yes.
            return result != PromptResult.Success;
        }

        private async Task<bool> ImportSeedPhraseAsync(string mnemonicPhrase, bool openAfterImport, bool saveAccounts)
        {
            var derivationCount = await PromptWalletDerivationAsync(mnemonicPhrase);
            if (!derivationCount.HasValue)
            {
                return false;
            }

            return await DeriveAccountsFromSeedAsync(mnemonicPhrase, derivationCount.Value, openAfterImport, saveAccounts);
        }
    }
}
