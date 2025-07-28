using HarmonyLib;
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

            __result = VTRSync.GetSynchronizedUpdateRate(thing);
            return false;
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.UpdateRateTicks), MethodType.Getter)]
    public static class VtrSyncProjectilePatch
    {
        static bool Prefix(ref int __result, Projectile __instance)
        {
            if (Multiplayer.Client == null)
                return true;

            __result = __instance.Spawned ? VTRSync.GetSynchronizedUpdateRate(__instance) : VTRSync.MaximumVtr;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.UpdateRateTicks), MethodType.Getter)]
    public static class VtrSyncWorldObjectPatch
    {
        static bool Prefix(ref int __result, WorldObject __instance)
        {
            if (Multiplayer.Client == null)
                return true;

            __result = VTRSync.MaximumVtr;
            return false;
        }
    }

    static class VTRSync
    {
        // Special identifier for world map (since it doesn't have a uniqueID like regular maps)
        public const int WorldMapId = -2;
        public static int lastMovedToMap = -1;
        public static int lastSentTick = -1;

        // Vtr rates
        public const int MaximumVtr = 15;
        public const int MinimumVtr = 1;

        public static int GetSynchronizedUpdateRate(Thing thing) => thing?.MapHeld?.AsyncTime()?.VTR ?? VTRSync.MaximumVtr;
    }

    [HarmonyPatch(typeof(Game), nameof(Game.CurrentMap), MethodType.Setter)]
    static class MapSwitchPatch
    {
        const int InvalidMapIndex = -1;

        static void Prefix(Map value)
        {
            if (Multiplayer.Client == null || Client.Multiplayer.session == null) return;

            try
            {
                int previousMap = GetPreviousMapIndex();
                int newMap = value?.uniqueID ?? InvalidMapIndex;
                int currentTick = Find.TickManager?.TicksGame ?? 0;

                // If no change in map, do nothing
                if (previousMap == newMap)
                    return;

                // Prevent duplicate commands for the same transition, but allow retry after a tick
                if (VTRSync.lastMovedToMap == newMap && currentTick == VTRSync.lastSentTick)
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

        private static int GetPreviousMapIndex()
        {
            bool currentMapIsRemovedAndWasLatestMap = Current.Game.currentMapIndex >= Find.Maps.Count;

            if (currentMapIsRemovedAndWasLatestMap)
            {
                return InvalidMapIndex;
            }

            return Find.CurrentMap?.uniqueID ?? InvalidMapIndex;
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

                lastRenderMode = __result;
            }
            catch (Exception ex)
            {
                MpLog.Error($"WorldRenderModePatch error: {ex.Message}");
            }
        }
    }
}
