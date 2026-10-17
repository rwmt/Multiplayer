using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Factions;

public static class FactionExtensions
{
    // Sets the current Faction.OfPlayer
    // Applies faction's world components
    // Applies faction's map components if map not null
    //
    // force: the faction is applied even if it already is Faction.OfPlayer, and to every map, not only the given one.
    // Forced pushes set up simulation contexts (map/world ticks and commands). Outside of them every map holds the
    // data of the local client's own faction, so applying the context's faction to all maps makes sure simulation
    // code touching another map (portals, pocket maps, gravships, ...) never sees client-specific data.
    // The matching pop re-applies the previous faction to the world and every map.
    public static void PushFaction(this Map map, Faction f, bool force = false)
    {
        var faction = FactionContext.Push(f, force, allMaps: force);
        if (faction == null) return;

        Multiplayer.WorldComp?.SetFaction(faction);
        if (force)
            SetFactionOnAllMaps(faction);
        else
            map?.MpComp().SetFaction(faction);
    }

    public static void PushFaction(this Map map, int factionId)
    {
        Faction faction = Find.FactionManager.GetById(factionId);
        map.PushFaction(faction);
    }

    public static Faction PopFaction() => PopFaction(null);

    public static Faction PopFaction(this Map map)
    {
        Faction faction = FactionContext.Pop(out bool allMaps);
        if (faction == null) return null;

        Multiplayer.WorldComp?.SetFaction(faction);
        if (allMaps)
            SetFactionOnAllMaps(faction);
        else
            map?.MpComp().SetFaction(faction);

        return faction;
    }

    // Pops a push made outside of ticks and commands and re-applies the local player's faction everywhere (the resting
    // state there) instead of the faction saved by the push, as the code in between may have changed the local faction
    public static void PopToLocalFaction()
    {
        FactionContext.Pop();

        var faction = Multiplayer.RealPlayerFaction;
        if (faction == null) return;

        FactionContext.Set(faction);
        Multiplayer.WorldComp?.SetFaction(faction);
        SetFactionOnAllMaps(faction);
    }

    private static void SetFactionOnAllMaps(Faction faction)
    {
        // Includes comps of removed maps (they're never removed from the list), which is harmless
        var comps = Multiplayer.game?.mapComps;
        if (comps == null) return;

        for (int i = 0; i < comps.Count; i++)
            comps[i].SetFaction(faction);
    }

    public static bool TryGetPlayerFaction(this Quest quest, out Faction faction)
    {
        faction = quest.QuestLookTargets
            .Where(t => t.HasWorldObject && t.WorldObject is Settlement)
            .Select(t => ((Settlement)t.WorldObject).Faction)
            .FirstOrDefault(f => f != null);

        return faction != null;
    }
}
