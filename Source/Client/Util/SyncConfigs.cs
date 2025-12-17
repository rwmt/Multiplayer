using System.Collections.Generic;
using System.IO;
using Verse;

namespace Multiplayer.Client.Util;

/// Responsible for saving a server's config files and retrieving them later.
public static class SyncConfigs
{
    private static readonly string TempConfigsPath = GenFilePaths.FolderUnderSaveData(JoinData.TempConfigsDir);

    public static void SaveConfigs(List<ModConfig> configs)
    {
        var tempDir = new DirectoryInfo(TempConfigsPath);
        tempDir.Delete(true);
        tempDir.Create();

        foreach (var config in configs)
            File.WriteAllText(Path.Combine(TempConfigsPath, $"Mod_{config.ModId}_{config.FileName}.xml"), config.Contents);
    }

    public static string GetConfigPath(string modId, string handleName) =>
        Path.Combine(TempConfigsPath, GenText.SanitizeFilename($"Mod_{modId}_{handleName}.xml"));
}
