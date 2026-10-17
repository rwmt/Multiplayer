using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(TileTemperaturesComp.CachedTileTemperatureData))]
    [HarmonyPatch(nameof(TileTemperaturesComp.CachedTileTemperatureData.CheckCache))]
    static class CachedTileTemperatureData_CheckCache
    {
        static void Prefix(PlanetTile ___tile, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;

            Map map = Current.Game.FindMap(___tile);
            if (map == null) return;

            __state = TimeSnapshot.GetAndSetFromMap(map);
        }

        static void Postfix(TimeSnapshot? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(TileTemperaturesComp), nameof(TileTemperaturesComp.RetrieveCachedData))]
    static class RetrieveCachedData_Patch
    {
        private static int uiTickedAtFrame = -1;

        static bool Prefix(TileTemperaturesComp __instance, PlanetTile tile, ref TileTemperaturesComp.CachedTileTemperatureData __result)
        {
            if (Multiplayer.InInterface && __instance != Multiplayer.WorldComp.uiTemperatures)
            {
                var uiTemperatures = Multiplayer.WorldComp.uiTemperatures;

                // The UI cache isn't a real world component, so vanilla never ticks it and its entries would never
                // refresh or get evicted. Tick it (at most once per frame) while the interface uses it.
                if (uiTickedAtFrame != Time.frameCount)
                {
                    uiTickedAtFrame = Time.frameCount;

                    try
                    {
                        uiTemperatures.WorldComponentTick();
                    }
                    catch (Exception e)
                    {
                        Log.ErrorOnce($"MP: Exception ticking UI tile temperatures: {e}", 846582641);
                    }
                }

                __result = uiTemperatures.RetrieveCachedData(tile);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(GenTemperature), nameof(GenTemperature.AverageTemperatureAtTileForTwelfth))]
    static class CacheAverageTileTemperature
    {
        static Dictionary<PlanetTile, float[]> averageTileTemps = new Dictionary<PlanetTile, float[]>();

        static bool Prefix(PlanetTile tile, Twelfth twelfth)
        {
            return !averageTileTemps.TryGetValue(tile, out float[] arr) || float.IsNaN(arr[(int)twelfth]);
        }

        static void Postfix(PlanetTile tile, Twelfth twelfth, ref float __result)
        {
            if (averageTileTemps.TryGetValue(tile, out float[] arr) && !float.IsNaN(arr[(int)twelfth]))
            {
                __result = arr[(int)twelfth];
                return;
            }

            if (arr == null)
                averageTileTemps[tile] = Enumerable.Repeat(float.NaN, 12).ToArray();

            averageTileTemps[tile][(int)twelfth] = __result;
        }

        public static void Clear()
        {
            averageTileTemps.Clear();
        }
    }

    [HarmonyPatch]
    static class ClearTemperatureCache
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PlanetLayer), nameof(PlanetLayer.RawDataToTiles));
            yield return AccessTools.Method(typeof(WorldGenStep_Terrain), nameof(WorldGenStep_Terrain.GenerateFresh));
        }

        static void Postfix() => CacheAverageTileTemperature.Clear();
    }

}
