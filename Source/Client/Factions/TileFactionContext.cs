using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;

namespace Multiplayer.Client.Factions
{
    public static class TileFactionContext
    {
        private static readonly Dictionary<PlanetTile, Faction> tileFactions = [];
        private static readonly object lockObject = new();

        public static void SetFactionForTile(PlanetTile tile, Faction faction)
        {
            lock (lockObject)
            {
                tileFactions[tile] = faction;
            }
        }

        public static Faction GetFactionForTile(PlanetTile tile)
        {
            lock (lockObject)
            {
                return tileFactions.TryGetValue(tile, out Faction faction) ? faction : null;
            }
        }

        public static void ClearTile(PlanetTile tile)
        {
            lock (lockObject)
            {
                tileFactions.Remove(tile);
            }
        }
    }
} 
