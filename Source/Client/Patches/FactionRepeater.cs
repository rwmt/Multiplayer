using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    static class FactionRepeater
    {
        public static bool Template(Action<FactionMapData> method, Map map, ref bool ignore)
        {
            if (Multiplayer.Client == null || ignore) return true;

            ignore = true;
            foreach (var data in map.MpComp().factionData.Values)
            {
                map.PushFaction(data.factionId);
                method(data);
                map.PopFaction();
            }
            ignore = false;

            return false;
        }
    }

    [HarmonyPatch(typeof(ListerFilthInHomeArea), nameof(ListerFilthInHomeArea.RebuildAll))]
    static class ListerFilthRebuildPatch
    {
        static bool ignore;

        static bool Prefix(ListerFilthInHomeArea __instance) =>
            FactionRepeater.Template(d => d.listerFilthInHomeArea.RebuildAll(), __instance.map, ref ignore);
    }

    [HarmonyPatch(typeof(ListerFilthInHomeArea), nameof(ListerFilthInHomeArea.Notify_FilthSpawned))]
    static class ListerFilthSpawnedPatch
    {
        static bool ignore;

        static bool Prefix(ListerFilthInHomeArea __instance, Filth f) =>
            FactionRepeater.Template(d => d.listerFilthInHomeArea.Notify_FilthSpawned(f), __instance.map, ref ignore);
    }

    // todo look at slot group in ListerHaulables

    [HarmonyPatch(typeof(ListerHaulables), nameof(ListerHaulables.Notify_Spawned))]
    static class ListerHaulablesSpawnedPatch
    {
        static bool ignore;

        static bool Prefix(ListerHaulables __instance, Thing t) =>
            FactionRepeater.Template(d => d.listerHaulables.Notify_Spawned(t), __instance.map, ref ignore);
    }

    [HarmonyPatch(typeof(ListerHaulables), nameof(ListerHaulables.Notify_DeSpawned))]
    static class ListerHaulablesDespawnedPatch
    {
        static bool ignore;

        static bool Prefix(ListerHaulables __instance, Thing t) =>
            FactionRepeater.Template(d => d.listerHaulables.Notify_DeSpawned(t), __instance.map, ref ignore);
    }

    [HarmonyPatch(typeof(ListerMergeables), nameof(ListerMergeables.Notify_Spawned))]
    static class ListerMergeablesSpawnedPatch
    {
        static bool ignore;

        static bool Prefix(ListerMergeables __instance, Thing t) =>
            FactionRepeater.Template(d => d.listerMergeables.Notify_DeSpawned(t), __instance.map, ref ignore);
    }

    [HarmonyPatch(typeof(ListerMergeables), nameof(ListerMergeables.Notify_DeSpawned))]
    static class ListerMergeablesDespawnedPatch
    {
        static bool ignore;

        static bool Prefix(ListerMergeables __instance, Thing t) =>
            FactionRepeater.Template(d => d.listerMergeables.Notify_DeSpawned(t), __instance.map, ref ignore);
    }

    [HarmonyPatch(typeof(ListerMergeables), nameof(ListerMergeables.Notify_ThingStackChanged))]
    static class ListerMergeablesStackChangedPatch
    {
        static bool ignore;

        static bool Prefix(ListerMergeables __instance, Thing t) =>
            FactionRepeater.Template(d => d.listerMergeables.Notify_ThingStackChanged(t), __instance.map, ref ignore);
    }

}
