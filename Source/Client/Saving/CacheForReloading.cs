using HarmonyLib;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client
{

    [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.RegenerateEverythingNow))]
    public static class MapDrawerRegenPatch
    {
        public static Dictionary<int, MapDrawer> copyFrom = new();

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

    [HarmonyPatch(typeof(WorldGrid), MethodType.Constructor)]
    public static class WorldGridCachePatch
    {
        public static WorldGrid copyFrom;

        static bool Prefix(WorldGrid __instance, ref int ___cachedTraversalDistance, ref int ___cachedTraversalDistanceForStart, ref int ___cachedTraversalDistanceForEnd)
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

    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.ExposeData))]
    public static class WorldGridExposeDataPatch
    {
        public static WorldGrid copyFrom;

        static bool Prefix(WorldGrid __instance)
        {
            if (copyFrom == null) return true;

            WorldGrid grid = __instance;

            grid.tileBiome = copyFrom.tileBiome;
            grid.tileElevation = copyFrom.tileElevation;
            grid.tileHilliness = copyFrom.tileHilliness;
            grid.tileTemperature = copyFrom.tileTemperature;
            grid.tileRainfall = copyFrom.tileRainfall;
            grid.tileSwampiness = copyFrom.tileSwampiness;
            grid.tileFeature = copyFrom.tileFeature;
            grid.tileRoadOrigins = copyFrom.tileRoadOrigins;
            grid.tileRoadAdjacency = copyFrom.tileRoadAdjacency;
            grid.tileRoadDef = copyFrom.tileRoadDef;
            grid.tileRiverOrigins = copyFrom.tileRiverOrigins;
            grid.tileRiverAdjacency = copyFrom.tileRiverAdjacency;
            grid.tileRiverDef = copyFrom.tileRiverDef;

            // This is plain old data apart from the WorldFeature feature field which is a reference
            // It later gets reset in WorldFeatures.ExposeData though so it can be safely copied
            grid.tiles = copyFrom.tiles;

            // ExposeData runs multiple times but WorldGrid only needs LoadSaveMode.LoadingVars
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
