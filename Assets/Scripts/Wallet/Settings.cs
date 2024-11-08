using Phantasma.SDK;
using System;
using UnityEngine;
using System.Numerics;

namespace Poltergeist
{
    public enum NexusKind
    {
        Unknown,
        Main_Net,
        Test_Net,
        Local_Net,
        Custom
    }

    public enum EthereumNetwork
    {
        Unknown,
        Main_Net,
        Goerli,
        Local_Net
    }

    public enum BinanceSmartChainNetwork
    {
        Unknown,
        Main_Net,
        Test_Net,
        Local_Net
    }

    public enum UiThemes
    {
        Default,
        Phantasia
    }

    public enum MnemonicPhraseLength
    {
        Twelve_Words,
        Twenty_Four_Words
    }

    public enum PasswordMode
    {
        Ask_Always,
        Ask_Only_On_Login,
        Master_Password
    }

    public enum MnemonicPhraseVerificationMode
    {
        Full,
        Simplified
    }

    public static class SettingsExtension
    {
        public static bool IsValidURL(this string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            if (!(url.StartsWith("http://") || url.StartsWith("https://")))
            {
                return false;
            }

            return true;
        }
    }

    public class Settings
    {
        public const string PhantasmaRPCTag = "settings.phantasma.rpc.url";
        public const string PhantasmaExplorerTag = "settings.phantasma.explorer.url";
        public const string PhantasmaNftExplorerTag = "settings.phantasma.nft.explorer.url";
        public const string PhantasmaPoaUrlTag = "settings.phantasma.poa.url";
        public const string NexusNameTag = "settings.nexus.name";

        public const string NexusKindTag = "settings.nexus.kind";
        public const string CurrencyTag = "settings.currency";
        public const string GasPriceTag = "settings.fee.price";
        public const string GasLimitTag = "settings.fee.limit";

        public const string LogLevelTag = "log.level";
        public const string LogOverwriteModeTag = "log.overwrite.mode";

        public const string UiThemeNameTag = "ui.theme.name";
        public const string UiFramerateTag = "ui.framerate";

        public const string TtrsNftSortModeTag = "ttrs.nft.sort.mode";
        public const string NftSortModeTag = "nft.sort.mode";
        public const string NftSortDirectionTag = "nft.sort.direction";

        public const string LastVisitedFolderTag = "last.visited.folder";

        public const string MnemonicPhraseLengthTag = "mnemonic.phrase.length";
        public const string MnemonicPhraseVerificationModeTag = "mnemonic.phrase.verification.mode";

        public const string PasswordModeTag = "password.mode";

        public const string DevModeTag = "developer.mode";

        public string phantasmaRPCURL;
        public string phantasmaExplorer;
        public string phantasmaNftExplorer;
        public string phantasmaPoaUrl;
        public string nexusName;
        public string currency;
        public BigInteger feePrice;
        public BigInteger feeLimit;
        public NexusKind nexusKind;
        public Log.Level logLevel;
        public bool logOverwriteMode;
        public string uiThemeName;
        public int uiFramerate;
        public int ttrsNftSortMode;
        public int nftSortMode;
        public int nftSortDirection;
        public string lastVisitedFolder;
        public MnemonicPhraseLength mnemonicPhraseLength;
        public MnemonicPhraseVerificationMode mnemonicPhraseVerificationMode;
        public PasswordMode passwordMode;
        public bool devMode;

        public override string ToString()
        {
            return "Nexus kind: " + this.nexusKind.ToString() + "\n" +
                "Phantasma RPC: " + this.phantasmaRPCURL + "\n" +
                "Phantasma Explorer: " + this.phantasmaExplorer + "\n" +
                "Phantasma NFT Explorer: " + this.phantasmaNftExplorer + "\n" +
                "Phantasma POA URL: " + this.phantasmaPoaUrl + "\n" +
                "Fee price: " + this.feePrice + "\n" +
                "Fee limit: " + this.feeLimit + "\n" +
                "Nexus name: " + this.nexusName + "\n" +
                "Currency: " + this.currency + "\n" +
                "UI theme: " + this.uiThemeName + "\n" +
                "UI framerate: " + this.uiFramerate + "\n" +
                "Log level: " + this.logLevel + "\n" +
                "Log overwrite: " + this.logOverwriteMode + "\n" +
                "TTRS NFT sort mode: " + this.ttrsNftSortMode + "\n" +
                "NFT sort mode: " + this.nftSortMode + "\n" +
                "NFT sort direction: " + this.nftSortDirection + "\n" +
                "Mnemonic phrase length: " + this.mnemonicPhraseLength + "\n" +
                "Mnemonic phrase verification mode: " + this.mnemonicPhraseVerificationMode + "\n" +
                "Password mode: " + this.passwordMode + "\n" +
                "Developer mode: " + this.devMode;
        }

        public void LoadLogSettings()
        {
            var logLevel = PlayerPrefs.GetString(LogLevelTag, Log.Level.Networking.ToString());
            if (!Enum.TryParse<Log.Level>(logLevel, true, out this.logLevel))
            {
                this.logLevel = Log.Level.Networking;
            }

            this.logOverwriteMode = PlayerPrefs.GetInt(LogOverwriteModeTag, 1) != 0;
        }

        public void Load()
        {
            Log.Write("Settings: Loading...");

            var nexusKind = PlayerPrefs.GetString(NexusKindTag, NexusKind.Main_Net.ToString());
            if (!Enum.TryParse<NexusKind>(nexusKind, true, out this.nexusKind))
            {
                this.nexusKind = NexusKind.Unknown;
            }

            this.phantasmaRPCURL = PlayerPrefs.GetString(PhantasmaRPCTag, GetDefaultValue(PhantasmaRPCTag));
            if (this.nexusKind == NexusKind.Main_Net || this.nexusKind == NexusKind.Test_Net)
            {
                // For mainnet/testnet we always load defaults for hidden settings,
                // to avoid dealing with "stuck" values from old PG version that had different defaults.
                this.phantasmaExplorer = GetDefaultValue(PhantasmaExplorerTag);
                this.phantasmaNftExplorer = GetDefaultValue(PhantasmaNftExplorerTag);
                this.phantasmaPoaUrl = GetDefaultValue(PhantasmaPoaUrlTag);
                this.nexusName = GetDefaultValue(NexusNameTag);
            }
            else
            {
                this.phantasmaExplorer = PlayerPrefs.GetString(PhantasmaExplorerTag, GetDefaultValue(PhantasmaExplorerTag));
                this.phantasmaNftExplorer = PlayerPrefs.GetString(PhantasmaNftExplorerTag, GetDefaultValue(PhantasmaNftExplorerTag));
                this.phantasmaPoaUrl = PlayerPrefs.GetString(PhantasmaPoaUrlTag, GetDefaultValue(PhantasmaPoaUrlTag));
                this.nexusName = PlayerPrefs.GetString(NexusNameTag, GetDefaultValue(NexusNameTag));
            }

            this.currency = PlayerPrefs.GetString(CurrencyTag, "USD");

            var defaultGasPrice = 100000;
            if (!BigInteger.TryParse(PlayerPrefs.GetString(GasPriceTag, defaultGasPrice.ToString()), out feePrice))
            {
                this.feePrice = defaultGasPrice;
            }

            var defaultGasLimit = 21000;
            if (!BigInteger.TryParse(PlayerPrefs.GetString(GasLimitTag, defaultGasLimit.ToString()), out feeLimit))
            {
                this.feeLimit = defaultGasLimit;
            }

            this.uiThemeName = PlayerPrefs.GetString(UiThemeNameTag, UiThemes.Default.ToString());
            this.uiFramerate = PlayerPrefs.GetInt(UiFramerateTag, -1);

            LoadLogSettings();

            this.ttrsNftSortMode = PlayerPrefs.GetInt(TtrsNftSortModeTag, 0);
            this.nftSortMode = PlayerPrefs.GetInt(NftSortModeTag, 0);
            this.nftSortDirection = PlayerPrefs.GetInt(NftSortDirectionTag, 0);

            var documentFolderPath = GetDocumentPath();
            this.lastVisitedFolder = PlayerPrefs.GetString(LastVisitedFolderTag, documentFolderPath);
            if (!System.IO.Directory.Exists(this.lastVisitedFolder))
                this.lastVisitedFolder = documentFolderPath;

            var mnemonicPhraseLength = PlayerPrefs.GetString(MnemonicPhraseLengthTag, MnemonicPhraseLength.Twelve_Words.ToString());
            if (!Enum.TryParse<MnemonicPhraseLength>(mnemonicPhraseLength, true, out this.mnemonicPhraseLength))
            {
                this.mnemonicPhraseLength = MnemonicPhraseLength.Twelve_Words;
            }

            var mnemonicPhraseVerificationMode = PlayerPrefs.GetString(MnemonicPhraseVerificationModeTag, MnemonicPhraseVerificationMode.Full.ToString());
            if (!Enum.TryParse<MnemonicPhraseVerificationMode>(mnemonicPhraseVerificationMode, true, out this.mnemonicPhraseVerificationMode))
            {
                this.mnemonicPhraseVerificationMode = MnemonicPhraseVerificationMode.Full;
            }

            var passwordMode = PlayerPrefs.GetString(PasswordModeTag, PasswordMode.Ask_Always.ToString());
            if (!Enum.TryParse<PasswordMode>(passwordMode, true, out this.passwordMode))
            {
                this.passwordMode = PasswordMode.Ask_Always;
            }

            this.devMode = PlayerPrefs.GetInt(DevModeTag, 0) != 0;

            Log.Write("Settings: Load: " + ToString());
        }

        public string GetDefaultValue(string tag)
        {
            string _return_value;

            switch (tag)
            {
                /*case PhantasmaRPCTag:
                    switch (nexusKind)
                    {
                        case NexusKind.Main_Net:
                            return "auto";

                        case NexusKind.Local_Net:
                            return "http://localhost:7077/rpc";

                        default:
                            return "http://45.76.88.140:7076/rpc";
                    }
                    break;
                    */

                case PhantasmaRPCTag:
                    switch (nexusKind)
                    {
                        case NexusKind.Main_Net:
                            _return_value = "https://pharpc1.phantasma.info/rpc";
                            break;

                        case NexusKind.Test_Net:
                            _return_value = "https://testnet.phantasma.info/rpc";
                            break;

                        case NexusKind.Local_Net:
                            _return_value = "http://localhost:7077/rpc";
                            break;

                        default:
                            _return_value = "https://pharpc1.phantasma.info/rpc";
                            break;
                    }
                    break;

                case PhantasmaExplorerTag:
                    switch (nexusKind)
                    {
                        case NexusKind.Main_Net:
                            _return_value = "https://explorer.phantasma.info";
                            break;

                        case NexusKind.Test_Net:
                            _return_value = "https://test-explorer.phantasma.info/";
                            break;

                        case NexusKind.Local_Net:
                            _return_value = "http://localhost:7074/";
                            break;

                        default:
                            _return_value = "https://explorer.phantasma.info";
                            break;
                    }
                    break;

                case PhantasmaNftExplorerTag:
                    switch (nexusKind)
                    {
                        case NexusKind.Main_Net:
                            _return_value = "https://ghostmarket.io/asset/pha";
                            break;

                        case NexusKind.Test_Net:
                            _return_value = "https://testnet.ghostmarket.io/asset/phat";
                            break;

                        case NexusKind.Local_Net:
                            _return_value = "https://dev.ghostmarket.io/asset/pha";
                            break;

                        default:
                            _return_value = "https://ghostmarket.io/asset/pha";
                            break;
                    }
                    break;

                case PhantasmaPoaUrlTag:
                    switch (nexusKind)
                    {
                        case NexusKind.Main_Net:
                            _return_value = "https://poa.phantasma.info";
                            break;

                        default:
                            _return_value = "https://poa.phantasma.info";
                            break;
                    }
                    break;

                case NexusNameTag:
                    switch (nexusKind)
                    {
                        case NexusKind.Main_Net:
                            _return_value = "mainnet";
                            break;

                        case NexusKind.Test_Net:
                            _return_value = "testnet";
                            break;

                        default:
                            _return_value = "simnet";
                            break;
                    }
                    break;

                default:
                    return "";
            }

            Log.Write("Settings: GetDefaultValue(" + tag + "->default): " + _return_value, Log.Level.Debug2);
            return _return_value;
        }

        public void Save()
        {
            PlayerPrefs.SetString(NexusKindTag, nexusKind.ToString());
            PlayerPrefs.SetString(PhantasmaRPCTag, this.phantasmaRPCURL);
            PlayerPrefs.SetString(PhantasmaExplorerTag, this.phantasmaExplorer);
            PlayerPrefs.SetString(PhantasmaNftExplorerTag, this.phantasmaNftExplorer);
            PlayerPrefs.SetString(PhantasmaPoaUrlTag, this.phantasmaPoaUrl);
            PlayerPrefs.SetString(GasPriceTag, this.feePrice.ToString());
            PlayerPrefs.SetString(GasLimitTag, this.feeLimit.ToString());

            PlayerPrefs.SetString(NexusNameTag, this.nexusName);
            PlayerPrefs.SetString(CurrencyTag, this.currency);
            PlayerPrefs.SetString(UiThemeNameTag, this.uiThemeName);
            PlayerPrefs.SetInt(UiFramerateTag, this.uiFramerate);
            PlayerPrefs.SetString(LogLevelTag, this.logLevel.ToString());
            PlayerPrefs.SetInt(LogOverwriteModeTag, this.logOverwriteMode ? 1 : 0);
            PlayerPrefs.SetString(MnemonicPhraseLengthTag, this.mnemonicPhraseLength.ToString());
            PlayerPrefs.SetString(MnemonicPhraseVerificationModeTag, this.mnemonicPhraseVerificationMode.ToString());
            PlayerPrefs.SetString(PasswordModeTag, this.passwordMode.ToString());
            PlayerPrefs.SetInt(DevModeTag, this.devMode ? 1 : 0);
            PlayerPrefs.Save();

            Log.Write("Settings: Save: " + ToString());
        }

        public void SaveOnExit()
        {
            PlayerPrefs.SetInt(TtrsNftSortModeTag, this.ttrsNftSortMode);
            PlayerPrefs.SetInt(NftSortModeTag, this.nftSortMode);
            PlayerPrefs.SetInt(NftSortDirectionTag, this.nftSortDirection);
            PlayerPrefs.SetString(PhantasmaRPCTag, this.phantasmaRPCURL);
            PlayerPrefs.SetString(LastVisitedFolderTag, this.lastVisitedFolder);
            PlayerPrefs.Save();

            Log.Write("Settings: Save on exit: TTRS NFT sort mode: " + ttrsNftSortMode + "\n" +
                      "                        NFT sort mode: " + nftSortMode + "\n" +
                      "                        NFT sort direction: " + nftSortDirection + "\n" +
                      "                        Phantasma RPC: " + phantasmaRPCURL + "\n",
                      Log.Level.Debug1);
        }

        public void RestoreEndpoints(bool restoreName)
        {
            //this.phantasmaRPCURL = this.GetDefaultValue(PhantasmaRPCTag);
            this.phantasmaRPCURL = this.GetDefaultValue(PhantasmaRPCTag);
            this.phantasmaExplorer = this.GetDefaultValue(PhantasmaExplorerTag);
            this.phantasmaNftExplorer = this.GetDefaultValue(PhantasmaNftExplorerTag);
            this.phantasmaPoaUrl = this.GetDefaultValue(PhantasmaPoaUrlTag);

            if (restoreName)
            {
                this.nexusName = this.GetDefaultValue(NexusNameTag);
            }

            Log.Write("Settings: Restore endpoints: restoreName mode: " + restoreName + "\n" +
                      "                             Phantasma RPC: " + this.phantasmaRPCURL + "\n" +
                      "                             Phantasma Explorer: " + this.phantasmaExplorer + "\n" +
                      "                             Phantasma NFT Explorer: " + this.phantasmaNftExplorer + "\n" +
                      "                             Phantasma POA URL: " + this.phantasmaPoaUrl + "\n" +
                      "                             Nexus name: " + this.nexusName,
                      Log.Level.Debug1);
        }

        private string GetDocumentPath()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer)
                return System.IO.Path.Combine(Environment.ExpandEnvironmentVariables("%userprofile%"), "Documents");
            else if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer)
                return System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "/Documents/";
            else if (Application.platform == RuntimePlatform.LinuxPlayer)
                return System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            else
            {
                return Application.persistentDataPath;
            }
        }
        public string GetLastVisitedFolder()
        {
            return this.lastVisitedFolder;
        }
        public void SetLastVisitedFolder(string folderPath)
        {
            this.lastVisitedFolder = folderPath;
        }
    }
}
