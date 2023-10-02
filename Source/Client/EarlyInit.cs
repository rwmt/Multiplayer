using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client.Patches;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client;

public static class EarlyInit
{
    public const string RestartConnectVariable = "MultiplayerRestartConnect";
    public const string RestartConfigsVariable = "MultiplayerRestartConfigs";

    internal static void ProcessEnvironment()
    {
        if (!Environment.GetEnvironmentVariable(RestartConnectVariable).NullOrEmpty())
        {
            Multiplayer.restartConnect = Environment.GetEnvironmentVariable(RestartConnectVariable);
            Environment.SetEnvironmentVariable(RestartConnectVariable, ""); // Effectively unsets it
        }

        if (!Environment.GetEnvironmentVariable(RestartConfigsVariable).NullOrEmpty())
        {
            Multiplayer.restartConfigs = Environment.GetEnvironmentVariable(RestartConfigsVariable) == "true";
            Environment.SetEnvironmentVariable(RestartConfigsVariable, "");
        }
    }

    internal static void EarlyPatches(Harmony harmony)
    {
        // Might fix some mod desyncs
        harmony.PatchMeasure(
            AccessTools.Constructor(typeof(Def), Type.EmptyTypes),
            new HarmonyMethod(typeof(RandPatches), nameof(RandPatches.Prefix)),
            new HarmonyMethod(typeof(RandPatches), nameof(RandPatches.Postfix))
        );

        Assembly.GetCallingAssembly().GetTypes().Do(type =>
        {
            if (type.IsDefined(typeof(EarlyPatchAttribute)))
                harmony.CreateClassProcessor(type).Patch();
        });

#if DEBUG
        DebugPatches.Init();
#endif
    }

    internal static void InitSync()
    {
        using (DeepProfilerWrapper.Section("Multiplayer SyncSerialization.Init"))
            SyncSerialization.Init();

        using (DeepProfilerWrapper.Section("Multiplayer SyncGame"))
            SyncGame.Init();

        using (DeepProfilerWrapper.Section("Multiplayer Sync register attributes"))
            Sync.RegisterAllAttributes(typeof(Multiplayer).Assembly);

        using (DeepProfilerWrapper.Section("Multiplayer Sync validation"))
            Sync.ValidateAll();
    }

    internal static void LatePatches(Harmony harmony)
    {
        // optimization, cache DescendantThingDefs
        // harmony.PatchMeasure(
        //     AccessTools.Method(typeof(ThingCategoryDef), "get_DescendantThingDefs"),
        //     new HarmonyMethod(typeof(ThingCategoryDef_DescendantThingDefsPatch), "Prefix"),
        //     new HarmonyMethod(typeof(ThingCategoryDef_DescendantThingDefsPatch), "Postfix")
        // );

        // optimization, cache ThisAndChildCategoryDefs
        // harmony.PatchMeasure(
        //     AccessTools.Method(typeof(ThingCategoryDef), "get_ThisAndChildCategoryDefs"),
        //     new HarmonyMethod(typeof(ThingCategoryDef_ThisAndChildCategoryDefsPatch), "Prefix"),
        //     new HarmonyMethod(typeof(ThingCategoryDef_ThisAndChildCategoryDefsPatch), "Postfix")
        // );

        if (MpVersion.IsDebug)
        {
            Log.Message("== Structure == \n" + SyncDict.syncWorkers.PrintStructure());
        }
    }
}
