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
    /// Mirrors legacy behavior while using UITK modals.
    /// </summary>
    public sealed partial class WalletAccountsView
    {
        private async void OnImportWallet()
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
                return;
            }

            var key = importPrompt.input?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                await ShowErrorWithStatusAsync("Seed phrase or private key that you entered is incorrect.");
                return;
            }

            if (PhantasmaAPI.IsValidPrivateKey(key) && !key.Contains(' '))
            {
                var legacy = await AskLegacySeedAsync("private key");
                var walletIndex = await ImportWalletAsync(key, -1, 1, null, legacy);
                if (walletIndex >= 0)
                {
                    await OpenAccountAtIndexAsync(walletIndex, false);
                }
                return;
            }

            if ((key.Length == 64 || (key.Length == 66 && key.ToUpper().StartsWith("0X"))) && !key.Contains(' '))
            {
                try
                {
                    var priv = Base16.Decode(key);
                    var tempKey = new PhantasmaKeys(priv);
                    var legacy = await AskLegacySeedAsync("WIF");
                    var walletIndex = await ImportWalletAsync(tempKey.ToWIF(), -1, 1, null, legacy);
                    if (walletIndex >= 0)
                    {
                        await OpenAccountAtIndexAsync(walletIndex, false);
                    }
                }
                catch (Exception e)
                {
                    Log.WriteWarning($"{LogPrefix}Failed to import HEX/WIF key: {e}");
                    await ShowErrorWithStatusAsync("Incorrect private key format.");
                }
                return;
            }

            var wordCount = key.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount == 12 || wordCount == 24)
            {
                await ImportSeedPhraseAsync(key);
                return;
            }

            await ShowErrorWithStatusAsync("Seed phrase or private key that you entered is incorrect.\nPlease check your spelling carefully, and try again.\n\nEnsure that:\n* If copy / pasting - That you've selected the entire set of characters.\n* If copy / pasting - That the characters have been copied into your clipboard correctly.\n* If typing it - Take care to check that you're using English keyboard layout and the correct case for each letter.");
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

        private async Task ImportSeedPhraseAsync(string mnemonicPhrase)
        {
            var derivationCount = await PromptWalletDerivationAsync(mnemonicPhrase);
            if (!derivationCount.HasValue)
            {
                return;
            }

            await DeriveAccountsFromSeedAsync(mnemonicPhrase, derivationCount.Value);
        }
    }
}
