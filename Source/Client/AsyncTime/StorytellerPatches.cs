using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.AsyncTime;

[HarmonyPatch]
public class StorytellerTickPatch
{
    public static bool updating;

    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Storyteller), nameof(Storyteller.StorytellerTick));
        yield return AccessTools.Method(typeof(StoryWatcher), nameof(StoryWatcher.StoryWatcherTick));
    }

    static bool Prefix()
    {
        updating = true;
        return Multiplayer.Client == null || Multiplayer.Ticking;
    }

    static void Finalizer() => updating = false;
}

[HarmonyPatch(typeof(Storyteller))]
[HarmonyPatch(nameof(Storyteller.AllIncidentTargets), MethodType.Getter)]
public class StorytellerTargetsPatch
{
    static void Postfix(List<IIncidentTarget> __result)
    {
        if (Multiplayer.Client == null) return;

        if (Multiplayer.MapContext != null)
        {
            __result.Clear();
            __result.Add(Multiplayer.MapContext);
        }
        else if (AsyncWorldTimeComp.tickingWorld)
        {
            __result.Clear();

            foreach (var caravan in Find.WorldObjects.Caravans)
                if (caravan.IsPlayerControlled)
                    __result.Add(caravan);

            __result.Add(Find.World);
        }
    }
}

// The MP Mod's ticker calls Storyteller.StorytellerTick() on both the World and each Map, each tick
// This patch aims to ensure each "spawn raid" Quest is only triggered once, to prevent 2x or 3x sized raids
[HarmonyPatch(typeof(Quest), nameof(Quest.PartsListForReading), MethodType.Getter)]
public class QuestPartsListForReadingPatch
{
    static void Postfix(ref List<QuestPart> __result)
    {
        if (StorytellerTickPatch.updating)
        {
            __result = __result.Where(questPart => {
                if (questPart is QuestPart_ThreatsGenerator questPartThreatsGenerator)
                {
                    return questPartThreatsGenerator.mapParent?.Map == Multiplayer.MapContext;
                }
                return true;
            }).ToList();
        }
    }
}

[HarmonyPatch(typeof(StorytellerUtility), nameof(StorytellerUtility.DefaultParmsNow))]
static class MapContextIncidentParms
{
    static void Prefix(IIncidentTarget target, ref Map __state)
    {
        // This may be running inside a context already
        if (AsyncTimeComp.tickingMap != null)
            return;

        if (AsyncWorldTimeComp.tickingWorld && target is Map map)
        {
            AsyncTimeComp.tickingMap = map;
            map.AsyncTime().PreContext();
            __state = map;
        }
    }

    static void Finalizer(Map __state)
    {
        if (__state != null)
        {
            __state.AsyncTime().PostContext();
            AsyncTimeComp.tickingMap = null;
        }
    }
}

[HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
static class MapContextIncidentExecute
{
    static void Prefix(IncidentParms parms, ref Map __state)
    {
        if (AsyncWorldTimeComp.tickingWorld && parms.target is Map map)
        {
            AsyncTimeComp.tickingMap = map;
            map.AsyncTime().PreContext();
            __state = map;
        }
    }

    static void Finalizer(Map __state)
    {
        if (__state != null)
        {
            __state.AsyncTime().PostContext();
            AsyncTimeComp.tickingMap = null;
        }
    }
}

[HarmonyPatch(typeof(Settlement), nameof(Settlement.IncidentTargetTags))]
static class SettlementIncidentTargetTagsPatch
{
    static IEnumerable<IncidentTargetTagDef> Postfix(IEnumerable<IncidentTargetTagDef> tags, Settlement __instance)
    {
        foreach (var tag in tags)
        {
            // Only return Map_Misc if player's faction is (heuristically) visiting the map
            // This affects multifaction where the storyteller ticks on every settlement for every faction separately
            if (tag != IncidentTargetTagDefOf.Map_Misc ||
                Find.AnyPlayerHomeMap != null && __instance.Map is { } m && m.mapPawns.AnyColonistSpawned)
                yield return tag;
        }
    }
}

[HarmonyPatch(typeof(StorytellerUtility), nameof(StorytellerUtility.DefaultThreatPointsNow))]
static class FixMultifactionPocketMapDefaultThreatPointsNow
{
    static bool Prefix(IIncidentTarget target)
    {
        // StorytellerUtility.DefaultThreatPointsNow uses Find.AnyPlayerHomeMap if
        // the target map is a pocket map. In such a situation, we stop the method
        // from executing if Find.AnyPlayerHomeMap would return null, as otherwise
        // we'll end up with a very frequent error spam from spectator faction and
        // any other factions without a map.
        return Multiplayer.Client == null || target is not Map { IsPocketMap: true } || Find.AnyPlayerHomeMap != null;
    }
}
