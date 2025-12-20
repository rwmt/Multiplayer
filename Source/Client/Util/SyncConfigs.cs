using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client.Patches;
using Verse;

namespace Multiplayer.Client.Util;

/// Responsible for saving a server's config files and retrieving them later.
public static class SyncConfigs
{
    private static readonly string TempConfigsPath = GenFilePaths.FolderUnderSaveData(JoinData.TempConfigsDir);
    private const string RestartConfigsVariable = "MultiplayerRestartConfigs";

    public static bool Applicable { private set; get; }

    // The env variable will get inherited by the child process started in GenCommandLine.Restart
    public static void MarkApplicableForChildProcess() => Environment.SetEnvironmentVariable(RestartConfigsVariable, "true");

    public static void Init()
    {
        Applicable = Environment.GetEnvironmentVariable(RestartConfigsVariable) is "true";
        Environment.SetEnvironmentVariable(RestartConfigsVariable, "");
    }

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

// Affects both reading and writing
[EarlyPatch]
[HarmonyPatch(typeof(LoadedModManager), nameof(LoadedModManager.GetSettingsFilename))]
static class OverrideConfigsPatch
{
    private static Dictionary<(string, string), ModContentPack> modCache = new();

    static void Postfix(string modIdentifier, string modHandleName, ref string __result)
    {
        if (!SyncConfigs.Applicable)
            return;

        if (!modCache.TryGetValue((modIdentifier, modHandleName), out var mod))
        {
            mod = modCache[(modIdentifier, modHandleName)] =
                LoadedModManager.RunningModsListForReading.FirstOrDefault(m =>
                    m.FolderName == modIdentifier
                    && m.assemblies.loadedAssemblies.Any(a => a.GetTypes().Any(t => t.Name == modHandleName))
                );
        }

        if (mod == null)
            return;

        if (JoinData.ignoredConfigsModIds.Contains(mod.ModMetaData.PackageIdNonUnique))
            return;

        __result = SyncConfigs.GetConfigPath(mod.PackageIdPlayerFacing.ToLowerInvariant(), modHandleName);
    }
}

[EarlyPatch]
[HarmonyPatch]
static class HugsLib_OverrideConfigsPatch
{
    public static string HugsLibConfigOverridenPath =
        SyncConfigs.GetConfigPath(JoinData.HugsLibId, JoinData.HugsLibSettingsFile);

    private static readonly MethodInfo MethodToPatch =
        AccessTools.Method("HugsLib.Core.PersistentDataManager:GetSettingsFilePath");

    static bool Prepare() => MethodToPatch != null;

    static MethodInfo TargetMethod() => MethodToPatch;

    static void Prefix(object __instance)
    {
        if (!SyncConfigs.Applicable) return;
        if (__instance.GetType().Name != "ModSettingsManager") return;
        if (!File.Exists(HugsLibConfigOverridenPath)) return;
        __instance.SetPropertyOrField("OverrideFilePath", HugsLibConfigOverridenPath);
    }
}
