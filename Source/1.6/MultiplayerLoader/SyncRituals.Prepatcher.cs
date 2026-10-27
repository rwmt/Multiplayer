using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mono.Cecil;
using Multiplayer.API;
using RimWorld;

namespace MultiplayerLoader;

public static partial class SyncRituals
{
    private static List<ISyncMethodForPrepatcher> syncMethodsWithPrepatches = new();

    public static bool PrepatcherPrefix(int index, object __instance, object[] __args)
    {
        var sync = syncMethodsWithPrepatches[index];
        return sync.TargetType != __instance.GetType() || !sync.DoSync(__instance, __args);
    }

    public static void Init() => ApplyPrepatches(null);

    public static void ApplyPrepatches(ModuleDefinition module)
    {
        void Register(string method, Type widgetType, Action<ISyncMethod> postProcess)
        {
            if (module != null)
            {
                Prepatches.AddPrepatcherPrefix(
                    module,
                    syncMethodsWithPrepatches.Count,
                    AccessTools.Method(typeof(PawnRoleSelectionWidgetBase<>), method),
                    AccessTools.Method(typeof(SyncRituals), nameof(PrepatcherPrefix))
                );
                syncMethodsWithPrepatches.Add(null);
                return;
            }

            var sync = (ISyncMethodForPrepatcher)syncMethod.Invoke(null, [widgetType, method, null]);
            postProcess?.Invoke(sync);
            syncMethodsWithPrepatches.Add(sync);
        }

        RegisterRitualSyncMethods(Register);
    }
}
