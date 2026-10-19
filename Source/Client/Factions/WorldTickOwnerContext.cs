using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Factions;

// Multifaction: the world tick runs in the spectator's context, where vanilla's Faction.OfPlayer checks match no
// player. Some world tick code decides saved state or sends letters with them, push the player faction it's about.
// The pushes are forced: the world and the maps hold the spectator's data in the world tick.
static class WorldTickOwnerContext
{
    // A map's player faction: its parent's, or else the first player faction (in the faction list) with a humanlike
    // pawn on it. Only synced state is read, so it's the same on every client.
    public static Faction MapOwner(Map map)
    {
        if (map == null)
            return null;

        if (IsPlayerFaction(map.ParentFaction))
            return map.ParentFaction;

        foreach (var faction in Find.FactionManager.AllFactionsListForReading)
            if (IsPlayerFaction(faction) && map.mapPawns.SpawnedPawnsInFaction(faction).Any(p => p.RaceProps.Humanlike))
                return faction;

        return null;
    }

    // The first player faction (in the faction list) following the ideo
    public static Faction IdeoFollower(Ideo ideo)
    {
        foreach (var faction in Find.FactionManager.AllFactionsListForReading)
            if (IsPlayerFaction(faction) && faction.ideos != null && faction.ideos.Has(ideo))
                return faction;

        return null;
    }

    private static bool IsPlayerFaction(Faction faction) =>
        faction is { IsPlayer: true } && faction != Multiplayer.WorldComp.spectatorFaction;

    public static bool TryPush(Faction faction)
    {
        if (faction == null)
            return false;

        ((Map)null).PushFaction(faction, force: true);
        return true;
    }

    public static bool Active => Multiplayer.Client != null && Multiplayer.GameComp.multifaction;
}

// The win check of a caravan battle (ambush or attacked caravan) compares the threats with Faction.OfPlayer and records
// the tale with a free colonist (an error under the spectator, which has none). The battlefield's own faction is the
// enemy's or none, so the world object patches don't push a player.
[HarmonyPatch(typeof(CaravansBattlefield), nameof(CaravansBattlefield.CheckWonBattle))]
static class CaravansBattlefieldCheckWonBattle_OwnerContext
{
    static void Prefix(CaravansBattlefield __instance, ref bool __state)
    {
        // The check does nothing once won, skip the owner scan
        if (WorldTickOwnerContext.Active && !__instance.wonBattle && __instance.HasMap)
            __state = WorldTickOwnerContext.TryPush(WorldTickOwnerContext.MapOwner(__instance.Map));
    }

    static void Finalizer(bool __state)
    {
        if (__state)
            FactionExtensions.PopFaction();
    }
}

// Ideos tick in the world tick: date ritual obligations (festivals) need Faction.OfPlayer to follow the ideo (and use
// the time zone of its home), role activity letters go to it. Without a push no player ever got festivals.
// Limitation: with an ideo shared by several player factions only the first one gets the festival and role letters and
// the festival timing of its home (the obligation itself is the ideo's). Before, none of them did.
// The date is still the world's: the trigger fires on an exact tick each world tick, a map clock can pause (firing it
// again) or run ahead (skipping it).
[HarmonyPatch(typeof(Ideo), nameof(Ideo.IdeoTick))]
static class IdeoTick_FollowerContext
{
    static void Prefix(Ideo __instance, ref bool __state)
    {
        if (WorldTickOwnerContext.Active && AsyncTime.AsyncWorldTimeComp.tickingWorld)
            __state = WorldTickOwnerContext.TryPush(WorldTickOwnerContext.IdeoFollower(__instance));
    }

    static void Finalizer(bool __state)
    {
        if (__state)
            FactionExtensions.PopFaction();
    }
}
