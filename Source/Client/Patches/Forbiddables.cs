using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    // todo handle conversion to singleplayer and PostSplitOff

    [HarmonyPatch(typeof(CompForbiddable), nameof(CompForbiddable.Forbidden), MethodType.Getter)]
    static class GetForbidPatch
    {
        static void Postfix(Thing ___parent, ref bool __result)
        {
            if (Multiplayer.Client == null) return;

            if (___parent.Spawned)
                __result = !___parent.Map.MpComp().GetCurrentCustomFactionData().unforbidden.Contains(___parent);
            else
                __result = false; // Keeping track of unspawned things is more difficult, just say it's not forbidden
        }
    }

    [HarmonyPatch(typeof(CompForbiddable), nameof(CompForbiddable.Forbidden), MethodType.Setter)]
    static class SetForbidPatch
    {
        static void Prefix(CompForbiddable __instance, Thing ___parent, bool value)
        {
            if (Multiplayer.Client == null) return;
            if (Multiplayer.InInterface) return; // Will get synced

            bool changed = false;

            if (Multiplayer.Client != null && ___parent.Spawned)
            {
                var set = ___parent.Map.MpComp().GetCurrentCustomFactionData().unforbidden;
                changed = value ? set.Remove(___parent) : set.Add(___parent);
            }

            // After the prefix the method early returns if (value == forbiddenInt)
            // Setting forbiddenInt to !value forces an update (prevents the early return)
            __instance.forbiddenInt = changed ? !value : value;
        }
    }

    [HarmonyPatch(typeof(CompForbiddable), nameof(CompForbiddable.UpdateOverlayHandle))]
    static class ForbiddablePostDrawPatch
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(CompForbiddable __instance, ref bool __state)
        {
            FactionContext.Push(Multiplayer.RealPlayerFaction);
            __state = __instance.forbiddenInt;
            __instance.forbiddenInt = __instance.Forbidden;
        }

        [HarmonyPriority(MpPriority.MpLast)]
        static void Postfix(CompForbiddable __instance, bool __state)
        {
            __instance.forbiddenInt = __state;
            FactionContext.Pop();
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
    static class ThingSpawnSetForbidden
    {
        static void Prefix(Thing __instance, Map map, bool respawningAfterLoad)
        {
            if (respawningAfterLoad) return;
            if (Multiplayer.Client == null) return;

            if (ThingContext.stack.Any(p => p.Item1?.def == ThingDefOf.ActiveDropPod)) return;

            if (__instance is ThingWithComps t && t.GetComp<CompForbiddable>() != null)
                map.MpComp().GetCurrentCustomFactionData().unforbidden.Add(__instance);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    static class ThingDespawnUnsetForbidden
    {
        static void Prefix(Thing __instance)
        {
            if (Multiplayer.Client == null) return;
            __instance.Map.MpComp().Notify_ThingDespawned(__instance);
        }
    }
}
