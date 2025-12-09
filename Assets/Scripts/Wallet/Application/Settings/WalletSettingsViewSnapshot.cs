using System.Globalization;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// UI-ready snapshot of wallet settings and available options.
    /// </summary>
    public sealed class WalletSettingsViewSnapshot
    {
        public WalletSettingsViewSnapshot(
            string[] currencyOptions,
            int currencyIndex,
            string[] nexusDisplayOptions,
            NexusKind[] nexusOptions,
            int nexusIndex,
            string[] mnemonicDisplayOptions,
            MnemonicPhraseLength[] mnemonicOptions,
            int mnemonicIndex,
            string[] passwordDisplayOptions,
            PasswordMode[] passwordModes,
            int passwordModeIndex,
            string[] logLevelDisplayOptions,
            Log.Level[] logLevels,
            int logLevelIndex,
            string[] uiThemeDisplayOptions,
            UiThemes[] uiThemes,
            int uiThemeIndex,
            string uiScaleMultiplierText,
            Settings settings,
            bool hasCustomEndpoints,
            bool hasCustomName,
            bool shouldShowNetworkWarning)
        {
            CurrencyOptions = currencyOptions;
            CurrencyIndex = currencyIndex;

            NexusDisplayOptions = nexusDisplayOptions;
            NexusOptions = nexusOptions;
            NexusIndex = nexusIndex;
            NexusKind = settings.nexusKind;
            NexusName = settings.nexusName;

            MnemonicDisplayOptions = mnemonicDisplayOptions;
            MnemonicOptions = mnemonicOptions;
            MnemonicIndex = mnemonicIndex;

            PasswordDisplayOptions = passwordDisplayOptions;
            PasswordModes = passwordModes;
            PasswordModeIndex = passwordModeIndex;

            LogLevelDisplayOptions = logLevelDisplayOptions;
            LogLevels = logLevels;
            LogLevelIndex = logLevelIndex;
            LogOverwriteMode = settings.logOverwriteMode;
            LogFolderPath = settings.logFolderPath ?? string.Empty;

            UiThemeDisplayOptions = uiThemeDisplayOptions;
            UiThemes = uiThemes;
            UiThemeIndex = uiThemeIndex;
            UiScaleMultiplierText = uiScaleMultiplierText;

            Currency = settings.currency;
            PhantasmaRpcUrl = settings.phantasmaRPCURL;
            PhantasmaExplorerUrl = settings.phantasmaExplorer;
            PhantasmaNftExplorerUrl = settings.phantasmaNftExplorer;
            PhantasmaPoaUrl = settings.phantasmaPoaUrl;

            FeePriceText = settings.feePrice.ToString();
            FeeLimitText = settings.feeLimit.ToString();
            BalanceDisplayThresholdText = settings.balanceDisplayThreshold.ToString(CultureInfo.InvariantCulture);
            BalanceDisplayPrecisionText = settings.balanceDisplayPrecision.ToString();
            UiFramerateText = settings.uiFramerate.ToString();
            InitialWindowWidthText = settings.initialWindowWidth.ToString();
            InitialWindowHeightText = settings.initialWindowHeight.ToString();
            ScriptlessMaxGasText = settings.scriptlessMaxGas.ToString();
            ScriptlessMaxDataText = settings.scriptlessMaxData.ToString();

            DevMode = settings.devMode;
            DevModeNoValidation = settings.devMode_NoValidation;
            ShowUnstableTools = settings.showUnstableTools;
            PreferScriptlessTxes = settings.preferScriptlessTxes;

            HasCustomEndpoints = hasCustomEndpoints;
            HasCustomName = hasCustomName;
            ShouldShowNetworkWarning = shouldShowNetworkWarning;
        }

        public string[] CurrencyOptions { get; }
        public int CurrencyIndex { get; }
        public string Currency { get; }

        public string[] NexusDisplayOptions { get; }
        public NexusKind[] NexusOptions { get; }
        public int NexusIndex { get; }
        public NexusKind NexusKind { get; }
        public string NexusName { get; }

        public string[] MnemonicDisplayOptions { get; }
        public MnemonicPhraseLength[] MnemonicOptions { get; }
        public int MnemonicIndex { get; }

        public string[] PasswordDisplayOptions { get; }
        public PasswordMode[] PasswordModes { get; }
        public int PasswordModeIndex { get; }

        public string[] LogLevelDisplayOptions { get; }
        public Log.Level[] LogLevels { get; }
        public int LogLevelIndex { get; }
        public bool LogOverwriteMode { get; }
        public string LogFolderPath { get; }

        public string[] UiThemeDisplayOptions { get; }
        public UiThemes[] UiThemes { get; }
        public int UiThemeIndex { get; }
        public string UiScaleMultiplierText { get; }

        public string PhantasmaRpcUrl { get; }
        public string PhantasmaExplorerUrl { get; }
        public string PhantasmaNftExplorerUrl { get; }
        public string PhantasmaPoaUrl { get; }

        public string FeePriceText { get; }
        public string FeeLimitText { get; }
        public string BalanceDisplayThresholdText { get; }
        public string BalanceDisplayPrecisionText { get; }
        public string UiFramerateText { get; }
        public string InitialWindowWidthText { get; }
        public string InitialWindowHeightText { get; }
        public string ScriptlessMaxGasText { get; }
        public string ScriptlessMaxDataText { get; }

        public bool DevMode { get; }
        public bool DevModeNoValidation { get; }
        public bool ShowUnstableTools { get; }
        public bool PreferScriptlessTxes { get; }

        public bool HasCustomEndpoints { get; }
        public bool HasCustomName { get; }
        public bool ShouldShowNetworkWarning { get; }
    }
}
