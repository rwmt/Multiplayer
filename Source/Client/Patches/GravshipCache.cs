using HarmonyLib;
using Multiplayer.Client.Util;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client.Persistent
{
    [HarmonyPatch(typeof(GravshipUtility), nameof(GravshipUtility.GetPlayerGravEngine_NewTemp))]
    public static class PatchGravshipUtilityGetPlayerGravEngine_NewTemp
    {
        static bool Prefix(Map map, Building_GravEngine __result, Building_GravEngine __state)
        {
            if (!ModsConfig.OdysseyActive || Faction.OfPlayer.loadID < 0)
            {
                return false;
            }
            GravshipCache cache = map.MpComp().factionData.GetValueOrDefault(Faction.OfPlayer.loadID).gravshipCache;
            if (cache != null)
            {
                Building_GravEngine cachedGravEngine = __state = cache.cachedGravEngine;

                // uptime cache
                if (Find.TickManager.TicksGame == cache.lastCachedEngineTick)
                {
                    __result = cachedGravEngine;
                    return false;
                }
                // no cache, default to search for one
                if (cachedGravEngine == null)
                {
                    //MpLog.Debug($"Allowed searching for gravengine of {Faction.OfPlayer} at {map.GetUniqueLoadID()} tick.{Find.TickManager.TicksGame}");
                    return true;
                }
                    // cached but old, validate cache available 
                    if (cachedGravEngine.Map == map && !cachedGravEngine.Destroyed)
                {
                    return false;
                }
            }
            return true;
        }
        static void Finalizer(Building_GravEngine __result, Building_GravEngine __state, Map map)
        {
            if (Faction.OfPlayer.loadID > 0)
            {
                GravshipCache cache = map.MpComp().factionData.GetValueOrDefault(Faction.OfPlayer.loadID).gravshipCache;
                if(cache != null)
                {
                    // if engine updated
                    if (__result != __state)
                        cache.cachedGravEngine = __result;

                    cache.lastCachedEngineTick = Find.TickManager.TicksGame;
                }
            }

        }
    }
}
