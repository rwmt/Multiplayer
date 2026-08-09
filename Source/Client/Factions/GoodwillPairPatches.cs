using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.Client.Factions;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Patches;

// Vanilla funnels all goodwill through Faction.OfPlayer - the spectator during
// the multifaction world tick - so natural drift and ideo-derived caps only
// ever touched the spectator pair. Faction.relations is already pairwise;
// these patches fix the access paths: per-faction GoodwillSituationManager
// instances (swapped in SetFaction) and per-NPC drift timers, with the drift
// check and 1000-tick caps recalc fanned per ownable faction under context.

[HarmonyPatch(typeof(Faction), "CheckReachNaturalGoodwill")]
static class CheckReachNaturalGoodwillPatch
{
    static bool Prefix(Faction __instance)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return true;

        // Vanilla's early-outs, replicated so the fan below only runs for
        // factions the original would have processed
        if (__instance.IsPlayer || !__instance.HasGoodwill || __instance.def.permanentEnemy)
            return false;

        foreach (var kv in Multiplayer.WorldComp.factionData)
        {
            var faction = Find.FactionManager.GetById(kv.Key);
            if (!QuestFactionOwnership.IsOwnablePlayerFaction(faction))
                continue;

            ((Map)null).PushFaction(faction);
            try
            {
                DriftForPair(__instance, kv.Value);
            }
            finally
            {
                FactionExtensions.PopFaction();
            }
        }

        return false;
    }

    // Vanilla CheckReachNaturalGoodwill body (1.6.4871) against the pushed
    // faction's manager instance and this pair's own timer. OfPlayer is the
    // pushed faction here, so BaseGoodwillWith/TryAffectGoodwillWith land on
    // the right FactionRelation pair.
    private static void DriftForPair(Faction npc, FactionWorldData data)
    {
        data.naturalGoodwillTimers.TryGetValue(npc.loadID, out int timer);

        int current = npc.BaseGoodwillWith(Faction.OfPlayer);
        int natural = data.goodwillSituationManager.GetNaturalGoodwill(npc);
        var window = new IntRange(natural - 50, natural + 50);

        if (window.Includes(current))
        {
            data.naturalGoodwillTimers[npc.loadID] = 0;
            return;
        }

        timer++;

        if (current < window.min)
        {
            if (timer >= 3000000)
            {
                npc.TryAffectGoodwillWith(Faction.OfPlayer, Mathf.Min(10, window.min - current),
                    canSendMessage: true, canSendHostilityLetter: !npc.temporary,
                    HistoryEventDefOf.ReachNaturalGoodwill);
                timer = 0;
            }
        }
        else if (current > window.max && timer >= 3000000)
        {
            npc.TryAffectGoodwillWith(Faction.OfPlayer, -Mathf.Min(10, current - window.max),
                canSendMessage: true, canSendHostilityLetter: !npc.temporary,
                HistoryEventDefOf.ReachNaturalGoodwill);
            timer = 0;
        }

        data.naturalGoodwillTimers[npc.loadID] = timer;
    }
}

[HarmonyPatch(typeof(GoodwillSituationManager), nameof(GoodwillSituationManager.GoodwillManagerTick))]
static class GoodwillManagerTickPatch
{
    static bool Prefix()
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return true;

        if (Find.TickManager.TicksGame % 1000 != 0)
            return false;

        foreach (var kv in Multiplayer.WorldComp.factionData)
        {
            var faction = Find.FactionManager.GetById(kv.Key);
            if (!QuestFactionOwnership.IsOwnablePlayerFaction(faction))
                continue;

            ((Map)null).PushFaction(faction);
            try
            {
                kv.Value.goodwillSituationManager.RecalculateAll(canSendHostilityChangedLetter: true);
            }
            finally
            {
                FactionExtensions.PopFaction();
            }
        }

        return false;
    }
}

// Cache fills from UI context must not persist: workers read live sim state,
// so a client whose UI filled an entry at tick T holds different cached
// values than one whose entry filled at the 1000-tick recalc - and the drift
// check trusts the cache. Pre-existing upstream hazard on stable MP too
// (faction dialog explanations fill lazily), worse with per-faction caches.
// UI reads get a fresh uncached computation; sim-context fills persist as
// vanilla. Persisting fills also fire CheckHostilityChanged (relation-kind
// flips + letters), so this gate keeps that sim-only as a side effect.
[HarmonyPatch(typeof(GoodwillSituationManager), nameof(GoodwillSituationManager.GetSituations))]
static class GetSituationsUIGuard
{
    private static readonly AccessTools.FieldRef<GoodwillSituationManager, Dictionary<Faction, List<GoodwillSituationManager.CachedSituation>>>
        cachedDataRef = AccessTools.FieldRefAccess<GoodwillSituationManager, Dictionary<Faction, List<GoodwillSituationManager.CachedSituation>>>("cachedData");

    private static readonly FastInvokeHandler recalculateInto = MethodInvoker.GetHandler(
        AccessTools.Method(typeof(GoodwillSituationManager), "Recalculate",
            new[] { typeof(Faction), typeof(List<GoodwillSituationManager.CachedSituation>) }));

    static bool Prefix(GoodwillSituationManager __instance, Faction other,
        ref List<GoodwillSituationManager.CachedSituation> __result)
    {
        if (Multiplayer.Client == null || Multiplayer.Ticking || Multiplayer.ExecutingCmds)
            return true;

        if (other == null || other.IsPlayer)
            return true;

        if (cachedDataRef(__instance).TryGetValue(other, out var cached))
        {
            __result = cached;
            return false;
        }

        var fresh = new List<GoodwillSituationManager.CachedSituation>();
        recalculateInto(__instance, other, fresh);
        __result = fresh;
        return false;
    }
}
