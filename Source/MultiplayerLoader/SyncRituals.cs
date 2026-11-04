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
#if RIMWORLD_1_7
    private static readonly Dictionary<(Type, string), ISyncMethodForPrepatcher> runtimeSyncMethods = new();
    private static readonly HashSet<MethodBase> runtimePatchedMethods = new();
    private static readonly Harmony runtimeHarmony = new("rwmt.Multiplayer.WaylithRituals");

    public static bool RuntimePrefix(object __instance, object[] __args, MethodBase __originalMethod)
    {
        return !runtimeSyncMethods.TryGetValue((__instance.GetType(), __originalMethod.Name), out var sync)
            || !sync.DoSync(__instance, __args);
    }
#endif

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
                var newSyncMethod = (ISyncMethodForPrepatcher)syncMethod.Invoke(null, [derivedType, method, null]);
                postProcess?.Invoke(newSyncMethod);
                syncMethodsWithPrepatches.Add(newSyncMethod);
#if RIMWORLD_1_7
                // Waylith supports runtime patching; the Mono preloader is not used.
                runtimeSyncMethods.Add((derivedType, method), newSyncMethod);
                var target = AccessTools.Method(derivedType, method);
                if (runtimePatchedMethods.Add(target))
                    runtimeHarmony.Patch(target, prefix: new HarmonyMethod(typeof(SyncRituals), nameof(RuntimePrefix)));
#endif
            }
        }

        // ======= PawnRitualRoleSelectionWidget =======

        var ritualRolesSerializer = Serializer.New<IEnumerable<RitualRole>, (PawnRitualRoleSelectionWidget roleSelectionWidget, string Key)>(
            RitualRoleWriter<PawnRitualRoleSelectionWidget, RitualRole>,
            RitualRoleReader<PawnRitualRoleSelectionWidget, RitualRole>
        );
        var psychicRitualRolesSerializer = Serializer.New<IEnumerable<PsychicRitualRoleDef>, (PawnPsychicRitualRoleSelectionWidget roleSelectionWidget, string Key)>(
            RitualRoleWriter<PawnPsychicRitualRoleSelectionWidget, PsychicRitualRoleDef>,
            RitualRoleReader<PawnPsychicRitualRoleSelectionWidget, PsychicRitualRoleDef>
        );

        Register(typeof(PawnRoleSelectionWidgetBase<>), nameof(PawnRoleSelectionWidgetBase<RitualRole>.TryAssignReplace),
            typeof(PawnRitualRoleSelectionWidget),
            m => m.TransformArgument(1, ritualRolesSerializer));
        Register(typeof(PawnRoleSelectionWidgetBase<>), nameof(PawnRoleSelectionWidgetBase<PsychicRitualRoleDef>.TryAssignReplace),
            typeof(PawnPsychicRitualRoleSelectionWidget),
            m => m.TransformArgument(1, psychicRitualRolesSerializer));

        Register(typeof(PawnRoleSelectionWidgetBase<>), nameof(PawnRoleSelectionWidgetBase<RitualRole>.TryAssignAnyRole),
            typeof(PawnRitualRoleSelectionWidget));
        Register(typeof(PawnRoleSelectionWidgetBase<>), nameof(PawnRoleSelectionWidgetBase<PsychicRitualRoleDef>.TryAssignAnyRole),
            typeof(PawnPsychicRitualRoleSelectionWidget));

        Register(typeof(PawnRoleSelectionWidgetBase<>), nameof(PawnRoleSelectionWidgetBase<RitualRole>.TryAssign),
            typeof(PawnRitualRoleSelectionWidget),
            m => m.TransformArgument(1, ritualRolesSerializer));
        Register(typeof(PawnRoleSelectionWidgetBase<>), nameof(PawnRoleSelectionWidgetBase<PsychicRitualRoleDef>.TryAssign),
            typeof(PawnPsychicRitualRoleSelectionWidget),
            m => m.TransformArgument(1, psychicRitualRolesSerializer));

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
                nameof(PawnRoleSelectionWidgetBase<PsychicRitualRoleDef>.DrawPawnListInternal),
                lambdaOrdinal: 7).Name,
            typeof(PawnPsychicRitualRoleSelectionWidget)
        ); // Roles right click delegate (try assign spectate)

        Register(
            typeof(PawnRoleSelectionWidgetBase<>),
            MpMethodUtil.GetLambda(
                typeof(PawnRoleSelectionWidgetBase<ILordJobRole>),
                nameof(PawnRoleSelectionWidgetBase<RitualRole>.DrawPawnListInternal),
                lambdaOrdinal: 2).Name,
            typeof(PawnRitualRoleSelectionWidget)
        ); // Not participating left click delegate (try assign any role or spectate)
        Register(
            typeof(PawnRoleSelectionWidgetBase<>),
            MpMethodUtil.GetLambda(
                typeof(PawnRoleSelectionWidgetBase<ILordJobRole>),
                nameof(PawnRoleSelectionWidgetBase<PsychicRitualRoleDef>.DrawPawnListInternal),
                lambdaOrdinal: 2).Name,
            typeof(PawnPsychicRitualRoleSelectionWidget)
        ); // Not participating left click delegate (try assign any role or spectate)
    }

    private static (WidgetType roleSelectionWidget, string Key) RitualRoleWriter<WidgetType, RoleType>(IEnumerable<RoleType> roles, object target, object[] _)
        where WidgetType : PawnRoleSelectionWidgetBase<RoleType>
        where RoleType : class, ILordJobRole
    {
        var roleSelectionWidget = (WidgetType)target;
        return (roleSelectionWidget, roleSelectionWidget.assignments.RoleGroups().FirstOrDefault(g => g.SequenceEqual(roles))?.Key);
    }

    private static IEnumerable<RoleType> RitualRoleReader<WidgetType, RoleType>((WidgetType roleSelectionWidget, string Key) data)
        where WidgetType : PawnRoleSelectionWidgetBase<RoleType>
        where RoleType : class, ILordJobRole
    {
        return data.roleSelectionWidget.assignments.RoleGroups().FirstOrDefault(g => g.Key == data.Key);
    }
}
