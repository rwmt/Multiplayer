using HarmonyLib;
using Multiplayer.Client.Patches;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld.Planet;
using System;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(GenTicks), nameof(GenTicks.GetCameraUpdateRate))]
    public static class VTRSyncPatch
    {
        static bool Prefix(Thing thing, ref int __result)
        {
            if (Multiplayer.Client == null)
                return true;

            __result = GetSynchronizedUpdateRate(thing);
            return false;
        }

        private static int GetSynchronizedUpdateRate(Thing thing) => thing?.MapHeld?.AsyncTime()?.VTR ?? 15;
    }

    static class VTRSync
    {
        public static int lastMovedToMap = -1;
        public static int lastSentTick = -1;
        
        // Special identifier for world map (since it doesn't have a uniqueID like regular maps)
        public const int WorldMapId = -2;
    }

    [HarmonyPatch(typeof(Game), nameof(Game.CurrentMap), MethodType.Setter)]
    static class MapSwitchPatch
    {
        static void Prefix(Map value)
        {
            if (Multiplayer.Client == null || Client.Multiplayer.session == null) return;

            try
            {
                int previousMap = Find.CurrentMap?.uniqueID ?? -1;
                int newMap = value?.uniqueID ?? -1;
                int currentTick = Find.TickManager?.TicksGame ?? 0;

                // If no change in map, do nothing
                if (previousMap == newMap)
                    return;

                // Prevent duplicate commands for the same transition, but allow retry after a tick
                if (VTRSync.lastMovedToMap == previousMap && currentTick == VTRSync.lastSentTick)
                    return;

                // Send map change command to server
                // Send as global command since it affects multiple maps
                Multiplayer.Client.SendCommand(CommandType.PlayerCount, ScheduledCommand.Global, ByteWriter.GetBytes(previousMap, newMap));

                // Track this command to prevent duplicates
                VTRSync.lastMovedToMap = newMap;
                VTRSync.lastSentTick = currentTick;
            }
            catch (Exception ex)
            {
                MpLog.Error($"VTR MapSwitchPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(WorldRendererUtility), nameof(WorldRendererUtility.CurrentWorldRenderMode), MethodType.Getter)]
    static class WorldRenderModePatch
    {
        private static WorldRenderMode lastRenderMode = WorldRenderMode.None;

        static void Postfix(WorldRenderMode __result)
        {
            if (Multiplayer.Client == null) return;

            try
            {
                // Detect transition to world map (Planet mode)
                if (__result == WorldRenderMode.Planet && lastRenderMode != WorldRenderMode.Planet)
                {
                    if (VTRSync.lastMovedToMap != -1)
                    {
                        Multiplayer.Client.SendCommand(CommandType.PlayerCount, ScheduledCommand.Global, ByteWriter.GetBytes(VTRSync.lastMovedToMap, VTRSync.WorldMapId));
                    }
                }
                // Detect transition away from world map
                else if (__result != WorldRenderMode.Planet && lastRenderMode == WorldRenderMode.Planet)
                {
                    int currentMapId = Find.CurrentMap?.uniqueID ?? -1;
                    Multiplayer.Client.SendCommand(CommandType.PlayerCount, ScheduledCommand.Global, ByteWriter.GetBytes(VTRSync.WorldMapId, currentMapId));
                }

                lastRenderMode = __result;
            }
            catch (Exception ex)
            {
                MpLog.Error($"WorldRenderModePatch error: {ex.Message}");
            }
        }
    }
}
