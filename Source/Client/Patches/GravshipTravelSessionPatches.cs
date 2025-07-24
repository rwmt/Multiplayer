using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Persistent;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.EnterpriseServices;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Tilemaps;
using Verse;
using Verse.Sound;

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
                    GravshipTravelSessionUtils.SyncCloseSession(Find.CurrentMap.Tile);
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

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.BeginTakeoffCutscene))]
    public static class Patch_BeginTakeoffCutscene
    {
        // Static flag to allow the original method to run only from the sync method
        private static bool allowVanillaBeginTakeoffCutscene = false;

        static bool Prefix(WorldComponent_GravshipController __instance)
        {
            if (Multiplayer.Client == null) return true;

            GravshipTravelSession session = GravshipTravelSessionUtils.GetSession(__instance.takeoffTile);
            if (session == null)
            {
                MpLog.Error($"[MP] Patch_BeginTakeoffCutscene: Gravship session not found for tile {__instance.takeoffTile}. Cannot end takeoff.");
                return false;
            }

            if (allowVanillaBeginTakeoffCutscene)
            {
                MpLog.Debug($"[MP] Patch_BeginTakeoffCutscene: Allowing vanilla.");
                allowVanillaBeginTakeoffCutscene = false;
                return true;
            }

            if (!session.beginTakeoffSyncScheduled)
            {
                session.beginTakeoffSyncScheduled = true;
                SyncBeginTakeoffCutscene(__instance);
            }
            return false; // Block local call, let sync handle it
        }

        [SyncMethod]
        public static void SyncBeginTakeoffCutscene(WorldComponent_GravshipController controller)
        {
            MpLog.Debug($"[MP] SyncBeginTakeoffCutscene.");
            GravshipTravelSession session = GravshipTravelSessionUtils.GetSession(controller.takeoffTile);
            if (session == null)
            {
                MpLog.Error($"[MP] SyncBeginTakeoffCutscene: Gravship controller or session is null, cannot end takeoff.");
                return;
            }

            session.UnregisterMap();
            allowVanillaBeginTakeoffCutscene = true;
            controller.BeginTakeoffCutscene();
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.TakeoffEnded))]
    public static class Patch_GravshipTakeoffEnded
    {
        // Static flag to allow the original method to run only from the sync method
        private static bool allowVanillaTakeoffEnded = false;

        static bool Prefix(WorldComponent_GravshipController __instance)
        {
            MpLog.Debug($"[MP] Patch_GravshipTakeoffEnded: Takeoff ended for tile {__instance.takeoffTile}.");
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
                MpLog.Debug($"[MP] Patch_GravshipTakeoffEnded: Allowing vanilla TakeoffEnded for tile {__instance.takeoffTile}.");
                session.takeOffEndedComplete = true;
                allowVanillaTakeoffEnded = false;
                return true;
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
            MpLog.Debug($"[MP] SyncTakeoffEnded: Ending takeoff for tile {controller.takeoffTile}.");
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


    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.LandingEnded))]
    public static class WorldComponent_GravshipController_LandingEnded_Patch
    {
        private static bool allowVanillaLanding = false;
        private static bool vanillaLandingCalled = false;

        static bool Prefix(WorldComponent_GravshipController __instance)
        {
            if (Multiplayer.Client == null) return true;
            if (__instance == null)
            {
                MpLog.Error("[MP] Patch_GravshipLandingEnded: WorldComponent_GravshipController is null, cannot end landing.");
                return false;
            }

            // TODO: If the landing has ended and this is called again via vanilla logic, session may be null.
            // Need a more graceful way to handle this so we can throw an error on session being null.
            GravshipTravelSession session = GravshipTravelSessionUtils.GetSession(__instance.takeoffTile);
            if (session == null)
                return false;

            if (allowVanillaLanding)
            {
                MpLog.Debug("[MP] Allowing vanilla landing logic to run.");
                allowVanillaLanding = false;
                vanillaLandingCalled = true;
                return true;
            }

            if (!session.landingSyncScheduled)
            {
                session.landingSyncScheduled = true;
                SyncBeginLandingCutscene(__instance);
            }

            return false;
        }

        static void Postfix(WorldComponent_GravshipController __instance)
        {
            if (Multiplayer.Client == null) return;
            if (!vanillaLandingCalled) return;

            GravshipTravelSessionUtils.CloseSession(__instance.takeoffTile);
            vanillaLandingCalled = false;
        }

        [SyncMethod]
        static void SyncBeginLandingCutscene(WorldComponent_GravshipController controller)
        {
            allowVanillaLanding = true;
            controller.LandingEnded();
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

    [HarmonyPatch(typeof(GravshipAudio), nameof(GravshipAudio.BeginTakeoff))]
    public static class GravshipAudio_BeginTakeoff_Patch
    {
        static bool Prefix()
        {
            if (Multiplayer.Client == null) return true;
            MpLog.Debug("[MP] GravshipAudio_BeginTakeoff_Patch: Beginning gravship takeoff audio.");
            return true; // Allow vanilla logic to run
        }
    }

    //[HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.DrawGravship))]
    //public static class WorldComponent_GravshipController_DrawGravship_Patch
    //{
    //    static bool Prefix(WorldComponent_GravshipController __instance)
    //    {
    //        if (Multiplayer.Client == null) return true;
    //        MpLog.Debug("[MP] WorldComponent_GravshipController_DrawGravship_Patch: Drawing gravship.");
    //        // Allow vanilla logic to run
    //        return true;
    //    }
    //}

    [HarmonyPatch(typeof(GravshipRenderer), nameof(GravshipRenderer.BeginCutscene))]
    public static class GravshipRenderer_BeginCutscene_Patch
    {
        static bool Prefix()
        {
            if (Multiplayer.Client == null) return true;
            MpLog.Debug("[MP] GravshipRenderer_BeginCutscene_Patch: Beginning gravship cutscene rendering.");
            return true; // Allow vanilla logic to run
        }
    }

    [HarmonyPatch(typeof(BiomeWorker_GlacialPlain), nameof(BiomeWorker_GlacialPlain.NoiseAt))]
    public static class BiomeWorker_GlacialPlain_Patch
    {
        static bool Prefix()
        {
            if (Multiplayer.Client == null) return true;

            MpLog.Debug($"[MP] BiomeWorker_GlacialPlain_Patch: getting noise with seed {Gen.HashCombineInt(Find.World.info.Seed, 44319114)}.");
            return true; // Allow vanilla logic to run
        }

        static void Postfix(ref float __result)
        {
            if (Multiplayer.Client == null) return;
            MpLog.Debug($"[MP] BiomeWorker_GlacialPlain_Patch: returning noise value {__result}.");
        }
    }

    [HarmonyPatch(typeof(MapParent), nameof(MapParent.Abandon))]
    public class MapParent_Abandon_Patch
    {
        static void Postfix(MapParent __instance)
        {
            if (Multiplayer.Client == null) return;
            MpLog.Debug("[MP] MapParent_Abandon_Patch: Abandoning map parent.");
        }
    }

    [HarmonyPatch(typeof(LandingOutcomeWorker_GravNausea), nameof(LandingOutcomeWorker_GravNausea.ApplyOutcome))]
    public static class LandingOutcomeWorker_GravNausea_ApplyOuytcome_Patch
    {
        static void Prefix()
        {
            MpLog.Debug("[MP] LandingOutcomeWorker_GravNausea_ApplyOuytcome_Patch");
        }
    }

    [HarmonyPatch(typeof(GravshipUtility), nameof(GravshipUtility.ArriveNewMap))]
    public static class GravshipUtility_ArriveNewMap_Patch
    {
        static bool Prefix()
        {
            if (Multiplayer.Client == null) return true;
            MpLog.Debug($"[MP] GravshipUtility_ArriveNewMap_Patch: Arriving at new map");
            return true; // Allow vanilla logic to run
        }
    }

    [HarmonyPatch(typeof(FreezeManager), nameof(FreezeManager.DoIceMelting))]
    public static class FreezeManager_DoIceMelting_Patch
    {
        static bool Prefix()
        {
            if (Multiplayer.Client == null) return true;
            return false; // Deny vanilla logic to run
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.InitiateTakeoff))]
    public static class WorldComponent_GravshipController_InitiateTakeoff_Patch
    {
        static void Prefix()
        {
            if (Multiplayer.Client == null) return;
            MpLog.Debug("[MP] WorldComponent_GravshipController_InitiateTakeoff_Patch.");
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.OnGravshipCaptureComplete))]
    public static class WorldComponent_GravshipController_OnGravshipCaptureComplete_Patch
    {
        static bool Prefix(WorldComponent_GravshipController __instance, ref RimWorld.Capture capture)
        {
            if (Multiplayer.Client == null) return true;
            MpLog.Debug("[MP] WorldComponent_GravshipController_OnGravshipCaptureComplete_Patch: Capturing gravship.");
            return true;

            //RimWorld.Capture existingCapture = __instance.gravship?.capture;
            //__instance.zoomRange = __instance.GetCutsceneZoomRange(capture);
            //__instance.PanIf(Find.CameraDriver.config.gravshipPanOnCutsceneStart, capture.captureCenter, __instance.zoomRange.min, 1f, delegate
            //{
            //    MpLog.Debug("[MP] WorldComponent_GravshipController_OnGravshipCaptureComplete_Patch: Starting pan?");
            //    Delay.AfterNSeconds(0.5f, delegate
            //    {
            //        MpLog.Debug("[MP] WorldComponent_GravshipController_OnGravshipCaptureComplete_Patch: After delay?");
            //        LongEventHandler.QueueLongEvent(delegate
            //        {
            //            MpLog.Debug("[MP] WorldComponent_GravshipController_OnGravshipCaptureComplete_Patch: Delegate");
            //            HashSet<IntVec3> validSubstructure = existingCapture.engine.ValidSubstructure;
            //            MpLog.Debug("[MP] WorldComponent_GravshipController_OnGravshipCaptureComplete_Patch: Before baking");
            //            LayerSubMesh item = SectionLayer_IndoorMask.BakeGravshipIndoorMesh(__instance.map, validSubstructure, validSubstructure.Count, WorldComponent_GravshipController.IndoorMaskGravship, existingCapture.captureCenter);
            //            List<LayerSubMesh> collection = SectionLayer_GravshipHull.BakeGravshipIndoorMesh(__instance.map, existingCapture.captureBounds, existingCapture.captureCenter);
            //            MpLog.Debug("[MP] WorldComponent_GravshipController_OnGravshipCaptureComplete_Patch: After baking");
            //            __instance.gravship = __instance.RemoveGravshipFromMap(existingCapture.engine);
            //            __instance.gravship.capture = existingCapture;
            //            __instance.gravship.bakedIndoorMasks.Clear();
            //            __instance.gravship.bakedIndoorMasks.Add(item);
            //            __instance.gravship.bakedIndoorMasks.AddRange(collection);
            //            __instance.RegenerateGravshipMask();
            //            __instance.BeginTakeoffCutscene();
            //        }, "GeneratingGravship", doAsynchronously: false, null);
            //    });
            //});
            //return false;
        }
    }

    [HarmonyPatch(typeof(SectionLayer_GravshipHull), nameof(SectionLayer_GravshipHull.BakeGravshipIndoorMesh))]
    public static class SectionLayer_GravshipHull_BakeGravshipIndoorMesh_Patch
    {
        static bool Prefix()
        {
            if (Multiplayer.Client == null) return true;
            MpLog.Debug("[MP] SectionLayer_GravshipHull_BakeGravshipIndoorMesh_Patch: Baking gravship hull mesh.");
            return true; // Allow vanilla logic to run
        }

        static void Postfix()
        {
            if (Multiplayer.Client == null) return;
            MpLog.Debug("[MP] SectionLayer_GravshipHull_BakeGravshipIndoorMesh_Patch: Finished baking gravship hull mesh.");
        }
    }

    [HarmonyPatch(typeof(SectionLayer_IndoorMask), nameof(SectionLayer_IndoorMask.BakeGravshipIndoorMesh))]
    public static class SectionLayer_IndoorMask_BakeGravshipIndoorMesh_Patch
    {
        static bool Prefix()
        {
            if (Multiplayer.Client == null) return true;
            MpLog.Debug("[MP] SectionLayer_IndoorMask_BakeGravshipIndoorMesh_Patch: Baking gravship indoor mesh.");
            return true; // Allow vanilla logic to run
        }

        static void Postfix()
        {
            if (Multiplayer.Client == null) return;
            MpLog.Debug("[MP] SectionLayer_IndoorMask_BakeGravshipIndoorMesh_Patch: Finished baking gravship indoor mesh.");
        }
    }

    [HarmonyPatch]
    public static class Patch_CaptureAndBeginCutscene_DisplayClass
    {
        static MethodBase TargetMethod()
        {
            var outerType = typeof(WorldComponent_GravshipController);

            // Manually search for the known nested type
            var nestedType = outerType.GetNestedType("<>c__DisplayClass45_0", BindingFlags.NonPublic);
            if (nestedType == null)
                throw new Exception("Could not find nested type <>c__DisplayClass45_0");

            // Now find the actual local function inside that class
            var method = nestedType.GetMethod("<InitiateLanding>g__CaptureAndBeginCutscene|0", BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
                throw new Exception("Could not find method <InitiateLanding>g__CaptureAndBeginCutscene|0");

            MpLog.Debug($"[MP] Found method: {method.DeclaringType.FullName}.{method.Name}");
            return method;
        }

        static void Prefix(object __instance)
        {
            // You can also inspect captured fields via reflection if needed
            MpLog.Debug("[MP] Prefix: CaptureAndBeginCutscene (DisplayClass)");
        }
    }
}
