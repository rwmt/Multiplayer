using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace Multiplayer.Client.Factions;

// Multifaction: vanilla quests have no owning faction, so quest parts executed
// in whatever context the tick held (usually spectator) - reward pawns joined
// the Spectator faction (#546), letters and ideo checks hit the wrong faction.
// Quests are now stamped with an owner whose context wraps their execution.
public static class QuestFactionOwnership
{
    public static Faction GetOwner(Quest quest)
    {
        if (quest == null ||
            !Multiplayer.WorldComp.questOwnership.TryGetValue(quest.id, out var factionId))
            return null;
        return Find.FactionManager.GetById(factionId);
    }

    public static void Stamp(Quest quest, Faction faction)
    {
        if (quest != null && faction != null)
            Multiplayer.WorldComp.questOwnership[quest.id] = faction.loadID;
    }

    public static bool IsOwnablePlayerFaction(Faction f) =>
        f is { IsPlayer: true } && f != Multiplayer.WorldComp.spectatorFaction;

    public static Faction ResolveOwner(Quest quest, Faction contextFaction)
    {
        // 1. Generation context (per-faction storyteller loop, synced command)
        if (IsOwnablePlayerFaction(contextFaction))
            return contextFaction;

        // 2. Infer from quest look targets (owning settlement)
        if (quest.TryGetPlayerFaction(out var inferred) && IsOwnablePlayerFaction(inferred))
            return inferred;

        // 3. Deterministic fallback: lowest-loadID player faction (the host's)
        return Find.FactionManager.AllFactionsListForReading
            .Where(IsOwnablePlayerFaction)
            .OrderBy(f => f.loadID)
            .FirstOrDefault();
    }

    // Old saves have no ownership data
    public static void BackfillOwnership()
    {
        var backfilled = 0;
        foreach (var quest in Find.QuestManager.QuestsListForReading)
        {
            if (Multiplayer.WorldComp.questOwnership.ContainsKey(quest.id))
                continue;
            Stamp(quest, ResolveOwner(quest, null));
            backfilled++;
        }

        if (backfilled > 0)
            MpLog.Log($"Backfilled faction ownership for {backfilled} quests");
    }
}

[HarmonyPatch(typeof(QuestGen), nameof(QuestGen.Generate))]
static class StampQuestFactionOnGeneration
{
    static void Postfix(Quest __result)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction || __result == null)
            return;

        var contextFaction = Faction.OfPlayer;
        var owner = QuestFactionOwnership.ResolveOwner(__result, contextFaction);

        // Logs identify generation sources that still run unrouted
        if (!QuestFactionOwnership.IsOwnablePlayerFaction(contextFaction))
            MpLog.Log($"Quest {__result.root?.defName} generated outside a player faction " +
                      $"context ({contextFaction?.Name}); assigned to {owner?.Name}");

        QuestFactionOwnership.Stamp(__result, owner);
    }
}

[HarmonyPatch(typeof(Quest), nameof(Quest.Accept))]
static class StampQuestFactionOnAccept
{
    // Ownership follows whoever accepts (the synced command's context faction)
    static void Prefix(Quest __instance)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return;

        if (QuestFactionOwnership.IsOwnablePlayerFaction(Faction.OfPlayer))
            QuestFactionOwnership.Stamp(__instance, Faction.OfPlayer);
    }
}

[HarmonyPatch]
static class PushQuestOwnerContext
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Quest), nameof(Quest.QuestTick));
        yield return AccessTools.Method(typeof(Quest), nameof(Quest.Notify_SignalReceived));
        yield return AccessTools.Method(typeof(Quest), nameof(Quest.CleanupQuestParts));
    }

    static void Prefix(Quest __instance, ref bool __state)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return;

        var owner = QuestFactionOwnership.GetOwner(__instance);
        if (owner == null)
            return;

        // Push/pop are null-balanced, safe when the owner already holds context
        ((Map)null).PushFaction(owner);
        __state = true;
    }

    static void Finalizer(bool __state)
    {
        if (__state)
            FactionExtensions.PopFaction();
    }
}

[HarmonyPatch(typeof(QuestManager), nameof(QuestManager.Remove))]
static class RemoveQuestOwnership
{
    static void Postfix(Quest quest)
    {
        if (Multiplayer.Client != null && quest != null)
            Multiplayer.WorldComp?.questOwnership.Remove(quest.id);
    }
}

// Vanilla QuestNode_GetMap picks any IsPlayerHome map, so one faction's
// quest could bind another player's colony. Generation context = owner;
// reject other player factions' maps (neutral/site maps stay acceptable).
[HarmonyPatch(typeof(RimWorld.QuestGen.QuestNode_GetMap), "IsAcceptableMap")]
static class QuestMapMatchesGeneratingFaction
{
    static void Postfix(Map map, ref bool __result)
    {
        if (!__result || Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return;

        if (QuestFactionOwnership.IsOwnablePlayerFaction(Faction.OfPlayer) &&
            map.ParentFaction is { IsPlayer: true } && map.ParentFaction != Faction.OfPlayer)
            __result = false;
    }
}

// Multifaction: QuestNode_Root_ReliquaryPilgrims has its own private map
// picker accepting any player map with a reliquary - neither GetMap owner
// filter applies. Re-pick among the generating faction's own maps.
[HarmonyPatch(typeof(QuestNode_Root_ReliquaryPilgrims), "GetMap")]
static class ReliquaryPilgrimsMapOwnerFilter
{
    static void Postfix(ref Map __result)
    {
        if (__result == null || Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return;

        if (!QuestFactionOwnership.IsOwnablePlayerFaction(Faction.OfPlayer) ||
            __result.ParentFaction is not { IsPlayer: true } || __result.ParentFaction == Faction.OfPlayer)
            return;

        Find.Maps
            .Where(m => m.IsPlayerHome && m.ParentFaction == Faction.OfPlayer &&
                        QuestNode_Root_ReliquaryPilgrims.TryFindReliquaryWithRelic(m, out _, out _, out _))
            .TryRandomElement(out var rePicked);
        __result = rePicked;
    }
}

// Same gap: QuestNode_Root_WorkSite picks its map with an inline predicate
// over IsPlayerHome maps. The predicate is a compiler-generated lambda -
// resolved by ordinal (like VNPE's Drain gizmo) and shape-checked so a game
// update degrades to a warning, not a crash.
[StaticConstructorOnStartup]
static class WorkSiteMapPickerFilter
{
    static WorkSiteMapPickerFilter()
    {
        try
        {
            var lambda = MpMethodUtil.GetLambda(typeof(QuestNode_Root_WorkSite), "RunInt", MethodType.Normal, null, 0);
            if (lambda.ReturnType != typeof(bool) ||
                lambda.GetParameters() is not { Length: 1 } ps || ps[0].ParameterType != typeof(Map))
            {
                Log.Warning("MP: WorkSite map-picker lambda shape unexpected - owner filter skipped");
                return;
            }

            Multiplayer.harmony.Patch(lambda,
                postfix: new HarmonyMethod(typeof(WorkSiteMapPickerFilter), nameof(PredicatePostfix)));
        }
        catch (Exception e)
        {
            Log.Warning($"MP: WorkSite map-picker lambda not found - owner filter skipped ({e.Message})");
        }
    }

    static void PredicatePostfix(Map m, ref bool __result)
    {
        if (!__result || Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return;

        if (QuestFactionOwnership.IsOwnablePlayerFaction(Faction.OfPlayer) &&
            m.ParentFaction is { IsPlayer: true } && m.ParentFaction != Faction.OfPlayer)
            __result = false;
    }
}

// The lambda filter narrows RunInt's candidates, so TestRunInt must agree or
// generation can proceed with no acceptable map left
[HarmonyPatch(typeof(QuestNode_Root_WorkSite), "TestRunInt")]
static class WorkSiteTestRunMatchesFilter
{
    static void Postfix(ref bool __result)
    {
        if (!__result || Multiplayer.Client == null || !Multiplayer.GameComp.multifaction ||
            !QuestFactionOwnership.IsOwnablePlayerFaction(Faction.OfPlayer))
            return;

        __result = Find.Maps.Any(m =>
            m.IsPlayerHome && m.ParentFaction == Faction.OfPlayer && QuestNode_Root_WorkSite.GetCandidates(m.Tile).Any());
    }
}

// Delivery-side: when a quest's bound map is lost mid-quest, vanilla
// retargets to the FIRST player home map, faction-blind - a hostile mech
// cluster or monument copy lands on an uninvolved colony. Quest parts call
// this under the pushed owner context, so prefer the owner's maps.
[HarmonyPatch(typeof(Quest), nameof(Quest.TryFindNewSuitableMapParentForRetarget))]
static class RetargetPrefersOwnerMaps
{
    static void Postfix(ref MapParent __result)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction ||
            !QuestFactionOwnership.IsOwnablePlayerFaction(Faction.OfPlayer))
            return;

        if (__result?.Map is { } m && m.ParentFaction == Faction.OfPlayer)
            return;

        var owned = Find.Maps.FirstOrDefault(map =>
            map.IsPlayerHome && map.ParentFaction == Faction.OfPlayer)?.Parent;
        if (owned != null)
            __result = owned;
    }
}

// Caravan royals' bestowing-ceremony checks tick in the world tick
// (spectator), so the quest was stamped to the fallback owner and its
// quest-available letter - received under spectator - was dropped for every
// client. Push the royal's own faction around generation.
[HarmonyPatch(typeof(RoyalTitleUtility), nameof(RoyalTitleUtility.GenerateBestowingCeremonyQuest))]
static class BestowingCeremonyOwnerContext
{
    static void Prefix(Pawn pawn, ref bool __state)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction ||
            !QuestFactionOwnership.IsOwnablePlayerFaction(pawn?.Faction) ||
            pawn.Faction == Faction.OfPlayer)
            return;

        ((Map)null).PushFaction(pawn.Faction);
        __state = true;
    }

    static void Finalizer(bool __state)
    {
        if (__state)
            FactionExtensions.PopFaction();
    }
}

// The same gap's second entry point: many C# quest roots (hospitality
// refugees, beggars, wanderer joins, DLC arrivals) pick their map via
// QuestGen_Get.GetMap, which never consults QuestNode_GetMap. Same owner
// rule: re-pick among the generating faction's own maps with vanilla's
// preference order; null makes the quest's map test fail, vanilla's own
// no-map behavior.
[HarmonyPatch(typeof(QuestGen_Get), nameof(QuestGen_Get.GetMap))]
static class QuestGenGetMapMatchesGeneratingFaction
{
    static void Postfix(ref Map __result, bool mustBeInfestable, int? preferMapWithMinFreeColonists, bool canBeSpace)
    {
        if (__result == null || Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return;

        if (!QuestFactionOwnership.IsOwnablePlayerFaction(Faction.OfPlayer) ||
            __result.ParentFaction is not { IsPlayer: true } || __result.ParentFaction == Faction.OfPlayer)
            return;

        int minCount = preferMapWithMinFreeColonists ?? 1;
        var ownMaps = Find.Maps.Where(m =>
            m.IsPlayerHome && m.ParentFaction == Faction.OfPlayer &&
            (canBeSpace || !m.Tile.LayerDef.isSpace) &&
            (!mustBeInfestable || InfestationCellFinder.TryFindCell(out _, m))).ToList();

        if (!ownMaps.Where(m => m.mapPawns.FreeColonists.Count >= minCount).TryRandomElement(out var rePicked))
            ownMaps.Where(m => m.mapPawns.FreeColonists.Any()).TryRandomElement(out rePicked);

        __result = rePicked;
    }
}
