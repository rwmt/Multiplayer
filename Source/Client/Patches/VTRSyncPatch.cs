using System;
using HarmonyLib;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld.Planet;
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

            // TODO: Put this back to the original value
            // Probably need to sync up all the animations before doing this
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

            __result = Multiplayer.AsyncWorldTime.VTR;
            return false;
        }
    }

    static class VTRSync
    {
        // Special identifier for the world map (since it doesn't have a uniqueID like regular maps)
        public const int WorldMapId = -2;
        public const int InvalidMapId = -1;
        public static int lastMovedToMapId = InvalidMapId;
        public static int lastSentAtTick = -1;

        // Vtr rates
        public const int MaximumVtr = 15;
        public const int MinimumVtr = 1;

        public static int GetSynchronizedUpdateRate(Thing thing) => thing?.MapHeld?.AsyncTime()?.VTR ?? MaximumVtr;

        public static void SendViewedMapUpdate(int previous, int current)
        {
            string warn = string.Empty;
            if (previous != lastMovedToMapId)
                warn = $" mismatch between expected previous map {previous} and last moved to map {lastMovedToMapId}";
            else if (previous == current) return;
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            MpLog.Debug($"VTR MapSwitchPatch: {lastMovedToMapId}->{current} @ tick {currentTick}{warn}");
            Multiplayer.Client.SendCommand(CommandType.PlayerCount, ScheduledCommand.Global, ByteWriter.GetBytes(previous, current));
            lastMovedToMapId = current;
        }

        public static void Reset()
        {
            lastMovedToMapId = InvalidMapId;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.CurrentMap), MethodType.Setter)]
    static class MapSwitchPatch
    {
        static void Prefix(Map value)
        {
            if (Multiplayer.Client == null) return;

            try
            {
                // WorldRenderModePatch will handle it
                if (VTRSync.lastMovedToMapId == VTRSync.WorldMapId) return;
                int previousMap = GetPreviousMapId();
                int newMap = value?.uniqueID ?? VTRSync.InvalidMapId;
                int currentTick = Find.TickManager?.TicksGame ?? 0;

                if (previousMap == newMap) return;
                if (VTRSync.lastMovedToMapId == newMap && currentTick == VTRSync.lastSentAtTick) return;

                VTRSync.SendViewedMapUpdate(previousMap, newMap);
                VTRSync.lastSentAtTick = currentTick;
            }
            catch (Exception ex)
            {
                MpLog.Error($"VTR MapSwitchPatch error: {ex}");
            }
        }

        private static int GetPreviousMapId()
        {
            bool currentMapIsRemovedAndWasLatestMap = Current.Game.currentMapIndex >= Find.Maps.Count;

            if (currentMapIsRemovedAndWasLatestMap)
            {
                return VTRSync.InvalidMapId;
            }

            return Find.CurrentMap?.uniqueID ?? VTRSync.InvalidMapId;
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
                    if (VTRSync.lastMovedToMapId != VTRSync.InvalidMapId && VTRSync.lastMovedToMapId != VTRSync.WorldMapId)
                    {
                        VTRSync.SendViewedMapUpdate(VTRSync.lastMovedToMapId, VTRSync.WorldMapId);
                    }
                }
                // Detect transition back to tile map
                else if (__result != WorldRenderMode.Planet && lastRenderMode == WorldRenderMode.Planet)
                {
                    var newMap = Find.CurrentMap?.uniqueID ?? VTRSync.InvalidMapId;
                    if (newMap != VTRSync.InvalidMapId && VTRSync.lastMovedToMapId == VTRSync.WorldMapId)
                    {
                        VTRSync.SendViewedMapUpdate(VTRSync.WorldMapId, newMap);
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
