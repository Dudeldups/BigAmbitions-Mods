#nullable enable
using System;
using System.IO;
using BAModAPI;
using UnityEngine;

namespace Pink
{
    internal static class PinkFileLogger
    {
        private static readonly object Gate = new object();

        private static string? filePath;
        private static IModLogger? gameLogger;
        private static bool enabled;
        private static bool verboseEnabled;
        private static bool initialized;

        internal static bool Enabled => enabled && initialized;
        internal static bool VerboseEnabled => Enabled && verboseEnabled;
        internal static string? FilePath => filePath;

        internal static void Initialize(string modId, IModLogger? logger, bool enableFileLogging, bool enableVerboseLogging)
        {
            enabled = enableFileLogging;
            verboseEnabled = enableVerboseLogging;
            gameLogger = logger;

            if (!enabled)
            {
                initialized = false;
                filePath = null;
                return;
            }

            try
            {
                var directory = Path.Combine(Application.persistentDataPath, "PinkCity");
                Directory.CreateDirectory(directory);

                filePath = Path.Combine(directory, "PinkCity.log");
                File.WriteAllText(
                    filePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] PinkCity log started. modId={modId}, persistentDataPath={Application.persistentDataPath}, verbose={verboseEnabled}{Environment.NewLine}");

                initialized = true;
                gameLogger?.Info($"PinkCity file log: {filePath}");
            }
            catch (Exception ex)
            {
                initialized = false;
                filePath = null;
                gameLogger?.Warn($"PinkCity file logger failed to initialize: {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static void Shutdown()
        {
            Info("PinkCity log closed.");
            initialized = false;
            enabled = false;
            verboseEnabled = false;
            filePath = null;
            gameLogger = null;
        }

        internal static void Info(string message, bool alsoGameLog = false)
        {
            Write("INFO", message);
            if (alsoGameLog && enabled)
                gameLogger?.Info(message);
        }

        internal static void Verbose(string message)
        {
            if (!VerboseEnabled)
                return;

            Write("VERBOSE", message);
        }

        internal static void Warn(string message, bool alsoGameLog = false)
        {
            Write("WARN", message);
            if (alsoGameLog && enabled)
                gameLogger?.Warn(message);
        }

        internal static void Error(string message, bool alsoGameLog = true)
        {
            Write("ERROR", message);
            if (alsoGameLog && enabled)
                gameLogger?.Error(message);
        }

        private static void Write(string level, string message)
        {
            if (!Enabled || string.IsNullOrEmpty(filePath))
                return;

            try
            {
                lock (Gate)
                {
                    File.AppendAllText(
                        filePath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Avoid recursive logging if the file system is not writable.
            }
        }
    }
}
