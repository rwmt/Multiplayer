using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Persistent;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client.Patches
{
    #region Input

    [HarmonyPatch(typeof(GravshipUtility), nameof(GravshipUtility.PreLaunchConfirmation))]
    public static class Patch_GravshipPreLaunchConfirmation
    {
        static void Prefix(Building_GravEngine engine, ref Action launchAction)
        {
            if (Multiplayer.Client == null) return;

            GravshipTravelUtils.OpenSessionAt(engine.Map.Tile);
        }
    }

    [HarmonyPatch(typeof(Dialog_MessageBox), MethodType.Constructor, [typeof(TaggedString), typeof(string), typeof(Action), typeof(string), typeof(Action), typeof(string), typeof(bool), typeof(Action), typeof(Action), typeof(WindowLayer)])]
    public static class Patch_GravshipPreLaunchCancel
    {
        static void Postfix(Dialog_MessageBox __instance)
        {
            if (Multiplayer.Client == null) return;
            if (!GravshipTravelUtils.IsGravShipMessageDialog(__instance)) return;

            __instance.buttonBAction = () =>
            {
                GravshipTravelUtils.SyncCloseSession(Find.CurrentMap.Tile);
                GravshipTravelUtils.SyncGravshipDialogCancel();
            };
        }
    }

    [HarmonyPatch(typeof(CompPilotConsole), nameof(CompPilotConsole.StartChoosingDestination))]
    public static class Patch_CompPilotConsole_StartChoosingDestination
    {
        public static PlanetTile? initialTile;
        static void Postfix(CompPilotConsole __instance)
        {
            if (Multiplayer.Client == null) return;
            if (!Multiplayer.ExecutingCmds) return;

            GravshipTravelUtils.CloseGravshipDialog();
            GravshipTravelUtils.OpenSessionAt(__instance.engine.Map.Tile);

            initialTile = __instance.parent.Map.Tile;
        }
    }

    // Tile confirmation input
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
            Patch_CompPilotConsole_StartChoosingDestination.initialTile = null;

            ClearTilePickerForNonIssuer();
        }

        private static void ClearTilePickerForNonIssuer()
        {
            if (!TickPatch.currentExecutingCmdIssuedBySelf)
            {
                Find.TilePicker.StopTargetingInt();
                Event.current.Use();
            }
        }
    }

    //Tile cancel input
    [HarmonyPatch(typeof(TilePicker), nameof(TilePicker.StopTargeting))]
    public static class Patch_TilePicker_StopTargeting
    {
        static void Prefix(TilePicker __instance)
        {
            if (Multiplayer.Client == null) return;
            if (!__instance.forGravship) return;
            if (Multiplayer.ExecutingCmds) return;

            if (!Patch_CompPilotConsole_StartChoosingDestination.initialTile.HasValue)
            {
                MpLog.Error("[MP] Patch_TilePicker_StopTargeting: initialTile is null, cannot close gravship session.");
                return;
            }

            GravshipTravelUtils.SyncCloseSession(Patch_CompPilotConsole_StartChoosingDestination.initialTile.Value);
            SyncStopTargeting();
        }

        [SyncMethod]
        static void SyncStopTargeting()
        {
            TilePicker tilePicker = Find.TilePicker;

            Find.World.renderer.wantedMode = WorldRenderMode.None;

            tilePicker.StopTargeting();

            Patch_CompPilotConsole_StartChoosingDestination.initialTile = null;
        }
    }

    [HarmonyPatch]
    public static class Patch_GravshipLandingPause
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GravshipUtility), nameof(GravshipUtility.ArriveExistingMap));
            yield return AccessTools.Method(typeof(GravshipUtility), nameof(GravshipUtility.ArriveNewMap));
        }
        static void Postfix(Gravship gravship)
        {
            if (Multiplayer.Client == null) return;

            GravshipTravelUtils.OpenSessionAt(gravship.destinationTile);
        }
    }

    [HarmonyPatch(typeof(Designator_MoveGravship), nameof(Designator_MoveGravship.DesignateSingleCell))]
    public static class HandleDesignatorDeselectForAllClients
    {
        static void Prefix() => CancelDesignatorDeselection.EnableCanceling();

        static void Finalizer() => CancelDesignatorDeselection.DisableCanceling();
    }

    [HarmonyPatch(typeof(GravshipLandingMarker), nameof(GravshipLandingMarker.BeginLanding))]
    public static class SyncLandingComfirmationButtonPressed
    {
        static bool Prefix(GravshipLandingMarker __instance)
        {
            if (Multiplayer.Client == null) return true;
            if (Multiplayer.ExecutingCmds) return true;

            SyncBeginLanding(__instance);

            return false;
        }

        [SyncMethod]
        public static void SyncBeginLanding(GravshipLandingMarker landingMarker)
        {
            var gravshipController = Find.GravshipController;

            if (landingMarker == null || landingMarker.Tile == null)
            {
                MpLog.Error($"[MP] SyncConfirmGravshipLanding: Marker [{landingMarker != null}] Tile [{landingMarker?.Tile != null}].");
                return;
            }

            gravshipController.landingMarker = landingMarker;
            landingMarker.BeginLanding(gravshipController);
            gravshipController.landingMarker = null;

            if (!TickPatch.currentExecutingCmdIssuedBySelf)
                SoundDefOf.Gravship_Land.PlayOneShotOnCamera();
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.AbortLanding))]
    public static class CloseSessionOnAbortLanding
    {
        static void Prefix(WorldComponent_GravshipController __instance, ref bool __state)
        {
            if (Multiplayer.Client == null) return;
            if (!Multiplayer.ExecutingCmds) return;

            GravshipTravelUtils.CloseSessionAt(__instance.landingTile);
        }
    }

    #endregion

    #region Landing/Takeoff freeze

    [HarmonyPatch]
    public static class FreezeGameForLandingAndTakeOff
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.InitiateTakeoff));
            yield return AccessTools.Method(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.InitiateLanding));
        }

        static void Postfix()
        {
            if (Multiplayer.Client == null) return;

            GravshipTravelUtils.StartFreeze();
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.TakeoffEnded))]
    public static class Patch_GravshipTakeoffEnded
    {
        static void Prefix(WorldComponent_GravshipController __instance)
        {
            if (Multiplayer.Client == null) return;

            GravshipTravelUtils.StopFreeze();
            GravshipTravelUtils.CloseSessionAt(__instance.takeoffTile);
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.LandingEnded))]
    public static class WorldComponent_GravshipController_LandingEnded_Patch
    {
        static void Prefix(WorldComponent_GravshipController __instance)
        {
            if (Multiplayer.Client == null) return;

            Rand.PushState();
            Rand.StateCompressed = __instance.map.AsyncTime().randState;

            GravshipTravelUtils.StopFreeze();
            GravshipTravelUtils.CloseSessionAt(__instance.gravship.destinationTile);
        }
        
        static void Finalizer()
        {
            if (Multiplayer.Client == null) return;
            Rand.PopState();
        }
    }
    #endregion

    // Stop the landing co message from showing every game tick
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
}
