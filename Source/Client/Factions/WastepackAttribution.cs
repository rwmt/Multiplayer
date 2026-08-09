using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Multiplayer.Client.Util;
using Verse;

namespace Multiplayer.Client.Factions;

// Multifaction: wastepack goodwill/retaliation events carry no dumper.
// Vanilla queues { tile, amount } and applies via Faction.OfPlayer in the
// world tick - under the spectator context the goodwill hit lands on nobody
// and the retaliation quest's map pick has no owner in context (falls back to
// the lowest-loadID player). Mirror the pending queue with a dumper faction
// per event, resolved at enqueue time (ambient push, holder chain, per-thing
// stamp, or the synced-command faction), and re-run the apply grouped by
// (tile, dumper) under that faction's context. Unknown dumper: goodwill
// behaves as before (spectator no-op) and retaliation is dropped - a raid
// must never fall back to an arbitrary player.
public static class WastepackAttribution
{
    // Mirrors CompDissolutionEffect_Goodwill.pendingGoodwillEvents 1:1.
    // Entries are faction loadIDs, -1 = unknown.
    public static readonly List<int> pendingDumpers = new();

    // Set around enqueue paths whose call site knows the dumper but whose
    // thing (if any) can't tell us (abandoned-map events have no thing).
    public static int ambientDumper = -1;

    public static bool MpActive => Multiplayer.Client != null && Multiplayer.GameComp.multifaction;

    static bool Ownable(Faction f) => QuestFactionOwnership.IsOwnablePlayerFaction(f);

    public static int ResolveDumper(Thing pack)
    {
        if (ambientDumper != -1)
            return ambientDumper;

        if (pack != null)
        {
            // Held packs: the holder chain names the dumper (transporter world
            // object, caravan, carrying pawn - all still parented at the
            // Notify_AbandonedAtTile/CompTick call sites).
            for (var holder = pack.ParentHolder; holder != null; holder = holder.ParentHolder)
            {
                var f = holder switch
                {
                    WorldObject wo => wo.Faction,
                    Pawn p => p.Faction,
                    Thing t => t.Faction,
                    _ => null
                };
                if (Ownable(f))
                    return f.loadID;
            }

            if (Multiplayer.WorldComp.wastepackDumpers.TryGetValue(pack.thingIDNumber, out var stamped) &&
                Ownable(Find.FactionManager.GetById(stamped)))
                return stamped;
        }

        // Synced-command context (player-ordered dumps, jobs under a pushed
        // faction) - the spectator is not ownable, so world-tick noise stays -1
        if (Ownable(Faction.OfPlayer))
            return Faction.OfPlayer.loadID;

        return -1;
    }

    // Keeps the mirror aligned with the vanilla list even when an enqueue was
    // gated off (pollution-ignored quests, unspawned packs) or a third party
    // enqueued while our patches were bypassed: pad, then append per growth.
    public static void MirrorAppend(int prevCount, Thing source)
    {
        var events = CompDissolutionEffect_Goodwill.pendingGoodwillEvents;

        while (pendingDumpers.Count < prevCount)
            pendingDumpers.Add(-1);

        for (int i = prevCount; i < events.Count; i++)
            pendingDumpers.Add(ResolveDumper(source));
    }

    // Vanilla WorldUpdate re-run grouped by (tile, dumper); apply wrapped in
    // the dumper's faction context so every Faction.OfPlayer read - goodwill,
    // IsPlayerTile, AnyPlayerHomeMap gates, GetMap's owner filter - resolves
    // to the polluter. Body mirrors CompDissolutionEffect_Goodwill.WorldUpdate
    // (1.6.4871); keep in lockstep with the decompile on game updates.
    public static void RunAttributed()
    {
        var events = CompDissolutionEffect_Goodwill.pendingGoodwillEvents;
        if (events.Count == 0)
        {
            pendingDumpers.Clear();
            return;
        }

        while (pendingDumpers.Count < events.Count)
            pendingDumpers.Add(-1);

        foreach (var group in events
                     .Select((e, i) => (e, dumperId: pendingDumpers[i]))
                     .GroupBy(x => (x.e.tile, x.dumperId)))
        {
            PlanetTile key = group.Key.tile;
            var dumper = Find.FactionManager.GetById(group.Key.dumperId);
            if (!Ownable(dumper))
                dumper = null;

            if (CompDissolutionEffect_Goodwill.TryGetAffectedSettlement(key, out var result, out var distance))
            {
                if (dumper != null)
                    ((Map)null).PushFaction(dumper);

                try
                {
                    int num = group.Sum(p => p.e.amount);
                    int num2 = UnityEngine.Mathf.Min(
                        -UnityEngine.Mathf.RoundToInt(
                            CompDissolutionEffect_Goodwill.GoodwillFactorOverDistanceCurvePerWastepack.Evaluate(distance) * num), -1);
                    HistoryEventDef historyEventDef =
                        ModsConfig.OdysseyActive && key.LayerDef.isSpace ? HistoryEventDefOf.OrbitalPollution
                        : result.Tile == key ? HistoryEventDefOf.PollutedBase
                        : distance > 8 ? HistoryEventDefOf.ToxicWasteDumping
                        : HistoryEventDefOf.PollutedNearbySite;

                    if (result.Faction.IsPlayerGoodwillMinimum())
                        Messages.Message("MessageAngeredPollutedCell".Translate(result.Faction.Name, historyEventDef.label),
                            result, MessageTypeDefOf.NegativeEvent);

                    Faction.OfPlayer.TryAffectGoodwillWith(result.Faction, num2, canSendMessage: true,
                        canSendHostilityLetter: true, historyEventDef, result);

                    if (!Current.Game.IsPlayerTile(key) && result.Faction.HostileTo(Faction.OfPlayer) &&
                        Find.AnyPlayerHomeMap != null &&
                        Rand.Chance(UnityEngine.Mathf.Clamp01((float)-num2 / 100f)))
                    {
                        if (dumper != null)
                            CompDissolutionEffect_Goodwill.TriggerRetaliationEvent(result.Faction);
                        else
                            Log.WarningOnce(
                                $"MP: dropped a wastepack retaliation with no attributable dumper (tile {key})",
                                Gen.HashCombineInt(key.GetHashCode(), 0x5EA7));
                    }
                }
                finally
                {
                    if (dumper != null)
                        FactionExtensions.PopFaction();
                }
            }

            CompDissolutionEffect_Goodwill.tmpAvailableSettlements.Clear();
        }

        events.Clear();
        pendingDumpers.Clear();
    }
}

[HarmonyPatch(typeof(CompDissolutionEffect_Goodwill), nameof(CompDissolutionEffect_Goodwill.WorldUpdate))]
static class WastepackWorldUpdateAttributed
{
    static bool Prefix()
    {
        if (!WastepackAttribution.MpActive)
        {
            // Vanilla will clear the event list; keep the mirror in step
            WastepackAttribution.pendingDumpers.Clear();
            return true;
        }

        WastepackAttribution.RunAttributed();
        return false;
    }
}

[HarmonyPatch(typeof(CompDissolutionEffect_Goodwill), nameof(CompDissolutionEffect_Goodwill.DoDissolutionEffectMap))]
static class WastepackMirrorMapEvent
{
    static void Prefix(ref int __state) =>
        __state = CompDissolutionEffect_Goodwill.pendingGoodwillEvents.Count;

    static void Postfix(CompDissolutionEffect_Goodwill __instance, int __state)
    {
        if (WastepackAttribution.MpActive)
            WastepackAttribution.MirrorAppend(__state, __instance.parent);
    }
}

[HarmonyPatch(typeof(CompDissolutionEffect_Goodwill), nameof(CompDissolutionEffect_Goodwill.AddWorldDissolutionEvent))]
static class WastepackMirrorWorldEvent
{
    static void Prefix(ref int __state) =>
        __state = CompDissolutionEffect_Goodwill.pendingGoodwillEvents.Count;

    static void Postfix(int __state)
    {
        if (WastepackAttribution.MpActive)
            WastepackAttribution.MirrorAppend(__state, null);
    }
}

// World dissolution of a held pack (caravan inventory tick, transporter
// "arrived and lost", abandon utilities): resolve from the holder chain while
// the pack is still parented
[HarmonyPatch(typeof(CompDissolution), nameof(CompDissolution.DissolveWorld))]
static class WastepackAmbientFromHolder
{
    static void Prefix(CompDissolution __instance, ref bool __state)
    {
        if (!WastepackAttribution.MpActive || WastepackAttribution.ambientDumper != -1)
            return;

        WastepackAttribution.ambientDumper = WastepackAttribution.ResolveDumper(__instance.parent);
        __state = true;
    }

    static void Finalizer(bool __state)
    {
        if (__state)
            WastepackAttribution.ambientDumper = -1;
    }
}

// Abandoned map: the synthetic world event has no thing - the dumper is the
// map's owner
[HarmonyPatch(typeof(PollutionInfo), nameof(PollutionInfo.MapRemoved))]
static class WastepackAmbientFromAbandonedMap
{
    static void Prefix(PollutionInfo __instance, ref bool __state)
    {
        if (!WastepackAttribution.MpActive)
            return;

        if (__instance.map.ParentFaction is { IsPlayer: true } owner &&
            owner != Multiplayer.WorldComp.spectatorFaction)
        {
            WastepackAttribution.ambientDumper = owner.loadID;
            __state = true;
        }
    }

    static void Finalizer(bool __state)
    {
        if (__state)
            WastepackAttribution.ambientDumper = -1;
    }
}

// Family-4 stamps: a spawned pack rotting on a non-home map has no holder and
// no context, so the dumper is recorded ahead of time (scribed - rot takes
// days)

// Pods/shuttles landing on an existing map: stamp cargo with the launcher
// before the arrival action scatters it
[HarmonyPatch(typeof(TravellingTransporters), nameof(TravellingTransporters.Arrived))]
static class WastepackStampOnArrival
{
    static void Prefix(TravellingTransporters __instance)
    {
        if (!WastepackAttribution.MpActive)
            return;
        if (__instance.Faction is not { IsPlayer: true } faction ||
            faction == Multiplayer.WorldComp.spectatorFaction)
            return;

        foreach (var info in __instance.transporters)
            foreach (var thing in info.innerContainer)
                if (thing.def == ThingDefOf.Wastepack)
                    Multiplayer.WorldComp.wastepackDumpers[thing.thingIDNumber] = faction.loadID;
    }
}

// Mech charger/gestator waste: stamp at production with the building's owner
[HarmonyPatch(typeof(CompWasteProducer), nameof(CompWasteProducer.ProduceWaste))]
static class WastepackStampOnProduction
{
    static void Postfix(CompWasteProducer __instance)
    {
        if (!WastepackAttribution.MpActive)
            return;
        if (__instance.parent.Faction is not { IsPlayer: true } faction ||
            faction == Multiplayer.WorldComp.spectatorFaction)
            return;

        var owner = __instance.parent.TryGetInnerInteractableThingOwner();
        if (owner == null)
            return;

        foreach (var thing in owner)
            if (thing.def == ThingDefOf.Wastepack &&
                !Multiplayer.WorldComp.wastepackDumpers.ContainsKey(thing.thingIDNumber))
                Multiplayer.WorldComp.wastepackDumpers[thing.thingIDNumber] = faction.loadID;
    }
}

// Catch-all: a pack spawning onto a non-home map under a synced command
// (pawn drops, trades) stamps from the acting faction. Existing stamps win.
[HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
static class WastepackStampOnSpawn
{
    static void Postfix(Thing __instance, Map map, bool respawningAfterLoad)
    {
        if (respawningAfterLoad || !WastepackAttribution.MpActive)
            return;
        if (__instance.def != ThingDefOf.Wastepack || map == null || map.IsPlayerHome)
            return;
        if (Multiplayer.WorldComp.wastepackDumpers.ContainsKey(__instance.thingIDNumber))
            return;

        var resolved = WastepackAttribution.ResolveDumper(__instance);
        if (resolved != -1)
            Multiplayer.WorldComp.wastepackDumpers[__instance.thingIDNumber] = resolved;
    }
}

[HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
static class WastepackStampPrune
{
    static void Prefix(Thing __instance)
    {
        if (WastepackAttribution.MpActive && __instance.def == ThingDefOf.Wastepack)
            Multiplayer.WorldComp.wastepackDumpers.Remove(__instance.thingIDNumber);
    }
}
