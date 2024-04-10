using System;
using System.Collections.Generic;
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
            try {
                if (type.IsDefined(typeof(EarlyPatchAttribute)))
                    harmony.CreateClassProcessor(type).Patch();
            } catch (Exception e) {
                Log.Error($"FAIL: {type} with {e}");
                Multiplayer.loadingErrors = true;
            }
        });

#if DEBUG
        DebugPatches.Init();
#endif
    }

    internal static void InitSync()
    {
        using (DeepProfilerWrapper.Section("Multiplayer RwSerialization.Init"))
            RwSerialization.Init();

        SyncDict.Init();

        using (DeepProfilerWrapper.Section("Multiplayer SyncGame.Init"))
            SyncGame.Init();

        using (DeepProfilerWrapper.Section("Multiplayer Sync register attributes"))
            Sync.RegisterAllAttributes(typeof(Multiplayer).Assembly);

        using (DeepProfilerWrapper.Section("Multiplayer Sync validation"))
            Sync.ValidateAll();
    }

    internal static void LatePatches()
    {
        if (MpVersion.IsDebug)
            Log.Message("== Structure == \n" + SyncDict.syncWorkers.PrintStructure());
    }
}
