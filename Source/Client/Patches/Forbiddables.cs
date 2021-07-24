using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
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
            if (Multiplayer.ShouldSync) return; // Will get synced

            bool changed = false;

            if (Multiplayer.Client != null && ___parent.Spawned)
            {
                var set = ___parent.Map.MpComp().GetCurrentCustomFactionData().unforbidden;
                changed = value ? set.Remove(___parent) : set.Add(___parent);
            }

            __instance.forbiddenInt = changed ? !value : value; // Force an update
        }
    }

    // todo 1.3: not needed?
    /*[HarmonyPatch(typeof(CompForbiddable), nameof(CompForbiddable.PostDraw))]
    static class ForbiddablePostDrawPatch
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(CompForbiddable __instance, ref bool __state)
        {
            __state = __instance.forbiddenInt;
            __instance.forbiddenInt = __instance.Forbidden;
        }

        [HarmonyPriority(MpPriority.MpLast)]
        static void Postfix(CompForbiddable __instance, ref bool __state)
        {
            __instance.forbiddenInt = __state;
        }
    }*/

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
