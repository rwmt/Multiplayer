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
            if (FactionCanReceiveIncidentsOn(Multiplayer.MapContext, Faction.OfPlayer))
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

    // In multifaction every player faction's storyteller ticks on every map (FactionRepeater). Keep each one
    // to its own maps and to unowned maps (sites etc.) where it has people, so faction A's incidents
    // (e.g. Map_Misc ones while A's colonists visit) don't land on faction B's home.
    static bool FactionCanReceiveIncidentsOn(Map map, Faction faction)
    {
        if (!Multiplayer.GameComp.multifaction)
            return true;

        if (map.ParentFaction == faction)
            return true;

        return map.ParentFaction is not { IsPlayer: true } &&
               map.mapPawns.SpawnedPawnsInFaction(faction).Any(p => p.RaceProps.Humanlike);
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

// The world tick runs incident code for maps (incident parms, TryExecute); it runs in that map's context. Incidents can
// nest (tickingWorld stays set): a nested call for the same map stays in its context, one for another map leaves the
// outer map's context for the duration and re-enters it afterwards, so each map's time, Rand stream and faction apply.
static class WorldTickMapContext
{
    public static void Enter(IIncidentTarget target, out (Map map, Map prev) state)
    {
        state = default;
        if (!AsyncWorldTimeComp.tickingWorld || target is not Map map) return;

        var prev = AsyncTimeComp.tickingMap;
        if (prev == map) return;

        prev?.AsyncTime().PostContext();
        AsyncTimeComp.tickingMap = map;
        map.AsyncTime().PreContext();
        state = (map, prev);
    }

    public static void Exit((Map map, Map prev) state)
    {
        if (state.map == null) return;

        state.map.AsyncTime().PostContext();
        AsyncTimeComp.tickingMap = state.prev;
        state.prev?.AsyncTime().PreContext();
    }
}

[HarmonyPatch(typeof(StorytellerUtility), nameof(StorytellerUtility.DefaultParmsNow))]
static class MapContextIncidentParms
{
    static void Prefix(IIncidentTarget target, out (Map, Map) __state) => WorldTickMapContext.Enter(target, out __state);

    static void Finalizer((Map, Map) __state) => WorldTickMapContext.Exit(__state);
}

[HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
static class MapContextIncidentExecute
{
    static void Prefix(IncidentParms parms, out (Map, Map) __state) => WorldTickMapContext.Enter(parms.target, out __state);

    static void Finalizer((Map, Map) __state) => WorldTickMapContext.Exit(__state);
}

[HarmonyPatch(typeof(Settlement), nameof(Settlement.IncidentTargetTags))]
static class SettlementIncidentTargetTagsPatch
{
    static IEnumerable<IncidentTargetTagDef> Postfix(IEnumerable<IncidentTargetTagDef> tags, Settlement __instance)
    {
        foreach (var tag in tags)
        {
            // Each map ticks its storyteller in that map's faction context. Do not let
            // another player faction's settlement advertise itself as this map's player home.
            if (tag == IncidentTargetTagDefOf.Map_PlayerHome &&
                Multiplayer.Client != null &&
                Multiplayer.GameComp.multifaction &&
                __instance.Faction is { IsPlayer: true } faction &&
                faction != Faction.OfPlayer)
                continue;

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
