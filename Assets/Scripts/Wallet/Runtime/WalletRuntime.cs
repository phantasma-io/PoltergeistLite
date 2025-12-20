using System;
using UnityEngine;
using System.IO;
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
        private const string DefaultLogFileName = "poltergeist.log";
        private static bool _initialized;
        private static bool _logForceWorkingFolderUsage;
        private static Log.Level _logLevel;
        private static bool _logOverwrite;
        private static bool _unityLogHooked;

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
                    _logLevel = settings.logLevel;
                    _logOverwrite = settings.logOverwriteMode;
                    _logForceWorkingFolderUsage = false;

                    for (int i = 0; i < args.Length; i++)
                    {
                        switch (args[i])
                        {
                            case "--log-level":
                                {
                                    if (i + 1 < args.Length)
                                    {
                                        Enum.TryParse<Log.Level>(args[i + 1], true, out _logLevel);
                                    }

                                    break;
                                }

                            case "--log-force-working-folder-usage":
                                {
                                    _logForceWorkingFolderUsage = true;
                                    break;
                                }
                        }
                    }

                    var logFilePath = ResolveLogFilePath(settings);
                    Log.Init(logFilePath, _logLevel, _logForceWorkingFolderUsage, _logOverwrite);
                    EnsureUnityLogHook();
                    Log.Write("********************************************************\n" +
                               "************** Poltergeist Wallet started **************\n" +
                               "********************************************************\n" +
                               "Wallet version: " + Application.version + $" built on: {Info.Instance.BuildTime} UTC\n" +
                               "Log level: " + _logLevel);

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

        private static void EnsureUnityLogHook()
        {
            if (_unityLogHooked)
            {
                return;
            }

            // Use only the threaded callback; it receives main-thread logs too and avoids duplicate events.
            Application.logMessageReceivedThreaded += OnUnityLogMessageReceived;
            _unityLogHooked = true;
        }

        private static void OnUnityLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            try
            {
                // Prevent re-logging messages that originate from Log.Write (it already writes to the file).
                if (Log.IsWriting)
                {
                    return;
                }

                const string prefix = "[Unity]";
                switch (type)
                {
                    case LogType.Error:
                    case LogType.Exception:
                    case LogType.Assert:
                        Log.WriteWarning($"{prefix} {type}: {condition}\n{stackTrace}");
                        break;
                    case LogType.Warning:
                        Log.WriteWarning($"{prefix} Warning: {condition}");
                        break;
                    default:
                        Log.Write($"{prefix} {type}: {condition}");
                        break;
                }
            }
            catch
            {
                // Avoid recursive failures while logging Unity messages.
            }
        }

        private static string ResolveLogFilePath(Settings settings)
        {
            var logFilePath = DefaultLogFileName;
            if (settings == null)
            {
                return logFilePath;
            }

            if (!string.IsNullOrWhiteSpace(settings.logFolderPath))
            {
                var customFolder = settings.logFolderPath.Trim();
                try
                {
                    Directory.CreateDirectory(customFolder);
                    logFilePath = Path.Combine(customFolder, DefaultLogFileName);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Startup] Failed to use custom log folder '{settings.logFolderPath}': {e.Message}");
                }
            }

            return logFilePath;
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

        public static void ReconfigureLogging(Settings settings)
        {
            if (!_initialized || settings == null)
            {
                return;
            }

            lock (Sync)
            {
                _logLevel = settings.logLevel;
                _logOverwrite = settings.logOverwriteMode;
                var logFilePath = ResolveLogFilePath(settings);
                try
                {
                    Log.Init(logFilePath, _logLevel, _logForceWorkingFolderUsage, _logOverwrite);
                    Log.Write($"[Startup] Log path set to '{logFilePath}'");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Startup] Failed to reconfigure logging: {e}");
                }
            }
        }
    }
}
