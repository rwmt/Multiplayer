using System.Collections.Generic;

namespace Multiplayer.Client
{
    /// <summary>
    /// Holds the random states for a given map, and its map id
    /// </summary>
    public class MapRandomStateData
    {
        public int mapId;
        public List<uint> randomStates = new List<uint>();

        public MapRandomStateData(int mapId)
        {
            this.mapId = mapId;
        }
    }
}