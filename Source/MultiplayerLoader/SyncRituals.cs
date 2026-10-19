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
    // Indexed like syncMethodsWithPrepatches
    private static List<Action<object>> beforeExecutingWithPrepatches = new();

    public static bool PrepatcherPrefix(int index, object __instance, object[] __args)
    {
        // This prefix is applied to a generic method and called by all possible instantiations
        // This check makes sure the type is correct
        if (syncMethodsWithPrepatches[index].TargetType == __instance.GetType())
        {
            if (syncMethodsWithPrepatches[index].DoSync(__instance, __args))
                return false;

            // Also runs for calls nested in another synced method
            if (MP.IsExecutingSyncCommand)
                beforeExecutingWithPrepatches[index]?.Invoke(__instance);
        }
        return true;
    }

    private static MethodInfo syncMethod = AccessTools.Method("Multiplayer.Client.Sync:Method");

    /// <summary>
    /// Whether the methods below run the sync prefix, i.e. Prepatcher ran the first pass. Prepatcher is a mod
    /// dependency but can be missing or fail to load (e.g. on macOS). Then the role assignments of ritual dialogs
    /// stay local. Set by the second pass from the patched code itself: a flag set by the first pass is lost because
    /// Prepatcher restarts the game with the patched assemblies.
    /// </summary>
    public static bool PrepatchesApplied { get; private set; }

    private static bool HasPrepatcherPrefix(MethodBase method)
    {
        try
        {
            // By name: the patched assembly refers to this method through its own assembly reference
            return PatchProcessor.ReadMethodBody(method).Any(inst =>
                inst.Value is MethodInfo m &&
                m.Name == nameof(PrepatcherPrefix) &&
                m.DeclaringType?.FullName == typeof(SyncRituals).FullName);
        }
        catch (Exception e)
        {
            Verse.Log.Warning($"Multiplayer: couldn't check whether Prepatcher patched {method}: {e}");
            return false;
        }
    }

    // This method is called twice (two passes)
    // The first pass with module != null is called from Prepatcher and applies prepatches
    // The second pass with module == null is called from Sync initialization and registers SyncMethods
    public static void ApplyPrepatches(ModuleDefinition module)
    {
        if (module == null)
            PrepatchesApplied = HasPrepatcherPrefix(AccessTools.Method(
                typeof(PawnRoleSelectionWidgetBase<RitualRole>),
                nameof(PawnRoleSelectionWidgetBase<RitualRole>.TryAssignReplace)));

        void Register(Type baseType, string method, Type derivedType, Action<ISyncMethodForPrepatcher> postProcess = null,
            Action<object> beforeExecuting = null)
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
            }

            beforeExecutingWithPrepatches.Add(beforeExecuting);
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
            typeof(PawnRitualRoleSelectionWidget),
            beforeExecuting: target => RefreshRoleGroups((PawnRitualRoleSelectionWidget)target));
        Register(typeof(PawnRoleSelectionWidgetBase<>), nameof(PawnRoleSelectionWidgetBase<PsychicRitualRoleDef>.TryAssignAnyRole),
            typeof(PawnPsychicRitualRoleSelectionWidget),
            beforeExecuting: target => RefreshRoleGroups((PawnPsychicRitualRoleSelectionWidget)target));

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

    // TryAssignAnyRole iterates this static cache, which only DrawPawnListInternal fills: empty on a client that
    // doesn't draw the dialog, or left over from another ritual. Its callers (click handlers) don't run inside a
    // loop over the cache and nothing it calls refills it, so refilling it here is safe.
    private static void RefreshRoleGroups<RoleType>(PawnRoleSelectionWidgetBase<RoleType> widget)
        where RoleType : class, ILordJobRole
    {
        var groups = PawnRoleSelectionWidgetBase<RoleType>.rolesGroupedTmp;
        groups.Clear();
        groups.AddRange(widget.assignments.RoleGroups());
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
