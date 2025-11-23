using System;
using System.Globalization;
using System.Numerics;
using UnityEngine;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Coordinates settings snapshot building and mutation outside of UI.
    /// </summary>
    public sealed class WalletSettingsPresenter
    {
        private readonly WalletSettingsViewBuilder builder;
        private readonly WalletSettingsService settingsService;
        private readonly Func<AccountManager> accountProvider;
        private WalletSettingsOptions options;

        public WalletSettingsPresenter(WalletSettingsViewBuilder builder, WalletSettingsService settingsService, Func<AccountManager> accountProvider, WalletSettingsOptions options = null)
        {
            this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
            this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            this.accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
            this.options = options;
        }

        public WalletSettingsViewSnapshot BuildSnapshot()
        {
            var accountManager = accountProvider();
            if (accountManager == null)
            {
                throw new InvalidOperationException("Account manager is not available yet.");
            }

            var opts = EnsureOptions();
            opts.RefreshCurrencyOptions();
            return builder.Build(accountManager.Settings, opts);
        }

        public bool ValidateAndApply(Action<string> onError)
        {
            return settingsService.ValidateAndApply(onError);
        }

        public void SetCurrencyIndex(int index)
        {
            var settings = GetSettings();
            var opts = EnsureOptions();
            if (opts.CurrencyOptions.Length == 0)
            {
                return;
            }

            var clampedIndex = Mathf.Clamp(index, 0, opts.CurrencyOptions.Length - 1);
            settings.currency = opts.CurrencyOptions[clampedIndex];
        }

        public void SetNexusIndex(int index)
        {
            var settings = GetSettings();
            var opts = EnsureOptions();
            var clampedIndex = Mathf.Clamp(index, 0, opts.NexusOptions.Length - 1);
            var previous = settings.nexusKind;
            var nextKind = opts.NexusOptions[clampedIndex];

            if (nextKind == previous)
            {
                return;
            }

            settings.nexusKind = nextKind;

            if (nextKind != NexusKind.Custom)
            {
                settings.RestoreEndpoints(true);
            }
        }

        public void SetMnemonicIndex(int index)
        {
            var settings = GetSettings();
            var opts = EnsureOptions();
            var clampedIndex = Mathf.Clamp(index, 0, opts.MnemonicOptions.Length - 1);
            settings.mnemonicPhraseLength = opts.MnemonicOptions[clampedIndex];
        }

        public void SetPasswordModeIndex(int index)
        {
            var settings = GetSettings();
            var opts = EnsureOptions();
            var clampedIndex = Mathf.Clamp(index, 0, opts.PasswordModes.Length - 1);
            settings.passwordMode = opts.PasswordModes[clampedIndex];
        }

        public void SetLogLevelIndex(int index)
        {
            var settings = GetSettings();
            var opts = EnsureOptions();
            var clampedIndex = Mathf.Clamp(index, 0, opts.LogLevels.Length - 1);
            settings.logLevel = opts.LogLevels[clampedIndex];
        }

        public void SetUiThemeIndex(int index)
        {
            var settings = GetSettings();
            var opts = EnsureOptions();
            var clampedIndex = Mathf.Clamp(index, 0, opts.UiThemes.Length - 1);
            settings.uiThemeName = opts.UiThemes[clampedIndex].ToString();
        }

        public void SetPhantasmaRpcUrl(string value)
        {
            GetSettings().phantasmaRPCURL = value;
        }

        public void SetPhantasmaExplorerUrl(string value)
        {
            GetSettings().phantasmaExplorer = value;
        }

        public void SetPhantasmaNftExplorerUrl(string value)
        {
            GetSettings().phantasmaNftExplorer = value;
        }

        public void SetPhantasmaPoaUrl(string value)
        {
            GetSettings().phantasmaPoaUrl = value;
        }

        public void SetNexusName(string value)
        {
            GetSettings().nexusName = value;
        }

        public void SetFeePrice(string value)
        {
            var settings = GetSettings();
            if (BigInteger.TryParse(value, out var parsed))
            {
                settings.feePrice = parsed;
            }
        }

        public void SetFeeLimit(string value)
        {
            var settings = GetSettings();
            if (BigInteger.TryParse(value, out var parsed))
            {
                settings.feeLimit = parsed;
            }
        }

        public void SetBalanceDisplayThreshold(string value)
        {
            var settings = GetSettings();
            if (decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                settings.balanceDisplayThreshold = parsed < 0 ? 0 : parsed;
            }
        }

        public void SetBalanceDisplayPrecision(string value)
        {
            var settings = GetSettings();
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                settings.balanceDisplayPrecision = Mathf.Clamp(parsed, 0, 18);
            }
        }

        public void SetLogOverwriteMode(bool value)
        {
            GetSettings().logOverwriteMode = value;
        }

        public void SetUiFramerate(string value)
        {
            var settings = GetSettings();
            if (int.TryParse(value, out var parsed))
            {
                if (parsed is -1 or (>= 1 and <= 120))
                {
                    settings.uiFramerate = parsed;
                    if (settings.uiFramerate > 0)
                    {
                        QualitySettings.vSyncCount = 0;
                        Application.targetFrameRate = settings.uiFramerate;
                    }
                }
            }
        }

        public void SetInitialWindowWidth(string value)
        {
            var settings = GetSettings();
            if (int.TryParse(value, out var parsed))
            {
                settings.initialWindowWidth = parsed;
            }
        }

        public void SetInitialWindowHeight(string value)
        {
            var settings = GetSettings();
            if (int.TryParse(value, out var parsed))
            {
                settings.initialWindowHeight = parsed;
            }
        }

        public void SetDevMode(bool value)
        {
            GetSettings().devMode = value;
        }

        public void SetDevModeNoValidation(bool value)
        {
            GetSettings().devMode_NoValidation = value;
        }

        public void SetPreferScriptlessTxes(bool value)
        {
            GetSettings().preferScriptlessTxes = value;
        }

        public void SetScriptlessMaxGas(string value)
        {
            var settings = GetSettings();
            if (BigInteger.TryParse(value, out var parsed))
            {
                settings.scriptlessMaxGas = parsed;
            }
        }

        public void SetScriptlessMaxData(string value)
        {
            var settings = GetSettings();
            if (BigInteger.TryParse(value, out var parsed))
            {
                settings.scriptlessMaxData = parsed;
            }
        }

        private Settings GetSettings()
        {
            var accountManager = accountProvider();
            if (accountManager == null)
            {
                throw new InvalidOperationException("Account manager is not available yet.");
            }

            return accountManager.Settings;
        }

        private WalletSettingsOptions EnsureOptions()
        {
            var accountManager = accountProvider();
            if (accountManager == null)
            {
                throw new InvalidOperationException("Account manager is not available yet.");
            }

            options ??= new WalletSettingsOptions(accountManager);
            return options;
        }
    }
}
