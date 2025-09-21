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

            if (__instance is Gravship or TravellingTransporters)
                __result = VTRSync.MinimumVtr;
            else
                __result = VTRSync.MaximumVtr;

            return false;
        }
    }

    static class VTRSync
    {
        // Special identifier for world map (since it doesn't have a uniqueID like regular maps)
        public const int WorldMapId = -2;
        public const int InvalidMapIndex = -1;
        public static int lastMovedToMap = InvalidMapIndex;
        public static int lastSentAtTick = -1;

        // Vtr rates
        public const int MaximumVtr = 15;
        public const int MinimumVtr = 1;

        public static int GetSynchronizedUpdateRate(Thing thing) => thing?.MapHeld?.AsyncTime()?.VTR ?? MaximumVtr;

        public static void SendViewedMapUpdate(int previous, int current)
        {
            string warn = string.Empty;
            if (previous != lastMovedToMap)
                warn = $" mismatch between expected previous map {previous} and last moved to map {lastMovedToMap}";
            else if (previous == current) return;
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            MpLog.Debug($"VTR MapSwitchPatch: {lastMovedToMap}->{current} @ tick {currentTick}{warn}");
            Multiplayer.Client.SendCommand(CommandType.PlayerCount, ScheduledCommand.Global, ByteWriter.GetBytes(previous, current));
            lastMovedToMap = current;
        }

        public static void Reset()
        {
            lastMovedToMap = InvalidMapIndex;
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
                if (VTRSync.lastMovedToMap == VTRSync.WorldMapId) return;
                int previousMap = GetPreviousMapIndex();
                int newMap = value?.uniqueID ?? VTRSync.InvalidMapIndex;
                int currentTick = Find.TickManager?.TicksGame ?? 0;

                if (previousMap == newMap) return;
                if (VTRSync.lastMovedToMap == newMap && currentTick == VTRSync.lastSentAtTick) return;

                VTRSync.SendViewedMapUpdate(previousMap, newMap);
                VTRSync.lastSentAtTick = currentTick;
            }
            catch (Exception ex)
            {
                MpLog.Error($"VTR MapSwitchPatch error: {ex}");
            }
        }

        private static int GetPreviousMapIndex()
        {
            bool currentMapIsRemovedAndWasLatestMap = Current.Game.currentMapIndex >= Find.Maps.Count;

            if (currentMapIsRemovedAndWasLatestMap)
            {
                return VTRSync.InvalidMapIndex;
            }

            return Find.CurrentMap?.uniqueID ?? VTRSync.InvalidMapIndex;
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
                    if (VTRSync.lastMovedToMap != VTRSync.InvalidMapIndex && VTRSync.lastMovedToMap != VTRSync.WorldMapId)
                    {
                        VTRSync.SendViewedMapUpdate(VTRSync.lastMovedToMap, VTRSync.WorldMapId);
                    }
                }
                // Detect transition back to tile map
                else if (__result != WorldRenderMode.Planet && lastRenderMode == WorldRenderMode.Planet)
                {
                    var newMap = Find.CurrentMap?.uniqueID ?? VTRSync.InvalidMapIndex;
                    if (newMap != VTRSync.InvalidMapIndex && VTRSync.lastMovedToMap == VTRSync.WorldMapId)
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
