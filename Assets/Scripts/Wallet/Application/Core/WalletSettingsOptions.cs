using System;
using System.Linq;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Unity.Core.Logging;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Provides read-only sets of available wallet settings options and helpers to work with them.
    /// </summary>
    public sealed class WalletSettingsOptions
    {
        private readonly AccountManager _accountManager;

        public WalletSettingsOptions(AccountManager accountManager)
        {
            _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));

            RefreshCurrencyOptions();

            NexusOptions = Enum.GetValues(typeof(NexusKind)).Cast<NexusKind>().ToArray();
            NexusDisplayOptions = NexusOptions.Select(x => x.ToString().Replace('_', ' ')).ToArray();

            MnemonicOptions = Enum.GetValues(typeof(MnemonicPhraseLength)).Cast<MnemonicPhraseLength>().ToArray();
            MnemonicDisplayOptions = MnemonicOptions.Select(x => x.ToString().Replace('_', ' ')).ToArray();

            PasswordModes = Enum.GetValues(typeof(PasswordMode)).Cast<PasswordMode>().ToArray();
            PasswordDisplayOptions = PasswordModes.Select(x => x.ToString().Replace('_', ' ')).ToArray();

            LogLevels = Enum.GetValues(typeof(Log.Level)).Cast<Log.Level>().ToArray();
            LogLevelDisplayOptions = LogLevels.Select(x => x.ToString()).ToArray();

            UiThemes = Enum.GetValues(typeof(UiThemes)).Cast<UiThemes>().ToArray();
            UiThemeDisplayOptions = UiThemes.Select(x => x.ToString()).ToArray();
        }

        public string[] CurrencyOptions { get; private set; } = Array.Empty<string>();

        public NexusKind[] NexusOptions { get; }
        public string[] NexusDisplayOptions { get; }

        public MnemonicPhraseLength[] MnemonicOptions { get; }
        public string[] MnemonicDisplayOptions { get; }

        public PasswordMode[] PasswordModes { get; }
        public string[] PasswordDisplayOptions { get; }

        public Log.Level[] LogLevels { get; }
        public string[] LogLevelDisplayOptions { get; }

        public UiThemes[] UiThemes { get; }
        public string[] UiThemeDisplayOptions { get; }

        public void RefreshCurrencyOptions()
        {
            CurrencyOptions = _accountManager.Currencies.ToArray();
        }

        public int GetCurrencyIndex(string currency)
        {
            return IndexOrZero(CurrencyOptions, currency, (a, b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase));
        }

        public int GetNexusIndex(NexusKind value)
        {
            return IndexOrZero(NexusOptions, value, (a, b) => a == b);
        }

        public int GetMnemonicIndex(MnemonicPhraseLength value)
        {
            return IndexOrZero(MnemonicOptions, value, (a, b) => a == b);
        }

        public int GetPasswordModeIndex(PasswordMode value)
        {
            return IndexOrZero(PasswordModes, value, (a, b) => a == b);
        }

        public int GetLogLevelIndex(Log.Level value)
        {
            return IndexOrZero(LogLevels, value, (a, b) => a == b);
        }

        public int GetUiThemeIndex(string uiThemeName)
        {
            return IndexOrZero(UiThemes, uiThemeName, (theme, name) => string.Equals(theme.ToString(), name, StringComparison.OrdinalIgnoreCase));
        }

        private static int IndexOrZero<T>(T[] list, T value, Func<T, T, bool> equals)
        {
            if (list == null || list.Length == 0)
            {
                return 0;
            }

            for (int i = 0; i < list.Length; i++)
            {
                if (equals(list[i], value))
                {
                    return i;
                }
            }

            return 0;
        }

        private static int IndexOrZero<T>(T[] list, string value, Func<T, string, bool> equals)
        {
            if (list == null || list.Length == 0)
            {
                return 0;
            }

            for (int i = 0; i < list.Length; i++)
            {
                if (equals(list[i], value))
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
