using Harmony;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.RegenerateEverythingNow))]
    public static class MapDrawerRegenPatch
    {
        public static Dictionary<int, MapDrawer> copyFrom = new Dictionary<int, MapDrawer>();

        static bool Prefix(MapDrawer __instance)
        {
            Map map = __instance.map;
            if (!copyFrom.TryGetValue(map.uniqueID, out MapDrawer keepDrawer)) return true;

            map.mapDrawer = keepDrawer;
            keepDrawer.map = map;

            foreach (Section section in keepDrawer.sections)
            {
                section.map = map;

                for (int i = 0; i < section.layers.Count; i++)
                {
                    SectionLayer layer = section.layers[i];

                    if (!ShouldKeep(layer))
                        section.layers[i] = (SectionLayer)Activator.CreateInstance(layer.GetType(), section);
                    else if (layer is SectionLayer_LightingOverlay lighting)
                        lighting.glowGrid = map.glowGrid.glowGrid;
                    else if (layer is SectionLayer_TerrainScatter scatter)
                        scatter.scats.Do(s => s.map = map);
                }
            }

            foreach (Section s in keepDrawer.sections)
                foreach (SectionLayer layer in s.layers)
                    if (!ShouldKeep(layer))
                        layer.Regenerate();

            copyFrom.Remove(map.uniqueID);

            return false;
        }

        static bool ShouldKeep(SectionLayer layer)
        {
            return layer.GetType().Assembly == typeof(Game).Assembly;
        }
    }

    //[HarmonyPatch(typeof(RegionAndRoomUpdater), nameof(RegionAndRoomUpdater.RebuildAllRegionsAndRooms))]
    public static class RebuildRegionsAndRoomsPatch
    {
        public static Dictionary<int, RegionGrid> copyFrom = new Dictionary<int, RegionGrid>();

        static bool Prefix(RegionAndRoomUpdater __instance)
        {
            Map map = __instance.map;
            if (!copyFrom.TryGetValue(map.uniqueID, out RegionGrid oldRegions)) return true;

            __instance.initialized = true;
            map.temperatureCache.ResetTemperatureCache();

            oldRegions.map = map; // for access to cellIndices in the iterator

            foreach (Region r in oldRegions.AllRegions_NoRebuild_InvalidAllowed)
            {
                r.cachedAreaOverlaps = null;
                r.cachedDangers.Clear();
                r.mark = 0;
                r.reachedIndex = 0;
                r.closedIndex = new uint[RegionTraverser.NumWorkers];
                r.cachedCellCount = -1;
                r.mapIndex = (sbyte)map.Index;

                if (r.door != null)
                    r.door = map.ThingReplacement(r.door);

                foreach (List<Thing> things in r.listerThings.listsByGroup.Concat(r.ListerThings.listsByDef.Values))
                    if (things != null)
                        for (int j = 0; j < things.Count; j++)
                            if (things[j] != null)
                                things[j] = map.ThingReplacement(things[j]);

                Room rm = r.Room;
                if (rm == null) continue;

                rm.mapIndex = (sbyte)map.Index;
                rm.cachedCellCount = -1;
                rm.cachedOpenRoofCount = -1;
                rm.statsAndRoleDirty = true;
                rm.stats = new DefMap<RoomStatDef, float>();
                rm.role = null;
                rm.uniqueNeighbors.Clear();
                rm.uniqueContainedThings.Clear();

                RoomGroup rg = rm.groupInt;
                rg.tempTracker.cycleIndex = 0;
            }

            for (int i = 0; i < oldRegions.regionGrid.Length; i++)
                map.regionGrid.regionGrid[i] = oldRegions.regionGrid[i];

            copyFrom.Remove(map.uniqueID);

            return false;
        }
    }

    [HarmonyPatch(typeof(WorldGrid), MethodType.Constructor)]
    public static class WorldGridCachePatch
    {
        public static WorldGrid copyFrom;

        static bool Prefix(WorldGrid __instance, int ___cachedTraversalDistance, int ___cachedTraversalDistanceForStart, int ___cachedTraversalDistanceForEnd)
        {
            if (copyFrom == null) return true;

            WorldGrid grid = __instance;

            grid.viewAngle = copyFrom.viewAngle;
            grid.viewCenter = copyFrom.viewCenter;
            grid.verts = copyFrom.verts;
            grid.tileIDToNeighbors_offsets = copyFrom.tileIDToNeighbors_offsets;
            grid.tileIDToNeighbors_values = copyFrom.tileIDToNeighbors_values;
            grid.tileIDToVerts_offsets = copyFrom.tileIDToVerts_offsets;
            grid.averageTileSize = copyFrom.averageTileSize;

            grid.tiles = new List<Tile>();
            ___cachedTraversalDistance = -1;
            ___cachedTraversalDistanceForStart = -1;
            ___cachedTraversalDistanceForEnd = -1;

            copyFrom = null;

            return false;
        }
    }

    [HarmonyPatch(typeof(WorldRenderer), MethodType.Constructor)]
    public static class WorldRendererCachePatch
    {
        public static WorldRenderer copyFrom;

        static bool Prefix(WorldRenderer __instance)
        {
            if (copyFrom == null) return true;

            __instance.layers = copyFrom.layers;
            copyFrom = null;

            return false;
        }
    }
}
