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
        public static int lastMovedToMapId = InvalidMapId;

        // Vtr rates
        public const int MaximumVtr = 15;
        public const int MinimumVtr = 1;

        public static int GetSynchronizedUpdateRate(Thing thing) => thing?.MapHeld?.AsyncTime()?.VTR ?? MaximumVtr;

        public static int CurrentViewId() =>
            WorldRendererUtility.WorldSelected ? WorldMapId : Find.CurrentMap?.uniqueID ?? InvalidMapId;

        // Called every frame. The counts follow what the player looks at however the view changed: catching only
        // the CurrentMap setter and world map toggles missed views that never went through them. A multifaction
        // joiner starts on the world without a current map, command execution changes the map index directly,
        // and hosting creates the game with a save and reload (during which nothing is reported).
        public static void UpdateView()
        {
            if (Multiplayer.arbiterInstance || Multiplayer.reloading || LongEventHandler.AnyEventNowOrWaiting)
                return;

            if (Multiplayer.session.client.State != ConnectionStateEnum.ClientPlaying)
                return;

            int view = CurrentViewId();
            if (view != lastMovedToMapId)
                SendViewedMapUpdate(lastMovedToMapId, view);
        }

        // The host reports its view as soon as its server runs
        public static void AnnounceCurrentView() => SendViewedMapUpdate(lastMovedToMapId, CurrentViewId());

        public static void ResetAllPlayerCounts()
        {
            Multiplayer.AsyncWorldTime.ResetPlayerCount();
            foreach (var map in Find.Maps)
                map.AsyncTime()?.ResetPlayerCount();
        }

        // previous must be what the server last got from this client: the server and every client apply the
        // transition to the synced counts
        public static void SendViewedMapUpdate(int previous, int current)
        {
            // Join point reloads keep the saved counts, the view doesn't change
            if (Multiplayer.reloading)
                return;

            // The arbiter doesn't view anything; counting it kept its map at the full rate for everyone
            if (Multiplayer.arbiterInstance)
                return;

            if (previous == current) return;

            MpLog.Debug($"VTR view: {previous}->{current} @ tick {Find.TickManager?.TicksGame ?? 0}");
            Multiplayer.Client.SendCommand(CommandType.PlayerCount, ScheduledCommand.Global, ByteWriter.GetBytes(previous, current));
            lastMovedToMapId = current;
        }

        // A new connection starts unknown to the server (which counts its own record of the player's view)
        public static void Reset()
        {
            lastMovedToMapId = InvalidMapId;
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
                // Detect transition to world map (Planet mode). Viewer counts are updated by VTRSync.UpdateView.
                if (__result == WorldRenderMode.Planet && lastRenderMode != WorldRenderMode.Planet)
                {
                    // On standalone with streaming, trigger a join point when leaving a map
                    // so each player can save independently without disturbing others
                    if (Multiplayer.session?.ConnectedToStandaloneServer == true && Multiplayer.GameComp.multifaction && Multiplayer.GameComp.asyncTime)
                        Autosaving.SendAutosavingRequest(JoinPointRequestReason.WorldTravel, TickPatch.Timer);
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
