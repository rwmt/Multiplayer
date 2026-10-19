using System.Collections.Generic;
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
    // The matching pop restores exactly the factions the world and each map had before the push.
    public static void PushFaction(this Map map, Faction f, bool force = false)
    {
        var faction = FactionContext.Push(f, force);
        if (faction == null) return;

        if (force)
            FactionContext.SetTopState(AppliedFactions.Capture());
        else if (map != null)
            FactionContext.SetTopState(map); // Lets an unwind (FactionContext.RestoreStack) restore the map too

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
        Faction faction = FactionContext.Pop(out var state);
        if (faction == null) return null;

        if (state is AppliedFactions applied)
        {
            applied.Restore(faction);
            return faction;
        }

        Multiplayer.WorldComp?.SetFaction(faction);
        (map ?? state as Map)?.MpComp()?.SetFaction(faction);

        return faction;
    }

    private static void SetFactionOnAllMaps(Faction faction)
    {
        var comps = Multiplayer.game?.mapComps;
        if (comps == null) return;

        for (int i = 0; i < comps.Count; i++)
            if (IsLive(comps[i]))
                comps[i].SetFaction(faction);
    }

    // Comps of removed maps are never removed from the list
    private static bool IsLive(MultiplayerMapComp comp) => comp.map is { Disposed: false };

    // The factions whose data the world and the maps held before a forced push
    private class AppliedFactions
    {
        private int worldFactionId;
        private List<MultiplayerMapComp> comps;
        private int[] mapFactionIds;

        public static AppliedFactions Capture()
        {
            var applied = new AppliedFactions
            {
                worldFactionId = Multiplayer.WorldComp?.GetAppliedFactionId() ?? -1,
                comps = Multiplayer.game?.mapComps
            };

            var count = applied.comps?.Count ?? 0;
            applied.mapFactionIds = new int[count];
            for (int i = 0; i < count; i++)
                applied.mapFactionIds[i] = IsLive(applied.comps[i]) ? applied.comps[i].GetAppliedFactionId() : -1;

            return applied;
        }

        // Maps without a known previous faction (e.g. generated in between) get the popped faction
        public void Restore(Faction popped)
        {
            var worldComp = Multiplayer.WorldComp;
            if (worldComp != null && (worldFactionId == -1 || !worldComp.SetFaction(worldFactionId)))
                worldComp.SetFaction(popped);

            var currentComps = Multiplayer.game?.mapComps;
            if (currentComps == null) return;

            for (int i = 0; i < currentComps.Count; i++)
            {
                var comp = currentComps[i];
                if (!IsLive(comp)) continue;

                var id = currentComps == comps && i < mapFactionIds.Length ? mapFactionIds[i] : -1;
                if (id == -1 || !comp.SetFaction(id))
                    comp.SetFaction(popped);
            }
        }
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
