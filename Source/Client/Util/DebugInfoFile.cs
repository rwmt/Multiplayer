using System;
using System.IO;
using System.IO.Compression;
using Multiplayer.Client.Desyncs;
using Multiplayer.Common;
using Multiplayer.Common.Util;
using Verse;

namespace Multiplayer.Client.Util;

public static class DebugInfoFile
{
    public static void Generate(bool openDirectory = true)
    {
        Log.Message($"Generating debug info at {DateTime.Now:u}");
        var logs = LogGenerator.PrepareLogData();
        var metadata = MetadataGenerator.Generate();

        var zipPath = Path.Combine(Multiplayer.LogsDir, "debug-info.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);

        using (var zip = MpZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.AddEntry("logs.txt", logs);
            zip.AddEntry("metadata.txt", metadata);
        }

        if (openDirectory) ShellOpenDirectory.Execute(Multiplayer.LogsDir);
    }
}
