using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Unity.Core.Logging;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Holds UI-specific state for settings screen to keep GUI layer thin.
    /// </summary>
    public sealed class WalletSettingsViewState
    {
        public string Currency;
        public NexusKind NexusKind;
        public MnemonicPhraseLength MnemonicLength;
        public PasswordMode PasswordMode;
        public Log.Level LogLevel;
        public UiThemes UiTheme;
        public float ScrollY;
    }
}
