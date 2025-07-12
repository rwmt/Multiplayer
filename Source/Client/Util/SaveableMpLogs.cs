using System;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Util;

public class SaveableMpLogs
{
    private const int MaxFiles = 10;
    private const string FilePrefix = "MpLog-";
    private const string FileExtension = ".log";

    private static string _currentLogFile = null;

    public static void InitMpLogs()
    {
        try
        {
            _currentLogFile = FindFileNameForNextFile();
            
            if (string.IsNullOrEmpty(_currentLogFile))
            {
                Log.Warning("SaveableMpLogs: Could not determine log file name, skipping log initialization");
                return;
            }

            using var stream = File.Open(_currentLogFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.WriteLine(GetLogDetails());
        }
        catch (Exception e)
        {
            Log.Error($"SaveableMpLogs: Exception writing initial log info: {e}");
            _currentLogFile = null; // Reset on error
        }
    }

    public static void ResetMpLogs() => _currentLogFile = null;

    public static void AddLog(string type, string logText)
    {
        try
        {
            if (Multiplayer.Client == null)
                return;

            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(logText))
            {
                Log.Warning("SaveableMpLogs: Invalid log parameters, skipping log entry");
                return;
            }

            if (_currentLogFile == null)
            {
                InitMpLogs();
                if (_currentLogFile == null) // Still null after init attempt
                    return;
            }

            int ticks = Multiplayer.Client == null ? -1 : Find.TickManager?.TicksGame ?? -1;
            int mapTicks = Find.CurrentMap?.AsyncTime()?.mapTicks ?? -1;

            using var stream = File.Open(_currentLogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.WriteLine($"[{type}] [{ticks}] [{mapTicks}] {logText}");
        }
        catch (Exception e)
        {
            Log.Error($"SaveableMpLogs: Exception writing log info: {e}");
            // Don't reset _currentLogFile here as it might be a temporary file system issue
        }
    }

    private static string GetLogDetails()
    {
        try
        {
            var logDetails = new StringBuilder()
                .AppendLine($"Multiplayer Log - {DateTime.Now}")
                .AppendLine("\n###Version Data###")
                .AppendLine($"Multiplayer Mod Version|||{MpVersion.Version}")
                .AppendLine($"Rimworld Version and Rev|||{VersionControl.CurrentVersionStringWithRev}")
                .AppendLine("\n###Debug Options###")
                .AppendLine($"Multiplayer Debug Build - Client|||{MpVersion.IsDebug}")
                .AppendLine($"Multiplayer Debug Mode - Host|||{Multiplayer.GameComp?.debugMode ?? false}")
                .AppendLine($"Rimworld Developer Mode - Client|||{Prefs.DevMode}")
                .AppendLine("\n###Server Info###")
                .AppendLine($"Async time active|||{Multiplayer.GameComp?.asyncTime ?? false}")
                .AppendLine($"Multifaction active|||{Multiplayer.GameComp?.multifaction ?? false}")
                .AppendLine("\n###OS Info###")
                .AppendLine($"OS Type|||{SystemInfo.operatingSystemFamily}")
                .AppendLine($"OS Name and Version|||{SystemInfo.operatingSystem}")
                .AppendLine("\n======================================================")
                .AppendLine("###Log Start###")
                .AppendLine("======================================================");
            return logDetails.ToString();
        }
        catch (Exception e)
        {
            Log.Error($"SaveableMpLogs: Exception getting log details: {e}");
            return $"Multiplayer Log - {DateTime.Now}\nError getting full log details: {e.Message}\n======================================================";
        }
    }

    private static string FindFileNameForNextFile()
    {
        try
        {
            // Get player directory
            string directory = Path.Combine(Multiplayer.MpLogsDir);

            // Ensure the directory exists
            Directory.CreateDirectory(directory);

            // Get all existing logs
            FileInfo[] files = new DirectoryInfo(directory).GetFiles($"{FilePrefix}*{FileExtension}");

            // Delete any pushing us over the limit, and reserve room for one more
            if (files.Length > MaxFiles - 1)
            {
                try
                {
                    files.OrderByDescending(f => f.LastWriteTime).Skip(MaxFiles - 1).Do(DeleteFileSilent);
                }
                catch (Exception e)
                {
                    Log.Warning($"SaveableMpLogs: Exception cleaning up old log files: {e}");
                }
            }

            // Find the current max number
            int max = 0;
            foreach (FileInfo file in files)
            {
                try
                {
                    // Get name without extension and prefix
                    string fileName = Path.GetFileNameWithoutExtension(file.Name);
                    if (fileName.Length > FilePrefix.Length)
                    {
                        string parsedName = fileName[FilePrefix.Length..];

                        // Try to parse the number and update max if it's greater
                        if (int.TryParse(parsedName, out int result) && result > max)
                            max = result;
                    }
                }
                catch (Exception e)
                {
                    Log.Warning($"SaveableMpLogs: Exception processing file {file.Name}: {e}");
                }
            }

            return Path.Combine(directory, $"{FilePrefix}{max + 1:00}{FileExtension}");
        }
        catch (Exception e)
        {
            Log.Error($"SaveableMpLogs: Exception finding log file name: {e}");
            return null;
        }
    }

    private static void DeleteFileSilent(FileInfo file)
    {
        try
        {
            if (file?.Exists == true)
            {
                file.Delete();
            }
        }
        catch (Exception)
        {
            // Silently ignore all exceptions when deleting old log files
        }
    }
}
