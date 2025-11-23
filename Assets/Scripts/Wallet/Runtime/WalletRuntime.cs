using System;
using UnityEngine;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist.Build;
using System.Threading.Tasks;

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
        public static string StartupError { get; private set; }
        public static bool HasStartupError => !string.IsNullOrEmpty(StartupError);

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
                    RegisterGlobalExceptionHandlers();
                    Log.Write("[Startup] WalletRuntime initialized.");
                }
                catch (Exception e)
                {
                    StartupError = $"Logging bootstrap failed: {e}";
                    Debug.LogError($"[Startup] {StartupError}");
                }
                finally
                {
                    _initialized = true;
                }
            }
        }

        private static void RegisterGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                if (ex != null)
                {
                    ReportFatal("UnhandledException", ex);
                }
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                ReportFatal("UnobservedTaskException", args.Exception);
            };
        }

        public static void ReportFatal(string location, Exception ex)
        {
            var message = $"Fatal {location}: {ex}";
            StartupError = message;
            Debug.LogError("[Startup] " + message);
            try
            {
                Log.Write(message);
            }
            catch
            {
                // ignore logging failures
            }
        }
    }
}
