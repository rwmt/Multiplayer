using System;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.Client.Factions;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    static class FactionRepeater
    {
        public static bool Template<T>(Dictionary<int, T> factionIdToData, Action<T> dataProcessor, Map map, ref bool ignore)
        {
            if (Multiplayer.Client == null || ignore) return true;

            ignore = true;
            foreach (var (id, data) in factionIdToData)
            {
                map.PushFaction(id);
                try
                {
                    dataProcessor(data);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception in FactionRepeater for faction {id} {Faction.OfPlayer}: {e}");
                }
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
            FactionRepeater.Template(
                __instance.map.MpComp()?.factionData, // The template doesn't run if not in MP
                d => d.listerFilthInHomeArea.RebuildAll(),
                __instance.map,
                ref ignore
            );
    }

    [HarmonyPatch(typeof(ListerFilthInHomeArea), nameof(ListerFilthInHomeArea.Notify_FilthSpawned))]
    static class ListerFilthSpawnedPatch
    {
        static bool ignore;

        static bool Prefix(ListerFilthInHomeArea __instance, Filth f) =>
            FactionRepeater.Template(
                __instance.map.MpComp()?.factionData,
                d => d.listerFilthInHomeArea.Notify_FilthSpawned(f),
                __instance.map,
                ref ignore
            );
    }

    // todo look at slot group in ListerHaulables

    [HarmonyPatch(typeof(ListerHaulables), nameof(ListerHaulables.Notify_Spawned))]
    static class ListerHaulablesSpawnedPatch
    {
        static bool ignore;

        static bool Prefix(ListerHaulables __instance, Thing t) =>
            FactionRepeater.Template(
                __instance.map.MpComp()?.factionData,
                d => d.listerHaulables.Notify_Spawned(t),
                __instance.map,
                ref ignore
            );
    }

    [HarmonyPatch(typeof(ListerHaulables), nameof(ListerHaulables.Notify_DeSpawned))]
    static class ListerHaulablesDespawnedPatch
    {
        static bool ignore;

        static bool Prefix(ListerHaulables __instance, Thing t) =>
            FactionRepeater.Template(
                __instance.map.MpComp()?.factionData,
                d => d.listerHaulables.Notify_DeSpawned(t),
                __instance.map,
                ref ignore
            );
    }

    [HarmonyPatch(typeof(ListerMergeables), nameof(ListerMergeables.Notify_Spawned))]
    static class ListerMergeablesSpawnedPatch
    {
        static bool ignore;

        static bool Prefix(ListerMergeables __instance, Thing t) =>
            FactionRepeater.Template(
                __instance.map.MpComp()?.factionData,
                d => d.listerMergeables.Notify_Spawned(t),
                __instance.map,
                ref ignore
            );
    }

    [HarmonyPatch(typeof(ListerMergeables), nameof(ListerMergeables.Notify_DeSpawned))]
    static class ListerMergeablesDespawnedPatch
    {
        static bool ignore;

        static bool Prefix(ListerMergeables __instance, Thing t) =>
            FactionRepeater.Template(
                __instance.map.MpComp()?.factionData,
                d => d.listerMergeables.Notify_DeSpawned(t),
                __instance.map,
                ref ignore
            );
    }

    [HarmonyPatch(typeof(ListerMergeables), nameof(ListerMergeables.Notify_ThingStackChanged))]
    static class ListerMergeablesStackChangedPatch
    {
        static bool ignore;

        static bool Prefix(ListerMergeables __instance, Thing t) =>
            FactionRepeater.Template(
                __instance.map.MpComp()?.factionData,
                d => d.listerMergeables.Notify_ThingStackChanged(t),
                __instance.map,
                ref ignore
            );
    }

    [HarmonyPatch(typeof(History), nameof(History.HistoryTick))]
    static class HistoryTickPatch
    {
        static bool ignore;

        static bool Prefix() =>
            FactionRepeater.Template(
                Multiplayer.game?.worldComp.factionData, // The template doesn't run if not in MP
                d => d.history.HistoryTick(),
                null,
                ref ignore
            );
    }

    [HarmonyPatch(typeof(Storyteller), nameof(Storyteller.StorytellerTick))]
    static class StorytellerTickPatch
    {
        static bool ignore;

        static bool Prefix() =>
            FactionRepeater.Template(
                Multiplayer.game?.worldComp.factionData,
                d => d.storyteller.StorytellerTick(),
                null,
                ref ignore
            );
    }

    [HarmonyPatch(typeof(StoryWatcher), nameof(StoryWatcher.StoryWatcherTick))]
    static class StoryWatcherTickPatch
    {
        static bool ignore;

        static bool Prefix() =>
            FactionRepeater.Template(
                Multiplayer.game?.worldComp.factionData,
                d => d.storyWatcher.StoryWatcherTick(),
                null,
                ref ignore
            );
    }

}
