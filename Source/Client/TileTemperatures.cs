using Harmony;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(TileTemperaturesComp.CachedTileTemperatureData))]
    [HarmonyPatch(nameof(TileTemperaturesComp.CachedTileTemperatureData.CheckCache))]
    static class CachedTileTemperatureData_CheckCache
    {
        static void Prefix(int ___tile, ref PrevTime? __state)
        {
            if (Multiplayer.Client == null) return;

            Map map = Current.Game.FindMap(___tile);
            if (map == null) return;

            __state = PrevTime.GetAndSetToMap(map);
        }

        static void Postfix(PrevTime? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(TileTemperaturesComp), nameof(TileTemperaturesComp.RetrieveCachedData))]
    static class RetrieveCachedDataPatch
    {
        static void Prefix(TileTemperaturesComp __instance, int tile, ref bool __state)
        {
            if (Multiplayer.Client == null) return;

            var cache = __instance.cache;
            __state = cache[tile] == null;

            if (!Multiplayer.ShouldSync && cache[tile]?.twelfthlyTempAverages.Length == 13)
            {
                cache[tile] = null;
                __instance.usedSlots.Remove(tile);
            }
        }

        static void Postfix(TileTemperaturesComp __instance, int tile, bool __state)
        {
            if (__state && Multiplayer.ShouldSync)
            {
                Array.Resize(ref __instance.cache[tile].twelfthlyTempAverages, 13);
            }
        }
    }
}
