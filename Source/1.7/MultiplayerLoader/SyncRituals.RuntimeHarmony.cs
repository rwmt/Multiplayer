using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;

namespace MultiplayerLoader;

public static partial class SyncRituals
{
    private static readonly Dictionary<(Type, string), ISyncMethod> runtimeSyncMethods = new();
    private static readonly HashSet<MethodBase> runtimePatchedMethods = new();
    private static readonly Harmony runtimeHarmony = new("rwmt.Multiplayer.Rituals");

    public static bool RuntimePrefix(object __instance, object[] __args, MethodBase __originalMethod)
    {
        return !runtimeSyncMethods.TryGetValue((__instance.GetType(), __originalMethod.Name), out var sync)
            || !sync.DoSync(__instance, __args);
    }

    public static void Init()
    {
        void Register(string method, Type widgetType, Action<ISyncMethod> postProcess)
        {
            // Keep the concrete widget type for serializers and dispatch on shared generic bodies.
            var sync = (ISyncMethod)syncMethod.Invoke(null, [widgetType, method, null]);
            postProcess?.Invoke(sync);
            runtimeSyncMethods.Add((widgetType, method), sync);

            // The closed generic method is the runtime target for each concrete widget.
            var target = AccessTools.Method(widgetType, method);
            if (runtimePatchedMethods.Add(target))
                runtimeHarmony.Patch(target, prefix: new HarmonyMethod(typeof(SyncRituals), nameof(RuntimePrefix)));
        }

        RegisterRitualSyncMethods(Register);
    }
}
