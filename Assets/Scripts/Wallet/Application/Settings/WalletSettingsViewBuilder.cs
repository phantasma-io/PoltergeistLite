using System;
using System.Globalization;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Builds settings view snapshots from runtime settings.
    /// </summary>
    public sealed class WalletSettingsViewBuilder
    {
        public WalletSettingsViewSnapshot Build(Settings settings, WalletSettingsOptions options)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var hasCustomEndpoints = settings.nexusKind is NexusKind.Custom or NexusKind.Local_Net;
            var hasCustomName = settings.nexusKind == NexusKind.Custom;
            var shouldShowNetworkWarning = settings.nexusKind != NexusKind.Main_Net && settings.nexusKind != NexusKind.Custom && settings.nexusKind != NexusKind.Unknown;

            return new WalletSettingsViewSnapshot(
                options.CurrencyOptions,
                options.GetCurrencyIndex(settings.currency),
                options.NexusDisplayOptions,
                options.NexusOptions,
                options.GetNexusIndex(settings.nexusKind),
                options.MnemonicDisplayOptions,
                options.MnemonicOptions,
                options.GetMnemonicIndex(settings.mnemonicPhraseLength),
                options.PasswordDisplayOptions,
                options.PasswordModes,
                options.GetPasswordModeIndex(settings.passwordMode),
                options.LogLevelDisplayOptions,
                options.LogLevels,
                options.GetLogLevelIndex(settings.logLevel),
                options.UiPreviewDeviceDisplayOptions,
                options.UiPreviewDevices,
                options.GetUiPreviewDeviceIndex(settings.uiPreviewDevice),
                settings.uiScaleMultiplier.ToString(CultureInfo.InvariantCulture),
                settings,
                hasCustomEndpoints,
                hasCustomName,
                shouldShowNetworkWarning);
        }
    }
}
