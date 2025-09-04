using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    /// <summary>
    /// Collects the game logs and loaded mods to be included with desync file.
    /// </summary>
    public static class LogGenerator
    {
        private const int MaxLogLineCount = 10000;

        internal static string PrepareLogData()
        {
            try
            {
                var logSection = GetLogFileContents();
                if (logSection != null)
                {
                    logSection = NormalizeLineEndings(logSection);
                    // redact logs for privacy
                    logSection = RedactRimworldPaths(logSection);
                    logSection = RedactPlayerConnectInformation(logSection);
                    logSection = RedactRendererInformation(logSection);
                    logSection = RedactHomeDirectoryPaths(logSection);
                    logSection = RedactSteamId(logSection);
                    logSection = RedactUselessLines(logSection);
                    logSection = TrimExcessLines(logSection);
                }
                else logSection = "Could not find the log";

                return logSection;
            }
            catch
            {
                // ignored
            }

            return null;
        }

        private static string NormalizeLineEndings(string log)
        {
            return log.Replace("\r\n", "\n");
        }

        private static string TrimExcessLines(string log)
        {
            var indexOfLastNewline = IndexOfOccurrence(log, '\n', MaxLogLineCount);
            if (indexOfLastNewline >= 0)
            {
                log = $"{log.Substring(0, indexOfLastNewline + 1)}(log trimmed to {MaxLogLineCount:N0} lines.)";
            }
            return log;
        }

        private static int IndexOfOccurrence(string s, char match, int occurrence)
        {
            var currentOccurrence = 1;
            var currentIndex = 0;
            while (currentOccurrence <= occurrence && (currentIndex = s.IndexOf(match, currentIndex + 1)) != -1)
            {
                if (currentOccurrence == occurrence) return currentIndex;
                currentOccurrence++;
            }
            return -1;
        }

        private static string RedactUselessLines(string log)
        {
            log = Regex.Replace(log, "Non platform assembly:.+\n", "");
            log = Regex.Replace(log, "Platform assembly: .+\n", "");
            log = Regex.Replace(log, "Fallback handler could not load library.+\n", "");
            log = Regex.Replace(log, "- Completed reload, in [\\d\\. ]+ seconds\n", "");
            log = Regex.Replace(log, "UnloadTime: [\\d\\. ]+ ms\n", "");
            log = Regex.Replace(log, "<RI> Initializing input\\.\r\n", "");
            log = Regex.Replace(log, "<RI> Input initialized\\.\r\n", "");
            log = Regex.Replace(log, "<RI> Initialized touch support\\.\r\n", "");
            log = Regex.Replace(log, "\\(Filename: C:/buildslave.+\n", "");
            log = Regex.Replace(log, "\n \n", "\n");
            return log;
        }

        // only relevant on linux
        private static string RedactSteamId(string log)
        {
            const string idReplacement = "[Steam Id redacted]";
            return Regex.Replace(log, "Steam_SetMinidumpSteamID.+", idReplacement);
        }

        private static string RedactHomeDirectoryPaths(string log)
        {
            // not necessary for windows logs
            if (UnityData.platform is RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor)
                return log;

            const string pathReplacement = "[Home_dir]";
            var homePath = Environment.GetEnvironmentVariable("HOME");
            if (homePath == null) return log;
            return log.Replace(homePath, pathReplacement);
        }

        private static string RedactRimworldPaths(string log)
        {
            const string pathReplacement = "[Rimworld_dir]";
            // easiest way to get the game folder is one level up from dataPath
            var appPath = Path.GetFullPath(Application.dataPath);
            var pathParts = appPath.Split(Path.DirectorySeparatorChar).ToList();
            pathParts.RemoveAt(pathParts.Count - 1);
            appPath = pathParts.Join(Path.DirectorySeparatorChar.ToString());
            log = log.Replace(appPath, pathReplacement);
            if (Path.DirectorySeparatorChar != '/')
            {
                // log will contain mixed windows and unix style paths
                appPath = appPath.Replace(Path.DirectorySeparatorChar, '/');
                log = log.Replace(appPath, pathReplacement);
            }
            return log;
        }

        private static string RedactRendererInformation(string log)
        {
            // apparently renderer information can appear multiple times in the log
            for (var i = 0; i < 5; i++)
            {
                var redacted = RedactString(log, "GfxDevice: ", "\nBegin MonoManager", "[Renderer information redacted]");
                if (log.Length == redacted.Length) break;
                log = redacted;
            }
            return log;
        }

        private static string RedactPlayerConnectInformation(string log)
        {
            return RedactString(log, "PlayerConnection ", "Initialize engine", "[PlayerConnect information redacted]\n");
        }

        private static string GetLogFileContents()
        {
            var filePath = TryGetLogFilePath();
            if (filePath.NullOrEmpty() || !File.Exists(filePath))
            {
                throw new FileNotFoundException("Log file not found:" + filePath);
            }

            var tempPath = Path.GetTempFileName();
            File.Delete(tempPath);
            // we need to copy the log file since the original is already opened for writing by Unity
            File.Copy(filePath, tempPath);
            var fileContents = File.ReadAllText(tempPath);
            File.Delete(tempPath);

            return fileContents;
        }

        private static string RedactString(string original, string redactStart, string redactEnd, string replacement)
        {
            var startIndex = original.IndexOf(redactStart, StringComparison.Ordinal);
            var endIndex = original.IndexOf(redactEnd, StringComparison.Ordinal);
            var result = original;

            if (startIndex < 0 || endIndex < 0) return result;

            var logTail = original.Substring(endIndex);
            result = original.Substring(0, startIndex + redactStart.Length);
            result += replacement;
            result += logTail;

            return result;
        }

        /// <summary>
        /// Attempts to return the path of the log file Unity is writing to.
        /// </summary>
        private static string TryGetLogFilePath()
        {
            if (TryGetCommandLineOptionValue("logfile") is { } cmdLog)
            {
                return cmdLog;
            }

            return UnityData.platform switch
            {
                RuntimePlatform.LinuxPlayer
                    => @"/tmp/rimworld_log",
                RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor
                    => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), $"Library/Logs/{Application.companyName}/{Application.productName}/Player.log"),
                RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor
                    => Path.Combine(Application.persistentDataPath, "Player.log"),
                _ => null
            };
        }

        private static string TryGetCommandLineOptionValue(string key)
        {
            const string keyPrefix = "-";
            var prefixedKey = keyPrefix + key;
            var valueExpectedNext = false;
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg.EqualsIgnoreCase(prefixedKey))
                {
                    valueExpectedNext = true;
                }
                else if (valueExpectedNext && !arg.StartsWith(keyPrefix) && !arg.NullOrEmpty())
                {
                    return arg;
                }
            }
            return null;
        }
    }
}
