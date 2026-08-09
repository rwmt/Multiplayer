using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Factions;

// Multifaction: WorldObjectComp ticking runs under the spectator world tick,
// so core-side Faction.OfPlayer gates in those paths match nobody. Sites
// with the same root cause:
// - TimedForcedExit.ForceReform gathers `x.Faction == Faction.OfPlayer` pawns
//   and reforms via ExitMapAndCreateCaravan(.., Faction.OfPlayer): under the
//   spectator both match nothing and an expiring site map can close over the
//   owner's pawns.
// - DefeatAllEnemiesQuestComp.CompTickInterval checks
//   AnyHostileActiveThreatToPlayer and delivers rewards to AnyPlayerHomeMap:
//   completion misdetects and the letter/rewards misfire for everyone.
// - CaravansBattlefield.CheckWonBattle uses AnyHostileActiveThreatToPlayer and
//   FreeColonists.RandomElement for the victory tale/letter: under Spectator
//   FreeColonists is empty on every client (ParentFaction is enemy/null, so the
//   existing WorldObjectMethodPatches push cannot install a player owner).
// Each gets the deterministic owner of the site map pushed around the body.
static class WorldTickOwnerContext
{
    // Deterministic "whose map is this" for maps without a player parent
    // faction: the lowest-loadID ownable player faction with a humanlike pawn
    // spawned there (same site rule as StorytellerTargetsPatch, same selector
    // shape as IdeoContextUtil.PrimaryPlayerFollower - identical on all
    // clients).
    public static Faction DeterministicMapOwner(Map map)
    {
        if (map == null)
            return null;

        if (map.ParentFaction is { IsPlayer: true } parent &&
            QuestFactionOwnership.IsOwnablePlayerFaction(parent))
            return parent;

        return Find.FactionManager.AllFactionsListForReading
            .Where(f => QuestFactionOwnership.IsOwnablePlayerFaction(f) &&
                        map.mapPawns.SpawnedPawnsInFaction(f).Any(p => p.RaceProps.Humanlike))
            .OrderBy(f => f.loadID)
            .FirstOrDefault();
    }

    public static bool PushOwnerOf(Map map)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return false;

        var owner = DeterministicMapOwner(map);
        if (owner == null || owner == Faction.OfPlayer)
            return false;

        ((Map)null).PushFaction(owner);
        return true;
    }
}

[HarmonyPatch(typeof(TimedForcedExit), nameof(TimedForcedExit.ForceReform))]
static class TimedForcedExitOwnerContext
{
    static void Prefix(MapParent mapParent, ref bool __state) =>
        __state = WorldTickOwnerContext.PushOwnerOf(mapParent?.Map);

    static void Finalizer(bool __state)
    {
        if (__state)
            FactionExtensions.PopFaction();
    }
}

[HarmonyPatch(typeof(DefeatAllEnemiesQuestComp), nameof(DefeatAllEnemiesQuestComp.CompTickInterval))]
static class DefeatAllEnemiesQuestOwnerContext
{
    static void Prefix(DefeatAllEnemiesQuestComp __instance, ref bool __state)
    {
        if (__instance.Active && __instance.parent is MapParent { HasMap: true } mapParent)
            __state = WorldTickOwnerContext.PushOwnerOf(mapParent.Map);
    }

    static void Finalizer(bool __state)
    {
        if (__state)
            FactionExtensions.PopFaction();
    }
}

// CaravansBattlefield.CheckWonBattle is private; ParentFaction is typically
// the ambushers (or null), so WorldObjectMethodPatches cannot push a player
// owner. Push the deterministic map owner so FreeColonists / victory letter
// see the caravan faction instead of Spectator.
[HarmonyPatch(typeof(CaravansBattlefield), "CheckWonBattle")]
static class CaravansBattlefieldWonBattleOwnerContext
{
    static void Prefix(CaravansBattlefield __instance, ref bool __state)
    {
        if (__instance.HasMap)
            __state = WorldTickOwnerContext.PushOwnerOf(__instance.Map);
    }

    static void Finalizer(bool __state)
    {
        if (__state)
            FactionExtensions.PopFaction();
    }
}

// When a mission's stamped destination map
// is lost mid-flight, SendAway's private picker scans settlements for
// Faction.OfPlayer - under the spectator world tick that matches nobody and
// the faction-blind AnyPlayerHomeMap fallback unloads the mission's pawns and
// loot at an arbitrary player colony. Push the mission owner so vanilla's own
// scan starts matching; no body replication, no new scribed state.
[HarmonyPatch(typeof(ShipJob_WaitSendable), "SendAway")]
static class ShipJobWaitSendableOwnerContext
{
    static void Prefix(ShipJob_WaitSendable __instance, ref bool __state)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return;

        var owner = ResolveOwner(__instance);
        if (owner == null || owner == Faction.OfPlayer)
            return;

        ((Map)null).PushFaction(owner);
        __state = true;
    }

    static void Finalizer(bool __state)
    {
        if (__state)
            FactionExtensions.PopFaction();
    }

    // Deterministic on every client: the quest table, the transporter's
    // contents and DeterministicMapOwner are all synced state. The quest scan
    // skips only Historical - never quest.hidden/dismissed like vanilla's
    // gizmo scan, because dismissal is a per-client UI action.
    static Faction ResolveOwner(ShipJob_WaitSendable job)
    {
        var shipThing = job.transportShip?.shipThing;
        if (shipThing == null)
            return null;

        var quests = Find.QuestManager.QuestsListForReading;
        for (int i = 0; i < quests.Count; i++)
        {
            if (quests[i].Historical)
                continue;
            if (quests[i].QuestLookTargets.Contains(shipThing) &&
                QuestFactionOwnership.GetOwner(quests[i]) is { } questOwner)
                return questOwner;
        }

        if (job.transportShip.TransporterComp?.innerContainer is { } held)
            foreach (var thing in held)
                if (thing is Pawn { RaceProps.Humanlike: true } pawn &&
                    QuestFactionOwnership.IsOwnablePlayerFaction(pawn.Faction))
                    return pawn.Faction;

        return WorldTickOwnerContext.DeterministicMapOwner(shipThing.Map);
    }
}
