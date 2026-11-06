using RimWorld;
using Verse;

namespace Multiplayer.Client.Persistent
{
    public class GravshipCache
    {
        public Map parent;
        public Building_GravEngine cachedGravEngine;
        public int lastCachedEngineTick;
        public GravshipCache(Map map = null)
        {
            parent = map;
        }
        public void Apply()
        {
            if (cachedGravEngine != null)
            {
                GravshipUtility.lastCachedEngineTick = lastCachedEngineTick;
                GravshipUtility.cachedGravEngine = cachedGravEngine;
                GravshipUtility.lastCachedEngineMapID = parent.uniqueID;
            }
            else
            {
                //comment out so default fall to world status
                GravshipUtility.lastCachedEngineTick = -1;
                GravshipUtility.cachedGravEngine = null;
                GravshipUtility.lastCachedEngineMapID = -1;
            }
        }
    }
}
