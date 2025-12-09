using System;
using System.IO;
using PhantasmaPhoenix.Unity.Core.Logging;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Validates and applies wallet settings outside of UI concerns.
    /// </summary>
    public sealed class WalletSettingsService
    {
        private readonly Func<AccountManager> _accountProvider;

        public WalletSettingsService(Func<AccountManager> accountProvider)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public bool ValidateAndApply(Action<string> onError)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                onError?.Invoke("Account manager is not available yet.");
                return false;
            }

            var settings = accountManager.Settings;

            if (settings.nexusKind == NexusKind.Unknown)
            {
                onError?.Invoke("Select a Phantasma network first.");
                return false;
            }

            if (!settings.phantasmaRPCURL.IsValidURL())
            {
                onError?.Invoke("Invalid URL for Phantasma RPC URL.\n" + settings.phantasmaRPCURL);
                return false;
            }

            if (!settings.phantasmaExplorer.IsValidURL())
            {
                onError?.Invoke("Invalid URL for Phantasma Explorer URL.\n" + settings.phantasmaExplorer);
                return false;
            }

            if (!string.IsNullOrEmpty(settings.phantasmaNftExplorer) && !settings.phantasmaNftExplorer.IsValidURL())
            {
                onError?.Invoke("Invalid URL for Phantasma NFT Explorer URL.\n" + settings.phantasmaNftExplorer);
                return false;
            }

            if (settings.feePrice < 1)
            {
                onError?.Invoke("Invalid value for fee price.\n" + settings.feePrice);
                return false;
            }

            if (settings.feeLimit < 900)
            {
                onError?.Invoke("Invalid value for fee limit.\n" + settings.feeLimit);
                return false;
            }

            if (settings.initialWindowWidth < -1)
            {
                onError?.Invoke("Invalid value for initial width.\n" + settings.initialWindowWidth);
                return false;
            }

            if (settings.initialWindowHeight < -1)
            {
                onError?.Invoke("Invalid value for initial height.\n" + settings.initialWindowHeight);
                return false;
            }

            if (settings.uiScaleMultiplier < 0.1f || settings.uiScaleMultiplier > 4f)
            {
                onError?.Invoke("UI scale multiplier must be between 0.1 and 4.");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(settings.logFolderPath))
            {
                try
                {
                    Directory.CreateDirectory(settings.logFolderPath);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Cannot use log folder '{settings.logFolderPath}': {e.Message}");
                    return false;
                }
            }

            if (accountManager.Accounts.Count == 0)
            {
                accountManager.InitDemoAccounts(settings.nexusKind);
            }

            accountManager.UpdateRPCURL();
            accountManager.UpdateAPIs(true);
            accountManager.RefreshTokenPrices();
            accountManager.Settings.Save();
            try
            {
                WalletRuntime.ReconfigureLogging(settings);
            }
            catch (Exception e)
            {
                Log.WriteWarning($"[SettingsService] Failed to reconfigure logging: {e.Message}");
            }
            return true;
        }
    }
}
