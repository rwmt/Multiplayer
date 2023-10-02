using RimWorld;
using Verse;

namespace Multiplayer.Client.Factions;

public static class FactionExtensions
{
    // Sets the current Faction.OfPlayer
    // Applies faction's world components
    // Applies faction's map components if map not null
    public static void PushFaction(this Map map, Faction f)
    {
        map?.MpComp()?.CheckInvariant();

        var faction = FactionContext.Push(f);
        if (faction == null) return;

        Multiplayer.WorldComp?.SetFaction(faction);
        map?.MpComp().SetFaction(faction);
    }

    public static void PushFaction(this Map map, int factionId)
    {
        Faction faction = Find.FactionManager.GetById(factionId);
        map.PushFaction(faction);
    }

    public static void PopFaction()
    {
        PopFaction(null);
    }

    public static Faction PopFaction(this Map map)
    {
        Faction faction = FactionContext.Pop();
        if (faction == null) return null;

        Multiplayer.WorldComp?.SetFaction(faction);
        map?.MpComp().SetFaction(faction);

        return faction;
    }
}
