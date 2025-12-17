using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    private static readonly string TempConfigsPath = GenFilePaths.FolderUnderSaveData("MultiplayerTempConfigs");
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

    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    public static string[] ignoredConfigsModIds =
    [
        // todo unhardcode it
        "rwmt.multiplayer",
        "hodlhodl.twitchtoolkit", // contains username
        "dubwise.dubsmintmenus",
        "dubwise.dubsmintminimap",
        "arandomkiwi.rimthemes",
        "brrainz.cameraplus",
        "giantspacehamster.moody",
        "fluffy.modmanager",
        "jelly.modswitch",
        "betterscenes.rimconnect", // contains secret key for streamer
        "jaxe.rimhud",
        "telefonmast.graphicssettings",
        "derekbickley.ltocolonygroupsfinal",
        "dra.multiplayercustomtickrates", // syncs its own settings
        "merthsoft.designatorshapes" // settings for UI and stuff meaningless for MP
        //"zetrith.prepatcher",
    ];

    public const string HugsLibId = "unlimitedhugs.hugslib";
    public const string HugsLibSettingsFile = "ModSettings";

    public static List<ModConfig> GetSyncableConfigContents(List<string> modIds)
    {
        var list = new List<ModConfig>();

        foreach (var modId in modIds)
        {
            if (ignoredConfigsModIds.Contains(modId)) continue;

            var mod = LoadedModManager.RunningMods.FirstOrDefault(m =>
                m.PackageIdPlayerFacing.ToLowerInvariant() == modId);
            if (mod == null) continue;

            foreach (var modInstance in LoadedModManager.runningModClasses.Values)
            {
                if (modInstance.modSettings == null) continue;
                if (!mod.assemblies.loadedAssemblies.Contains(modInstance.GetType().Assembly)) continue;

                var instanceName = modInstance.GetType().Name;

                // This path may point to configs downloaded from the server
                var file = LoadedModManager.GetSettingsFilename(mod.FolderName, instanceName);

                if (File.Exists(file))
                    list.Add(GetConfigCatchError(file, modId, instanceName));
            }
        }

        // Special case for HugsLib
        if (modIds.Contains(HugsLibId) && JoinData.GetInstalledMod(HugsLibId) is { Active: true })
        {
            var hugsConfig = HugsLib_OverrideConfigsPatch.HugsLibConfigIsOverriden
                ? HugsLib_OverrideConfigsPatch.HugsLibConfigOverridePath
                : Path.Combine(GenFilePaths.SaveDataFolderPath, "HugsLib", "ModSettings.xml");

            if (File.Exists(hugsConfig))
                list.Add(GetConfigCatchError(hugsConfig, HugsLibId, HugsLibSettingsFile));
        }

        return list;

        ModConfig GetConfigCatchError(string path, string id, string file)
        {
            try
            {
                return new ModConfig(id, file, Contents: File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Log.Error($"Exception getting config contents {file}: {e}");
                return new ModConfig(id, "ERROR", "");
            }
        }
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

        if (SyncConfigs.ignoredConfigsModIds.Contains(mod.ModMetaData.PackageIdNonUnique))
            return;

        __result = SyncConfigs.GetConfigPath(mod.PackageIdPlayerFacing.ToLowerInvariant(), modHandleName);
    }
}

[EarlyPatch]
[HarmonyPatch]
static class HugsLib_OverrideConfigsPatch
{
    public static string HugsLibConfigOverridePath =
        SyncConfigs.GetConfigPath(SyncConfigs.HugsLibId, SyncConfigs.HugsLibSettingsFile);
    public static bool HugsLibConfigIsOverriden => File.Exists(HugsLibConfigOverridePath);

    private static readonly MethodInfo MethodToPatch =
        AccessTools.Method("HugsLib.Core.PersistentDataManager:GetSettingsFilePath");

    static bool Prepare() => MethodToPatch != null;

    static MethodInfo TargetMethod() => MethodToPatch;

    static void Prefix(object __instance)
    {
        if (!SyncConfigs.Applicable) return;
        if (__instance.GetType().Name != "ModSettingsManager") return;
        if (!HugsLibConfigIsOverriden) return;
        __instance.SetPropertyOrField("OverrideFilePath", HugsLibConfigOverridePath);
    }
}
