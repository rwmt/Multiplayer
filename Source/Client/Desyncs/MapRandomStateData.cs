using System.Collections.Generic;

namespace Multiplayer.Client
{
    /// <summary>
    /// Holds the random states for a given map, and its map id
    /// </summary>
    public class MapRandomStateData(int mapId)
    {
        public int mapId = mapId;
        public List<uint> randomStates = new();
    }
}
