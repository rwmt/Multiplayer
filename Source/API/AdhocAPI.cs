using RimWorld;

namespace Multiplayer.API;

public static class AdhocAPI
{
    public static Faction RealPlayerFaction => Client.Multiplayer.RealPlayerFaction;
}
