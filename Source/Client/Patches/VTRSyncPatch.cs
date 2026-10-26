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

            // Keep the synchronized update rate until animation timing can be
            // brought back in line with the vanilla value.
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
        private static readonly ViewedMapTracker viewedMap = new();
        public static int lastMovedToMapId => viewedMap.LastReportedMapId;
        public static bool Enabled { get; private set; }

        // Vtr rates
        public const int MaximumVtr = 15;
        public const int MinimumVtr = 1;

        public static int GetSynchronizedUpdateRate(Thing thing) => thing?.MapHeld?.AsyncTime()?.VTR ?? MaximumVtr;

        public static void Start()
        {
            Enabled = true;
            SendViewedMapUpdate(WorldRendererUtility.WorldSelected ? WorldMapId : Find.CurrentMap?.uniqueID ?? InvalidMapId);
        }

        public static void Suspend() => Enabled = false;

        public static void SendViewedMapUpdate(int current)
        {
            if (!Enabled || Multiplayer.reloading || Multiplayer.Client is not { State: ConnectionStateEnum.ClientPlaying }) return;

            // Reloading can change the local view without reporting it. Use the
            // last sent map as the previous map so the server sees a valid chain.
            viewedMap.Report(current, (previous, next) =>
            {
                MpLog.Debug($"VTR MapSwitchPatch: {previous}->{next} @ tick {Find.TickManager?.TicksGame ?? 0}");
                Multiplayer.Client.SendCommand(CommandType.PlayerCount, ScheduledCommand.Global, ByteWriter.GetBytes(previous, next));
            });
        }

        public static void Reset()
        {
            Enabled = false;
            viewedMap.Reset();
            WorldRenderModePatch.Reset();
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.CurrentMap), MethodType.Setter)]
    static class MapSwitchPatch
    {
        static void Prefix(Map value)
        {
            if (Multiplayer.Client == null || !VTRSync.Enabled) return;

            try
            {
                // WorldRenderModePatch will handle it
                if (VTRSync.lastMovedToMapId == VTRSync.WorldMapId) return;
                int newMap = value?.uniqueID ?? VTRSync.InvalidMapId;
                VTRSync.SendViewedMapUpdate(newMap);
            }
            catch (Exception ex)
            {
                MpLog.Error($"VTR MapSwitchPatch error: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(WorldRendererUtility), nameof(WorldRendererUtility.CurrentWorldRenderMode), MethodType.Getter)]
    static class WorldRenderModePatch
    {
        private static WorldRenderMode lastRenderMode = WorldRenderMode.None;

        public static void Reset() => lastRenderMode = WorldRenderMode.None;

        static void Postfix(WorldRenderMode __result)
        {
            if (Multiplayer.Client == null || !VTRSync.Enabled || Multiplayer.reloading) return;

            try
            {
                // Detect transition to world map (Planet mode)
                if (__result == WorldRenderMode.Planet && lastRenderMode != WorldRenderMode.Planet)
                {
                    var leavingMap = VTRSync.lastMovedToMapId >= 0;
                    VTRSync.SendViewedMapUpdate(VTRSync.WorldMapId);

                    // On standalone with streaming, trigger a join point when leaving a map
                    // so each player can save independently without disturbing others
                    if (leavingMap && Multiplayer.session?.ConnectedToStandaloneServer == true && Multiplayer.GameComp.multifaction && Multiplayer.GameComp.asyncTime)
                        Autosaving.SendAutosavingRequest(JoinPointRequestReason.WorldTravel);
                }
                // Detect transition back to tile map
                else if (__result != WorldRenderMode.Planet && lastRenderMode == WorldRenderMode.Planet)
                {
                    var newMap = Find.CurrentMap?.uniqueID ?? VTRSync.InvalidMapId;
                    if (newMap != VTRSync.InvalidMapId && VTRSync.lastMovedToMapId == VTRSync.WorldMapId)
                    {
                        VTRSync.SendViewedMapUpdate(newMap);
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
