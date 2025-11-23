using System;
using UnityEngine;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist.Build;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Bootstraps settings and logging before any scene objects run.
    /// </summary>
    public static class WalletRuntime
    {
        private static readonly object Sync = new object();
        private static bool _initialized;

        public static Settings Settings { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            Initialize();
        }

        public static Settings GetSettings()
        {
            Initialize();
            return Settings;
        }

        private static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            lock (Sync)
            {
                if (_initialized)
                {
                    return;
                }

                try
                {
                    var settings = new Settings();
                    settings.LoadLogSettings();

                    var args = Environment.GetCommandLineArgs();
                    var logLevel = settings.logLevel;
                    var logOverwrite = settings.logOverwriteMode;
                    var logForceWorkingFolderUsage = false;

                    for (int i = 0; i < args.Length; i++)
                    {
                        switch (args[i])
                        {
                            case "--log-level":
                                {
                                    if (i + 1 < args.Length)
                                    {
                                        Enum.TryParse<Log.Level>(args[i + 1], true, out logLevel);
                                    }

                                    break;
                                }

                            case "--log-force-working-folder-usage":
                                {
                                    logForceWorkingFolderUsage = true;
                                    break;
                                }
                        }
                    }

                    Log.Init("poltergeist.log", logLevel, logForceWorkingFolderUsage, logOverwrite);
                    Log.Write("********************************************************\n" +
                               "************** Poltergeist Wallet started **************\n" +
                               "********************************************************\n" +
                               "Wallet version: " + Application.version + $" built on: {Info.Instance.BuildTime} UTC\n" +
                               "Log level: " + logLevel);

                    Settings = settings;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Startup] Logging bootstrap failed: {e}");
                }
                finally
                {
                    _initialized = true;
                }
            }
        }
    }
}
