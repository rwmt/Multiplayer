using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace Multiplayer.Client.Factions;

// IdeoManager keeps three scalars as single globals. Under
// the per-faction storyteller/thought fan they become cross-faction bleed:
// every faction drains the one gauranlen pod counter (and the reset value's
// divisibility parks the zero-crossing on the same faction every cycle), and
// one faction's resettle or psychic ritual moves every faction's precept
// moods. Per-faction state lives on FactionWorldData; the vanilla globals
// stay written so SP and single-faction MP are untouched.
static class IdeoCounterPatches
{
    public static FactionWorldData ContextFactionData()
    {
        var f = Faction.OfPlayer;
        if (f == null || !QuestFactionOwnership.IsOwnablePlayerFaction(f))
            return null;
        return Multiplayer.WorldComp.factionData.TryGetValue(f.loadID, out var data) ? data : null;
    }
}

[HarmonyPatch(typeof(StorytellerComp_GauranlenPodSpawn), nameof(StorytellerComp_GauranlenPodSpawn.MakeIntervalIncidents))]
static class PerFactionGauranlenPodCounter
{
    static bool Prefix(StorytellerComp_GauranlenPodSpawn __instance, IIncidentTarget target,
        ref IEnumerable<FiringIncident> __result)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return true;

        __result = PerFaction(__instance, target);
        return false;
    }

    static IEnumerable<FiringIncident> PerFaction(StorytellerComp_GauranlenPodSpawn comp, IIncidentTarget target)
    {
        // Spectator context (or a faction with no data) must not drain anyone
        var data = IdeoCounterPatches.ContextFactionData();
        if (data == null)
            yield break;

        // Replicated from StorytellerComp_GauranlenPodSpawn.MakeIntervalIncidents
        // (1.6.4871) with the counter swapped to the context faction's.
        // Re-verify against the decompile on game update.
        var props = (StorytellerCompProperties_GauranlenPodSpawn)comp.props;
        if (!ModsConfig.IdeologyActive || props.daysBetweenPodSpawns == 0f ||
            Faction.OfPlayer.ideos == null || (float)GenDate.DaysPassed < props.minDaysPassed)
            yield break;

        int num = 1;
        if (props.countdownFactorAnyConnectors > 1)
        {
            foreach (Ideo ideo in Faction.OfPlayer.ideos.AllIdeos)
            {
                if (ideo.HasMeme(MemeDefOf.TreeConnection))
                {
                    num = props.countdownFactorAnyConnectors;
                    break;
                }
            }
        }

        // First sim use for this faction: take over the shared global's
        // progress instead of restarting a full cycle. Sim-only path, so the
        // seed write is deterministic on every client.
        if (data.ticksToNextGauranlenSpawn < 0)
            data.ticksToNextGauranlenSpawn = Find.IdeoManager.ticksToNextGauranlenSpawn;

        data.ticksToNextGauranlenSpawn -= 1000 * num;
        if (data.ticksToNextGauranlenSpawn <= 0)
        {
            data.ticksToNextGauranlenSpawn = (int)(props.daysBetweenPodSpawns * 60000f);
            yield return new FiringIncident(props.incident, comp, comp.GenerateParms(props.incident.category, target));
        }
    }
}

// Writers: stamp the acting faction's copy whenever vanilla stamped the
// global this tick. Comparing against the global inherits vanilla's own
// gating (e.g. AddNewHome only stamps for faction == Faction.OfPlayer)
// without replicating it.
[HarmonyPatch(typeof(SettleUtility), nameof(SettleUtility.AddNewHome))]
static class PerFactionResettledStampSettle
{
    static void Postfix()
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return;
        if (Find.IdeoManager != null && Find.IdeoManager.lastResettledTick == GenTicks.TicksGame &&
            IdeoCounterPatches.ContextFactionData() is { } data)
            data.lastResettledTick = GenTicks.TicksGame;
    }
}

[HarmonyPatch(typeof(GravshipUtility), nameof(GravshipUtility.ArriveNewMap))]
static class PerFactionResettledStampGravship
{
    static void Postfix()
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return;
        if (Find.IdeoManager != null && Find.IdeoManager.lastResettledTick == GenTicks.TicksGame &&
            IdeoCounterPatches.ContextFactionData() is { } data)
            data.lastResettledTick = GenTicks.TicksGame;
    }
}

[HarmonyPatch(typeof(LordToil_PsychicRitual), "RitualCompleted")]
static class PerFactionPsychicRitualStamp
{
    static void Postfix()
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return;
        if (Find.IdeoManager != null && Find.IdeoManager.lastPsychicRitualPerformedTick == Find.TickManager.TicksGame &&
            IdeoCounterPatches.ContextFactionData() is { } data)
            data.lastPsychicRitualPerformedTick = Find.TickManager.TicksGame;
    }
}

// Readers: swap the context faction's values into the globals around the
// thought-worker bodies, restore unconditionally. -1 (never stamped for this
// faction) reads as vanilla's default 0 WITHOUT writing anything - reads can
// run from UI context (mood tooltips), where a lazily-seeded write would be a
// per-client mutation of scribed state.
[HarmonyPatch]
static class PerFactionIdeoStampReads
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ThoughtWorker_Precept_ResettledRecently), "ShouldHaveThought");
        yield return AccessTools.Method(typeof(ThoughtWorker_Precept_ResettledRecently), "CurrentStateInternal");
        yield return AccessTools.Method(typeof(ThoughtWorker_Precept_NoPsychicRituals), "ShouldHaveThought");
    }

    static void Prefix(ref (int resettled, int psychic)? __state)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction || Find.IdeoManager == null)
            return;
        if (IdeoCounterPatches.ContextFactionData() is not { } data)
            return;

        __state = (Find.IdeoManager.lastResettledTick, Find.IdeoManager.lastPsychicRitualPerformedTick);
        Find.IdeoManager.lastResettledTick = Math.Max(0, data.lastResettledTick);
        Find.IdeoManager.lastPsychicRitualPerformedTick = Math.Max(0, data.lastPsychicRitualPerformedTick);
    }

    static void Finalizer((int resettled, int psychic)? __state)
    {
        if (__state is not { } saved)
            return;
        Find.IdeoManager.lastResettledTick = saved.resettled;
        Find.IdeoManager.lastPsychicRitualPerformedTick = saved.psychic;
    }
}

// GetDescriptionArgs is an iterator, so a field swap around the call would be
// restored before the body ever runs - replace the one-line body instead.
// Replicated from ThoughtWorker_Precept_ResettledRecently.GetDescriptionArgs
// (1.6.4871).
[HarmonyPatch(typeof(ThoughtWorker_Precept_ResettledRecently), nameof(ThoughtWorker_Precept_ResettledRecently.GetDescriptionArgs))]
static class PerFactionResettledDescription
{
    static bool Prefix(ref IEnumerable<NamedArgument> __result)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return true;
        if (IdeoCounterPatches.ContextFactionData() is not { } data)
            return true;

        int ticksSince = GenTicks.TicksGame - Math.Max(0, data.lastResettledTick);
        __result = new List<NamedArgument> { ticksSince.ToStringTicksToPeriod().Named("DURATION") };
        return false;
    }
}
