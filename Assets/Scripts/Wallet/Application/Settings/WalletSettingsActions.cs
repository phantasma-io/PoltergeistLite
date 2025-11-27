using System;
using System.IO;
using UnityEngine;
using PhantasmaPhoenix.Cryptography.Legacy;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist;
using Poltergeist.Wallet;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Encapsulates settings-related operations that should remain UI-agnostic.
    /// </summary>
    public sealed class WalletSettingsActions
    {
        private readonly Func<AccountManager> _accountProvider;

        public WalletSettingsActions(Func<AccountManager> accountProvider)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public string ClearCacheConfirmation => "Are you sure you want to clear wallet's cache?";
        public string ClearCacheSuccess => "Cache cleared.";
        public string ResetNotificationsSuccess => "Startup notifications will be shown again on next wallet start.";
        public string ResetSettingsConfirmation => "All settings will be set to default values.\nMake sure you have backups of your private keys!";
        public string ResetSettingsSuccess => "All settings set to default values.";
        public string DeleteEverythingConfirmation => "All wallets and settings stored in this device will be lost.\nMake sure you have backups of your private keys!\nOtherwise you will lose access to your funds.";
        public string DeleteEverythingSuccess => "All data removed from this device.";
        public string LegacySeedPrompt => "Enter your old seed phrase (created with Poltergeist 2.3 or older)";
        public string LegacySeedPasswordPrompt => "For wallets created with Poltergeist v1.0-v1.2: Enter seed password.\nIf you put a wrong password, wrong WIF will be generated.\n\nFor wallets created with v1.3 or later (without a seed password), you must leave this field blank.\n\nThis is NOT your wallet password used to log into the wallet.\n";
        public string ProofOfAddressesPrompt => "Enter proof of addresses messages";

        public void ClearCache()
        {
            Cache.Clear();
        }

        public ValidationResult<bool> ResetNotifications()
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return ValidationResult<bool>.Fail("Account manager is not available.");
            }

            accountManager.Settings.lastShownInformationScreen = 0;
            accountManager.Settings.SaveOnExit();
            return ValidationResult<bool>.Ok(true);
        }

        public ValidationResult<bool> ResetSettingsToDefaults()
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return ValidationResult<bool>.Fail("Account manager is not available.");
            }

            var walletsVersion = PlayerPrefs.GetInt(AccountManager.WalletVersionTag);
            var wallets = PlayerPrefs.GetString(AccountManager.WalletTag, string.Empty);

            PlayerPrefs.DeleteAll();

            PlayerPrefs.SetInt(AccountManager.WalletVersionTag, walletsVersion);
            PlayerPrefs.SetString(AccountManager.WalletTag, wallets);

            accountManager.Settings.Load();
            accountManager.UpdateRPCURL();

            return ValidationResult<bool>.Ok(true);
        }

        public ValidationResult<bool> DeleteEverything()
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                return ValidationResult<bool>.Fail("Account manager is not available.");
            }

            accountManager.DeleteAll();
            PlayerPrefs.DeleteAll();
            accountManager.Settings.Load();
            return ValidationResult<bool>.Ok(true);
        }

        public string GetDisplaySettings()
        {
            var accountManager = _accountProvider();
            return accountManager?.Settings.ToString() ?? string.Empty;
        }

        public string GetLogFolderPath()
        {
            var settings = _accountProvider()?.Settings;
            if (!string.IsNullOrWhiteSpace(settings?.logFolderPath))
            {
                return settings.logFolderPath.TrimEnd('\\', '/');
            }

            var path = Log.FilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return Path.GetDirectoryName(path)?.TrimEnd('\\', '/') ?? string.Empty;
        }

        public ValidationResult<string> ConvertLegacySeedToWif(string legacySeed, string legacySeedPassword)
        {
            if (string.IsNullOrWhiteSpace(legacySeed))
            {
                return ValidationResult<string>.Fail("Legacy seed cannot be empty.");
            }

            try
            {
                var wif = MnemonicsLegacy.DecodeLegacySeedToWif(legacySeed, legacySeedPassword);
                return ValidationResult<string>.Ok(wif);
            }
            catch (Exception e)
            {
                Log.Write($"Legacy seed decoding exception: {e}");
                return ValidationResult<string>.Fail("Legacy seed cannot be decoded");
            }
        }

        public ValidationResult<string> VerifyProofOfAddresses(string proofMessage, bool isDevMode)
        {
            if (string.IsNullOrWhiteSpace(proofMessage))
            {
                return ValidationResult<string>.Fail("Proof of addresses message is empty.");
            }

            var verifier = new ProofOfAddressesVerifier(proofMessage);

            if (isDevMode)
            {
                Log.Write("signedMessage: '" + verifier.SignedMessage + "'");
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
                return ValidationResult<string>.Fail(errorMessage);
            }

            return ValidationResult<string>.Ok("Proof of addresses message was validated successfully");
        }
    }
}
