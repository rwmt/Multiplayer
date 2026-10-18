using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.Client.Factions;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    static class FactionRepeater
    {
        public static bool Template<T>(IDictionary<int, T> factionIdToData, Action<T> dataProcessor, Map map, ref bool ignore)
        {
            if (Multiplayer.Client == null || ignore) return true;

            var spectatorId = Multiplayer.WorldComp.spectatorFaction.loadID;
            ignore = true;
            foreach (var (id, data) in factionIdToData)
            {
                if (id == spectatorId)
                    continue;

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

    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.Notify_MonolithLevelChanged))]
    static class MonolithLevelChangedPatch
    {
        static bool ignore;

        static bool Prefix(int newLevel) =>
            FactionRepeater.Template(
                Multiplayer.game?.worldComp.factionData,
                d => d.researchManager.Notify_MonolithLevelChanged(newLevel),
                null,
                ref ignore
            );
    }

    // A building that can't overlap zones clears zone cells only from the installed (context faction's) zone
    // manager, other factions' zones kept cells under it. Repeat it for them when any of them has a zone there.
    [HarmonyPatch(typeof(ZoneManager), nameof(ZoneManager.Notify_NoZoneOverlapThingSpawned))]
    static class ZoneOverlapSpawnedPatch
    {
        static bool ignore;

        static void Prefix(ZoneManager __instance, Thing thing)
        {
            if (Multiplayer.Client == null || ignore) return;

            var factionData = __instance.map.MpComp()?.factionData;
            if (factionData == null || !AnyOtherFactionZoneUnder(factionData, __instance, thing)) return;

            // The original then runs on the installed manager again, a no-op once its cells are cleared
            FactionRepeater.Template(
                factionData,
                d => d.zoneManager.Notify_NoZoneOverlapThingSpawned(thing),
                __instance.map,
                ref ignore
            );
        }

        static bool AnyOtherFactionZoneUnder(IDictionary<int, FactionMapData> factionData, ZoneManager installed, Thing thing)
        {
            var rect = thing.OccupiedRect();
            foreach (var data in factionData.Values)
            {
                if (data.zoneManager == installed) continue;
                foreach (var c in rect)
                    if (c.InBounds(installed.map) && data.zoneManager.ZoneAt(c) != null)
                        return true;
            }

            return false;
        }
    }

    // Destroying a thing removes designations on it only from each map's installed designation manager, so other
    // factions' managers kept designations on a destroyed thing (they're dropped on load, so a reloading client
    // differed from the others). Designations on a thing live on its own map.
    [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
    static class ThingDestroyDesignationsPatch
    {
        static void Prefix(Thing __instance, ref Map __state)
        {
            if (Multiplayer.Client != null && __instance.def.category != ThingCategory.Mote)
                __state = __instance.MapHeld;
        }

        static void Postfix(Thing __instance, Map __state)
        {
            if (__state == null || !__instance.Destroyed) return;

            var factionData = __state.MpComp()?.factionData;
            if (factionData == null || !factionData.Values.Any(d =>
                    d.designationManager != __state.designationManager &&
                    d.designationManager.AllDesignationsOn(__instance).Count > 0))
                return;

            var ignore = false;
            FactionRepeater.Template(
                factionData,
                d => d.designationManager.RemoveAllDesignationsOn(__instance),
                __state,
                ref ignore
            );
        }
    }

    // Fix for RimWorld 1.6 bug where HistoryAutoRecorder.Tick() calls .Last() on empty collection
    [HarmonyPatch(typeof(HistoryAutoRecorder), nameof(HistoryAutoRecorder.Tick))]
    static class HistoryAutoRecorderTickPatch
    {
        // Transpiler to fix the .Last() call on potentially empty collection
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var safeLastMethod = AccessTools.Method(typeof(HistoryAutoRecorderTickPatch), nameof(SafeLast));

            var codes = new List<CodeInstruction>(instructions);


            for (int i = 0; i < codes.Count; i++)
            {
                var instruction = codes[i];

                if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo methodInfo)
                {
                    // Check if this is the generic Enumerable.Last<T>(...) method.
                    if (methodInfo.Name == nameof(Enumerable.Last) &&
                        methodInfo.DeclaringType == typeof(Enumerable) &&
                        methodInfo.IsGenericMethod)
                    {
                        // Found it, replace the operand with our safe method.
                        instruction.operand = safeLastMethod;
                        break;
                    }
                }
            }

            return codes;
        }

        // Safe version of .Last() that returns 0f for empty collections
        static float SafeLast(IEnumerable<float> source)
        {
            return source.Any() ? source.Last() : 0f;
        }
    }

}
