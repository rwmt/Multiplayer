using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mono.Cecil;
using Multiplayer.API;
using Multiplayer.Client.Util;
using RimWorld;

namespace MultiplayerLoader;

public static class SyncRituals
{
    private static List<ISyncMethodForPrepatcher> syncMethodsWithPrepatches = new();

    public static bool PrepatcherPrefix(int index, object __instance, object[] __args)
    {
        // This prefix is applied to a generic method and called by all possible instantiations
        // This check makes sure the type is correct
        if (syncMethodsWithPrepatches[index].TargetType == __instance.GetType())
            return !syncMethodsWithPrepatches[index].DoSync(__instance, __args);
        return true;
    }

    private static MethodInfo syncMethod = AccessTools.Method("Multiplayer.Client.Sync:Method");

    // This method is called twice (two passes)
    // The first pass with module != null is called from Prepatcher and applies prepatches
    // The second pass with module == null is called from Sync initialization and registers SyncMethods
    public static void ApplyPrepatches(ModuleDefinition module)
    {
        void Register(Type baseType, string method, Type derivedType, Action<ISyncMethodForPrepatcher> postProcess = null)
        {
            if (module != null)
            {
                Prepatches.AddPrepatcherPrefix(
                    module,
                    syncMethodsWithPrepatches.Count,
                    AccessTools.Method(baseType, method),
                    AccessTools.Method(typeof(SyncRituals), nameof(PrepatcherPrefix))
                );

                // The ids need to be in sync between passes (note the array is recreated after Prepatcher restarts the game)
                syncMethodsWithPrepatches.Add(null);
            }
            else
            {
                // Sync.Method doesn't apply a Harmony patch and doesn't require the DeclaringType to be the target type
                var syncMethod = (ISyncMethodForPrepatcher)SyncRituals.syncMethod.Invoke(null, [derivedType, method, null]);
                postProcess?.Invoke(syncMethod);
                syncMethodsWithPrepatches.Add(syncMethod);
            }
        }

        // ======= PawnRitualRoleSelectionWidget =======

        var ritualRolesSerializer = Serializer.New(
            (IEnumerable<RitualRole> roles, object target, object[] _) =>
            {
                var roleSelectionWidget = (PawnRitualRoleSelectionWidget)target;
                return (roleSelectionWidget, roleSelectionWidget.assignments.RoleGroups().FirstOrDefault(g => g.SequenceEqual(roles))?.Key);
            },
            data => data.roleSelectionWidget.assignments.RoleGroups().FirstOrDefault(g => g.Key == data.Key)
        );

        Register(typeof(PawnRoleSelectionWidgetBase<>), nameof(PawnRoleSelectionWidgetBase<RitualRole>.TryAssignReplace),
            typeof(PawnRitualRoleSelectionWidget),
            m => m.TransformArgument(1, ritualRolesSerializer));

        Register(typeof(PawnRoleSelectionWidgetBase<>), nameof(PawnRoleSelectionWidgetBase<RitualRole>.TryAssignAnyRole),
            typeof(PawnRitualRoleSelectionWidget));

        Register(typeof(PawnRoleSelectionWidgetBase<>), nameof(PawnRoleSelectionWidgetBase<RitualRole>.TryAssign),
            typeof(PawnRitualRoleSelectionWidget),
            m => m.TransformArgument(1, ritualRolesSerializer));

        Register(
            typeof(PawnRoleSelectionWidgetBase<>),
            MpMethodUtil.GetLambda(
                typeof(PawnRoleSelectionWidgetBase<ILordJobRole>),
                nameof(PawnRoleSelectionWidgetBase<RitualRole>.DrawPawnListInternal),
                lambdaOrdinal: 7).Name,
            typeof(PawnRitualRoleSelectionWidget)
        ); // Roles right click delegate (try assign spectate)

        Register(
            typeof(PawnRoleSelectionWidgetBase<>),
            MpMethodUtil.GetLambda(
                typeof(PawnRoleSelectionWidgetBase<ILordJobRole>),
                nameof(PawnRoleSelectionWidgetBase<RitualRole>.DrawPawnListInternal),
                lambdaOrdinal: 2).Name,
            typeof(PawnRitualRoleSelectionWidget)
        ); // Not participating left click delegate (try assign any role or spectate)
    }
}
