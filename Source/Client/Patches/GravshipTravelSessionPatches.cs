using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Persistent;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.EnterpriseServices;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;
using Verse;
using Verse.Sound;
using System.Diagnostics;
using System.Collections.Generic;

namespace Multiplayer.Client.Patches
{
    // Prelaunch & confirmation
    [HarmonyPatch(typeof(GravshipUtility), nameof(GravshipUtility.PreLaunchConfirmation))]
    public static class Patch_GravshipPreLaunchConfirmation
    {
        private static string GravshipDialogPrefix => "ConfirmGravEngineLaunch".Translate().RawText;

        static bool Prefix(Building_GravEngine engine, ref Action launchAction)
        {
            if (Multiplayer.Client == null) return true;
            if (GravshipTravelSessionUtils.GetSession(engine.Map.Tile) != null)
            {
                // If a session already exists, we don't need to show the dialog again
                MpLog.Debug($"[MP] [{Multiplayer.AsyncWorldTime.worldTicks}] Patch_GravshipPreLaunchConfirmation: Session already exists for tile {engine.Map.Tile}, skipping dialog.");
                return true;
            }
            MpLog.Debug($"[MP] [{Multiplayer.AsyncWorldTime.worldTicks}] Patch_GravshipPreLaunchConfirmation: Creating gravship travel session for tile {engine.Map.Tile}.");
            GravshipTravelSessionUtils.CreateGravshipTravelSession(engine.Map);

            launchAction = () =>
            {
                SyncGravshipDialogOK(engine);
            };

            return true;
        }

        [SyncMethod]
        public static void SyncGravshipDialogOK(Building_GravEngine engine)
        {
            CloseGravshipDialog();

            CompPilotConsole pilotConsole = engine.GravshipComponents.OfType<CompPilotConsole>().FirstOrDefault();
            if (pilotConsole == null)
            {
                MpLog.Error($"[MP] No pilot console found for gravship engine {engine.def.defName}");
                return;
            }

            pilotConsole?.StartChoosingDestination();
        }

        public static void CloseGravshipDialog()
        {
            Dialog_MessageBox dialog = Find.WindowStack.Windows
                .OfType<Dialog_MessageBox>()
                .FirstOrDefault(w => w.text.RawText.StartsWith(GravshipDialogPrefix));
            dialog?.Close();
        }
    }

    // Dialog cancel
    [HarmonyPatch(typeof(Dialog_MessageBox), MethodType.Constructor,
        [typeof(TaggedString), typeof(string), typeof(Action), typeof(string), typeof(Action),
         typeof(string), typeof(bool), typeof(Action), typeof(Action), typeof(WindowLayer)])]
    public static class Patch_GravshipPreLaunchCancel
    {
        private static string GravshipDialogPrefix => "ConfirmGravEngineLaunch".Translate().RawText;
        static void Postfix(Dialog_MessageBox __instance)
        {
            if (Multiplayer.Client == null) return;
            if (__instance.text.RawText.StartsWith(GravshipDialogPrefix))
            {
                __instance.buttonBAction = () =>
                {
                    GravshipTravelSessionUtils.CloseSession(Find.CurrentMap.Tile);
                    SyncGravshipDialogCancel();
                };
            }
        }

        [SyncMethod]
        public static void SyncGravshipDialogCancel()
        {
            MpLog.Debug($"[MP] [{Multiplayer.AsyncWorldTime.worldTicks}] Patch_GravshipPreLaunchCancel: Cancelling gravship launch.");
            Patch_GravshipPreLaunchConfirmation.CloseGravshipDialog();
        }
    }

    // Patch tile selection confirmation
    [HarmonyPatch(typeof(SettlementProximityGoodwillUtility), nameof(SettlementProximityGoodwillUtility.CheckConfirmSettle))]
    public static class Patch_SettlementProximityGoodwillUtility_CheckConfirmSettle
    {
        static bool Prefix(PlanetTile tile, ref Action settleAction, Action cancelAction = null, Building_GravEngine gravEngine = null)
        {
            if (Multiplayer.Client == null) return true;
            if (gravEngine == null) return true;

            settleAction = () => SyncGravshipTileConfirm(gravEngine, tile);
            return true;
        }

        [SyncMethod]
        public static void SyncGravshipTileConfirm(Building_GravEngine engine, PlanetTile planetTile)
        {
            MpLog.Debug($"[MP] [{Multiplayer.AsyncWorldTime.worldTicks}] Patch_SettlementProximityGoodwillUtility_CheckConfirmSettle: Confirming settlement for tile {planetTile} with gravship engine {engine.def.defName}.");
            // Run the same logic as the original confirmation delegate
            WorldComponent_GravshipController.DestroyTreesAroundSubstructure(engine.Map, engine.ValidSubstructure);
            Find.World.renderer.wantedMode = WorldRenderMode.None;
            engine.ConsumeFuel(planetTile);
            Find.GravshipController.InitiateTakeoff(engine, planetTile);
            SoundDefOf.Gravship_Launch.PlayOneShotOnCamera();
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.TakeoffEnded))]
    public static class Patch_GravshipTakeoffEnded
    {
        // Static flag to allow the original method to run only from the sync method
        private static bool allowVanillaTakeoffEnded = false;

        static bool Prefix(WorldComponent_GravshipController __instance)
        {
            if (Multiplayer.Client == null) return true;

            GravshipTravelSession session = GravshipTravelSessionUtils.GetSession(__instance.takeoffTile);
            if (session == null)
            {
                MpLog.Error($"[MP] Patch_GravshipTakeoffEnded: Gravship session not found for tile {__instance.takeoffTile}. Cannot end takeoff.");
                return false;
            }

            if (session.takeOffEndedComplete)
                return false;

            if (allowVanillaTakeoffEnded)
            {
                session.takeOffEndedComplete = true;
                allowVanillaTakeoffEnded = false;
                return true;
            }

            if (session.Map == null)
            {
                return session.takeoffEndedSyncScheduled;
            }

            if (!session.takeoffEndedSyncScheduled)
            {
                session.takeoffEndedSyncScheduled = true;
                SyncTakeoffEnded(__instance);
            }
            return false; // Block local call, let sync handle it
        }

        [SyncMethod]
        public static void SyncTakeoffEnded(WorldComponent_GravshipController controller)
        {
            GravshipTravelSession session = GravshipTravelSessionUtils.GetSession(controller.takeoffTile);
            if (session == null)
            {
                MpLog.Error($"[MP] SyncTakeoffEnded: Gravship controller or session is null, cannot end takeoff.");
                return;
            }

            session.UnregisterMap();
            allowVanillaTakeoffEnded = true;
            controller.TakeoffEnded();
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.Notify_LandingAreaConfirmationStarted))]
    public static class Patch_GravshipLandingPause
    {
        static void Postfix(ref GravshipLandingMarker marker)
        {
            if (Multiplayer.Client == null) return;
            if (marker == null || marker.Map == null)
            {
                MpLog.Error("[MP] Patch_GravshipLandingPlacementPause: Marker or map is null, cannot pause for gravship landing.");
                return;
            }

            GravshipTravelSessionUtils.RegisterMap(marker.gravship.initialTile, marker.Map);
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.LandingEnded))]
    public static class Patch_GravshipLandingPauseEnd
    {
        static void Postfix(WorldComponent_GravshipController __instance)
        {
            if (Multiplayer.Client == null) return;
            if (__instance.takeoffTile == null)
            {
                MpLog.Error("[MP] Patch_GravshipLandingPauseEnd: Gravship takeoff tile is null, cannot end landing pause.");
                return;
            }

            if (Multiplayer.LocalServer != null)
                GravshipTravelSessionUtils.CloseSession(__instance.takeoffTile);
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.PlayerCanControl), MethodType.Getter)]
    public static class TickManager_PlayerCanControl_Patch
    {
        private static bool shownLandingMessage = false;

        static bool Prefix(ref AcceptanceReport __result)
        {
            if (Multiplayer.Client == null) return true;

            WorldComponent_GravshipController gravshipController = Find.GravshipController;
            if (gravshipController != null && gravshipController.LandingAreaConfirmationInProgress)
            {
                __result = shownLandingMessage ? false : "MessageConfirmLandingAreaFirst".Translate();
                shownLandingMessage = true;
                return false;
            }

            // Not in a landing session, use vanilla logic for player control
            __result = Current.Game.PlayerHasControl;
            return false;
        }

        // Call this when the landing session ends (e.g., in your session cleanup)
        public static void ResetLandingMessageFlag() => shownLandingMessage = false;
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.WorldComponentOnGUI))]
    public static class Patch_GravshipLandingConfirmSync
    {
        static void Prefix(WorldComponent_GravshipController __instance)
        {
            if (Multiplayer.Client == null) return;
            if (__instance.landingMarker == null) return;

            if (__instance.LandingAreaConfirmationInProgress && !Find.ScreenshotModeHandler.Active)
            {
                Rect rect = new Rect((float)UI.screenWidth / 2f - 215f, (float)UI.screenHeight - 150f - 70f, 430f, 70f);
                Widgets.DrawWindowBackground(rect);
                Rect rect2 = new Rect(rect.xMin + 10f, rect.yMin + 10f, 200f, 50f);
                Designator_MoveGravship des = __instance.MoveDesignator();
                if (Widgets.ButtonText(rect2, "DesignatorMoveGravship".Translate()))
                {
                    Find.DesignatorManager.Select(des);
                }
                TooltipHandler.TipRegion(rect2, "DesignatorMoveGravshipDesc".Translate());
                rect2.x += 210f;
                if (Widgets.ButtonText(rect2, "ConfirmLandGravship".Translate()))
                {
                    SyncConfirmGravshipLanding(__instance.landingMarker, __instance);
                }
                TooltipHandler.TipRegion(rect2, "ConfirmLandGravshipDesc".Translate());
            }
        }

        [SyncMethod]
        public static void SyncConfirmGravshipLanding(GravshipLandingMarker marker, WorldComponent_GravshipController controller)
        {
            marker.BeginLanding(controller);
            controller.landingMarker = null;
            SoundDefOf.Gravship_Land.PlayOneShotOnCamera();
        }
    }

    [HarmonyPatch(typeof(TempTerrainManager), nameof(TempTerrainManager.Tick))]
    public static class Patch_TempTerrainManager_Tick
    {
        static bool Prefix(TempTerrainManager __instance)
        {
            if (Multiplayer.Client == null) return true;
            return false;
        }
    }
}
