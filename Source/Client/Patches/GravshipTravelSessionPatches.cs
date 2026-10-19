using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Factions;
using Multiplayer.Client.Persistent;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client.Patches
{
    #region Input

    [HarmonyPatch(typeof(GravshipUtility), nameof(GravshipUtility.PreLaunchConfirmation))]
    public static class PatchGravshipPreLaunchConfirmation
    {
        static void Prefix(Building_GravEngine engine, ref Action launchAction)
        {
            if (Multiplayer.Client == null) return;

            GravshipTravelUtils.OpenSessionAt(engine.Map.Tile);
        }
    }

    [HarmonyPatch]
    public static class PatchGravshipPreLaunchCancel
    {
        static MethodBase TargetMethod()
        {
            return MpMethodUtil.GetLambda(typeof(GravshipUtility), nameof(GravshipUtility.PreLaunchConfirmation), lambdaOrdinal: 4);
        }

        static void Postfix(Dialog_MessageBox __instance)
        {
            if (Multiplayer.Client == null) return;
            if (!Multiplayer.ExecutingCmds) return;

            GravshipTravelUtils.CloseSessionAt(Find.CurrentMap.Tile);
            GravshipTravelUtils.CloseGravshipPrelaunchDialog();
        }
    }

    [HarmonyPatch(typeof(CompPilotConsole), nameof(CompPilotConsole.StartChoosingDestination_NewTemp))]
    public static class PatchPilotConsoleStartChoosingDestination
    {
        static void Postfix(CompPilotConsole __instance, bool launching)
        {
            if (Multiplayer.Client == null) return;
            if (!launching) return;

            GravshipTravelUtils.CloseGravshipPrelaunchDialog();
            GravshipTravelUtils.OpenSessionAt(__instance.engine.Map.Tile);
        }
    }

    [HarmonyPatch]
    static class PatchTilePickerCancelLambda
    {
        static MethodBase TargetMethod()
        {
            return MpMethodUtil.GetLambda(typeof(CompPilotConsole), nameof(CompPilotConsole.StartChoosingDestination_NewTemp), lambdaOrdinal: 4);
        }

        static void Prefix(bool ___launching, ref bool __state)
        {
            if (Multiplayer.Client == null || ___launching || Multiplayer.dontSync)
                return;

            Multiplayer.dontSync = true;
            __state = true;
        }

        // TODO: Something in Feedback.cs seems to block the wantedMode switch.
        // For now, it's set manually - consider keeping it this way permanently.
        static void Finalizer(bool ___launching, PlanetTile ___curTile, bool __state)
        {
            if (Multiplayer.Client == null) return;
            if (__state) Multiplayer.dontSync = false;
            if (!Multiplayer.ExecutingCmds || !___launching) return;

            Find.World.renderer.wantedMode = WorldRenderMode.None;
            Find.TilePicker.StopTargetingInt();
            GravshipTravelUtils.CloseSessionAt(___curTile);
        }
    }

    [HarmonyPatch]
    public static class PatchGravshipMapArriveMethods
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
    public static class PatchGravshipDesignatorDeselectForAllClients
    {
        static void Prefix() => CancelDesignatorDeselection.EnableCanceling();

        static void Finalizer() => CancelDesignatorDeselection.DisableCanceling();
    }

    // TODO: Is there a better way to synchronize this method?
    [HarmonyPatch(typeof(GravshipLandingMarker), nameof(GravshipLandingMarker.BeginLanding))]
    public static class PatchBeginLandingToSyncWithClients
    {
        // The marker this client just confirmed, see KeepLandingMarkerUntilSyncedLanding
        internal static GravshipLandingMarker confirmedLocally;

        static bool Prefix(GravshipLandingMarker __instance)
        {
            if (Multiplayer.Client == null) return true;
            if (Multiplayer.ExecutingCmds) return true;

            confirmedLocally = __instance;
            SyncBeginLanding(__instance, __instance.GravshipRotation);

            return false;
        }

        // The confirming player's rotation (vanilla only confirms without the move designator selected, so it's the
        // last designated one): other players may still have the designator selected, and rotating its preview
        // changes the marker's, and so the gravship's, rotation locally. BeginLanding lands with it.
        [SyncMethod]
        public static void SyncBeginLanding(GravshipLandingMarker landingMarker, Rot4 rotation)
        {
            var gravshipController = Find.GravshipController;

            // Already landing: the confirm button stays usable until this command executes, so it can be sent twice
            // (the destroyed marker then reads as null)
            if (landingMarker == null || landingMarker.Destroyed)
                return;

            if (landingMarker.Tile == null)
            {
                MpLog.Error($"[MP] SyncConfirmGravshipLanding: Marker [{landingMarker != null}] Tile [{landingMarker?.Tile != null}].");
                return;
            }

            // Deselect the designator everywhere: rotating its preview during the cutscene would change the rotation
            // PlaceGravship places the ship with. Before setting the rotation, Deselected restores its own one.
            if (Find.DesignatorManager.SelectedDesignator is Designator_MoveGravship)
            {
                CancelDesignatorDeselection.EnableCanceling();
                try
                {
                    Find.DesignatorManager.Deselect();
                }
                finally
                {
                    CancelDesignatorDeselection.DisableCanceling();
                }
            }

            landingMarker.GravshipRotation = rotation;

            gravshipController.landingMarker = landingMarker;
            landingMarker.BeginLanding(gravshipController);
            gravshipController.landingMarker = null;

            if (!TickPatch.currentExecutingCmdIssuedBySelf)
                SoundDefOf.Gravship_Land.PlayOneShotOnCamera();
        }
    }

    /// <summary>
    /// Vanilla's confirm landing button (WorldComponentOnGUI) clears the controller's landingMarker right after
    /// BeginLanding, which MP turns into a command. Until that command executes, the confirming client would be the only
    /// one without a landing confirmation in progress: a join point wouldn't restore its landing session, the move
    /// designator's sync worker and AbortLanding would fail there only. Keep the field until the synced landing.
    /// </summary>
    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.WorldComponentOnGUI))]
    public static class KeepLandingMarkerUntilSyncedLanding
    {
        static void Prefix() => PatchBeginLandingToSyncWithClients.confirmedLocally = null;

        static void Finalizer(WorldComponent_GravshipController __instance)
        {
            var marker = PatchBeginLandingToSyncWithClients.confirmedLocally;
            PatchBeginLandingToSyncWithClients.confirmedLocally = null;

            if (Multiplayer.Client != null && marker is { Destroyed: false } && __instance.landingMarker == null)
                __instance.landingMarker = marker;
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.AbortLanding))]
    public static class PatchAbortLandingToCloseSession
    {
        static void Prefix(WorldComponent_GravshipController __instance, ref bool __state)
        {
            if (Multiplayer.Client == null) return;
            if (!Multiplayer.ExecutingCmds) return;

            GravshipTravelUtils.CloseSessionAt(__instance.landingTile);
        }
    }

    /// <summary>
    /// PlaceGravship (spawning the ship, clearing what's under it, recounting) runs in a long event queued from a
    /// render callback of the landing cutscene, so outside of any tick or command and with the interface's time and
    /// RNG. Run it in the landing map's context, like a command of that map (its time, random state and faction;
    /// PlaceGravship_FactionContext_Patch pushes the gravship's faction inside it), and afterwards update what a
    /// command updates. In MP MapUpdate doesn't do that (see AsyncTimeComp.UpdateManagers), which left the ship's
    /// lights off (#635).
    /// All clients reach this point after the same command (SyncBeginLanding) and the landing map stays paused by its
    /// GravshipTravelSession meanwhile. The camera pan before the long event takes a second, by which time the cutscene
    /// freeze sent by the host normally has stopped every client at the same tick. That isn't guaranteed (no host on
    /// a standalone server, a client lagging by over a second): making the placement a command would need the
    /// cutscene's callback chain to wait for it.
    /// </summary>
    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.PlaceGravship))]
    public static class PatchPlaceGravshipInMapContext
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(Map map, out int __state)
        {
            __state = -1;
            if (Multiplayer.Client == null || Multiplayer.Ticking || Multiplayer.ExecutingCmds) return;
            if (map?.AsyncTime() is not { } comp) return;

            __state = Rand.stateStack.Count;
            comp.PreContext();
        }

        static void Postfix(Map map, int __state)
        {
            if (__state >= 0)
                map.AsyncTime().UpdateManagers();
        }

        [HarmonyPriority(MpPriority.MpLast)]
        static void Finalizer(Map map, int __state)
        {
            if (__state < 0) return;

            AsyncTimeComp.RestoreRandStack(__state + 1, nameof(WorldComponent_GravshipController.PlaceGravship));
            map.AsyncTime().PostContext();
        }
    }

    /// <summary>
    /// The takeoff counterpart of PatchPlaceGravshipInMapContext: the long event queued from the gravship capture's
    /// callback (OnGravshipCaptureComplete) removes the gravship from the takeoff map, creating its world object, and
    /// rebuilds the map's vacuum data. Run it in the takeoff map's context as well
    /// (RemoveGravshipFromMap_Patch pushes the gravship's faction inside it).
    /// </summary>
    [HarmonyPatch]
    public static class PatchGravshipRemovalInMapContext
    {
        static MethodBase TargetMethod()
        {
            // The only lambda of OnGravshipCaptureComplete calling RemoveGravshipFromMap
            var removeGravship = AccessTools.Method(typeof(WorldComponent_GravshipController),
                nameof(WorldComponent_GravshipController.RemoveGravshipFromMap));
            var lambdaPrefix = $"<{nameof(WorldComponent_GravshipController.OnGravshipCaptureComplete)}>";

            return typeof(WorldComponent_GravshipController).GetNestedTypes(AccessTools.all)
                .SelectMany(t => t.GetDeclaredMethods())
                .Where(m => m.Name.StartsWith(lambdaPrefix))
                .First(m => PatchProcessor.ReadMethodBody(m).Any(i => i.Value as MethodInfo == removeGravship));
        }

        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(out (Map map, int randDepth) __state)
        {
            __state = (null, -1);
            if (Multiplayer.Client == null || Multiplayer.Ticking || Multiplayer.ExecutingCmds) return;

            // The takeoff map, set by InitiateTakeoff
            var map = Find.GravshipController?.map;
            if (map?.AsyncTime() is not { } comp) return;

            __state = (map, Rand.stateStack.Count);
            comp.PreContext();
        }

        static void Postfix((Map map, int randDepth) __state)
        {
            __state.map?.AsyncTime().UpdateManagers();
        }

        [HarmonyPriority(MpPriority.MpLast)]
        static void Finalizer((Map map, int randDepth) __state)
        {
            if (__state.map == null) return;

            AsyncTimeComp.RestoreRandStack(__state.randDepth + 1, nameof(WorldComponent_GravshipController.RemoveGravshipFromMap));
            __state.map.AsyncTime().PostContext();
        }
    }

    #endregion

    #region Landing/Takeoff freeze

    [HarmonyPatch]
    public static class PatchGravshipCutsceneToFreeze
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.InitiateTakeoff));
            yield return AccessTools.Method(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.InitiateLanding));
        }

        static void Postfix(MethodBase __originalMethod)
        {
            if (Multiplayer.Client == null) return;

            GravshipCutsceneSync.CutsceneStarted(__originalMethod.Name == nameof(WorldComponent_GravshipController.InitiateTakeoff));
        }
    }

    /// <summary>
    /// The takeoff/landing cutscenes end in WorldComponent_GravshipController.WorldComponentUpdate, i.e. in a frame
    /// update, at a different moment on every client and outside of any tick or command. TakeoffEnded/LandingEnded
    /// change the game state (landing outcome, world objects, maps, sessions), so in MP they are not run from there.
    /// Instead, every client sends a synced command when its own cutscene ends and the first one to execute runs
    /// the method on all clients at the same point of the simulation. The others are ignored.
    /// </summary>
    public static class GravshipCutsceneSync
    {
        private enum Cutscene { None, Takeoff, Landing }

        // Only changed from InitiateTakeoff/InitiateLanding and TakeoffEnded/LandingEnded which run in commands,
        // so it's the same on all clients. Saved with the controller, a join point can happen during a cutscene.
        private static Cutscene pending;

        // Local state of this client
        private static bool endRequested;
        private static bool frozen;

        public static bool EndRequested => endRequested && pending != Cutscene.None;

        public static void ExposeData()
        {
            // Don't add it to singleplayer saves, but reset it on every load
            if (Scribe.mode == LoadSaveMode.Saving && Multiplayer.Client == null)
                return;

            Scribe_Values.Look(ref pending, "mpPendingCutsceneEnd");
        }

        public static void CutsceneStarted(bool takeoff)
        {
            pending = takeoff ? Cutscene.Takeoff : Cutscene.Landing;
            endRequested = false;

            frozen = true;
            GravshipTravelUtils.StartFreeze();
        }

        private static void Unfreeze()
        {
            if (!frozen) return;

            frozen = false;
            GravshipTravelUtils.StopFreeze();
        }

        private static bool IsPending(bool takeoff) =>
            pending == (takeoff ? Cutscene.Takeoff : Cutscene.Landing);

        // Called by vanilla in the interface when the local cutscene ends
        public static void LocalCutsceneEnded(WorldComponent_GravshipController controller, bool takeoff)
        {
            if (EndRequested || !Multiplayer.ShouldSync)
                return;

            if (!IsPending(takeoff))
            {
                // The synced end already ran (e.g. this client's cutscene was very late), only clean up the visuals
                Log.Warning($"MP: Gravship {(takeoff ? "takeoff" : "landing")} cutscene ended locally without a pending synced end");
                Unfreeze();
                WorldComponent_GravshipController.cutsceneInProgress = false;

                try
                {
                    controller.ResetCutscene();
                }
                catch (Exception e)
                {
                    Log.Error($"MP: Exception resetting gravship cutscene: {e}");
                }

                return;
            }

            endRequested = true;

            // Commands don't execute while frozen
            Unfreeze();

            if (takeoff)
                SyncTakeoffEnded();
            else
                SyncLandingEnded(controller.map);
        }

        // Called when TakeoffEnded/LandingEnded actually runs
        public static void SyncedEndExecuting()
        {
            pending = Cutscene.None;
            endRequested = false;
            Unfreeze();
        }

        /// <summary>
        /// Vanilla can't finish a cutscene without its gravship, which nothing saves while it's neither on a map nor a
        /// world object: a join point during a cutscene (e.g. right after the unfreeze, before the synced end) loses
        /// it, and a catching-up joiner can execute the landing's end before its own PlaceGravship ran. TakeoffEnded
        /// (after abandoning the map) and LandingEnded would throw and leave the game in the cutscene for good (no
        /// world map, no saving, the map paused). End the cutscene without the gravship instead, which is lost.
        /// Runs in the synced end command or at the same point of every load, so the same way on all clients.
        /// </summary>
        public static void EndWithoutGravship(WorldComponent_GravshipController controller, bool takeoff)
        {
            Log.Warning($"MP: Gravship {(takeoff ? "takeoff" : "landing")} ended without its gravship (lost by a reload during the cutscene or not placed here yet)");

            pending = Cutscene.None;
            endRequested = false;
            Unfreeze();

            var map = controller.map;
            if (takeoff)
            {
                GravshipTravelUtils.CloseSessionAt(controller.takeoffTile);
            }
            else
            {
                GravshipTravelUtils.CloseSessionAt(map?.Tile ?? controller.landingTile);
                if (map != null)
                    PatchGravshipLandingEnded.SetNormalSpeedAfterLanding(map);
                controller.moveDesignator = null;
                Current.Game.Gravship = null;
            }

            controller.map = null;
            controller.ResetCutscene();
        }

        // After loading, a cutscene in progress can't continue (its gravship isn't saved) and nobody would end it
        public static void EndCutsceneLostByLoading(WorldComponent_GravshipController controller)
        {
            if (pending != Cutscene.None && controller.gravship == null)
                EndWithoutGravship(controller, pending == Cutscene.Takeoff);
        }

        [SyncMethod]
        public static void SyncTakeoffEnded()
        {
            if (!IsPending(true)) return;
            RunWithFeedback(Find.GravshipController.TakeoffEnded);
        }

        // The map argument makes it a command of the landing map (its RNG, time and faction)
        [SyncMethod]
        public static void SyncLandingEnded(Map map)
        {
            if (!IsPending(false)) return;
            RunWithFeedback(Find.GravshipController.LandingEnded);
        }

        // Everyone watched the cutscene, so show its feedback (sounds, messages, camera) to all players like
        // vanilla does, not only to the player whose command happened to arrive first.
        private static void RunWithFeedback(Action action)
        {
            var prevIssuedBySelf = TickPatch.currentExecutingCmdIssuedBySelf;
            TickPatch.currentExecutingCmdIssuedBySelf = !TickPatch.Simulating;

            try
            {
                action();
            }
            finally
            {
                TickPatch.currentExecutingCmdIssuedBySelf = prevIssuedBySelf;
            }
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.ExposeData))]
    public static class PatchGravshipControllerExposePendingEnd
    {
        static void Postfix() => GravshipCutsceneSync.ExposeData();
    }

    /// <summary>
    /// GravshipTravelSession isn't saved, so every reload (all clients reload at a join point) and every joiner lost
    /// the pause of the landing map while the players choose where to land, which can take a while. Vanilla saves the
    /// landing confirmation state (the landing marker), so recreate the session from it after loading. This runs at
    /// the same point of every load on every client and joiner, so they all get the same session (and session id).
    /// The takeoff side (prelaunch dialog and destination picker) isn't restored: it's UI which a reload closes, so a
    /// restored session would keep the map paused with no way to continue or cancel. A cutscene in progress can't
    /// be resumed after loading (its gravship isn't saved), so it's ended instead (EndCutsceneLostByLoading).
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    public static class RestoreGravshipLandingSessionOnLoad
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null || !ModsConfig.OdysseyActive) return;

            var controller = Find.GravshipController;
            if (controller == null) return;

            GravshipCutsceneSync.EndCutsceneLostByLoading(controller);

            if (!controller.LandingAreaConfirmationInProgress) return;

            // The same tile ArriveExistingMap/ArriveNewMap open the session at
            if (controller.landingMarker.gravship is { } gravship)
                GravshipTravelUtils.OpenSessionAt(gravship.destinationTile);
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.WorldComponentUpdate))]
    public static class PatchGravshipCutsceneHoldLastFrame
    {
        static void Prefix(WorldComponent_GravshipController __instance)
        {
            if (Multiplayer.Client == null) return;

            // Keep the cutscene on its last frame while waiting for the synced end
            if (GravshipCutsceneSync.EndRequested && __instance.timeLeft < 0f)
                __instance.timeLeft = 0f;
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.TakeoffEnded))]
    public static class PatchGravshipTakeoffEnded
    {
        static bool Prefix(WorldComponent_GravshipController __instance, out bool __state)
        {
            __state = false;
            if (Multiplayer.Client == null) return true;

            if (Multiplayer.InInterface)
            {
                GravshipCutsceneSync.LocalCutsceneEnded(__instance, true);
                return false;
            }

            if (__instance.gravship == null)
            {
                GravshipCutsceneSync.EndWithoutGravship(__instance, takeoff: true);
                return false;
            }

            GravshipCutsceneSync.SyncedEndExecuting();
            GravshipTravelUtils.CloseSessionAt(__instance.takeoffTile);

            // Forced like the other gravship pushes (see RemoveGravshipFromMap_Patch): in this world command
            // Faction.OfPlayer may already be the gravship's faction (its player's command came first) while the maps
            // hold the spectator's data, which would make a regular push a no-op
            if (Multiplayer.GameComp.multifaction && __instance.gravship?.Faction is { } faction)
            {
                FactionExtensions.PushFaction(null, faction, force: true);
                __state = true;
            }

            return true;
        }

        static void Finalizer(bool __state)
        {
            // The forced pop restores the world and every live map, TakeoffEnded can remove the map left behind
            if (__state)
                FactionExtensions.PopFaction();
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.LandingEnded))]
    public static class PatchGravshipLandingEnded
    {
        static bool Prefix(WorldComponent_GravshipController __instance, out (Map factionMap, bool randPushed) __state)
        {
            __state = default;
            if (Multiplayer.Client == null) return true;

            if (Multiplayer.InInterface)
            {
                GravshipCutsceneSync.LocalCutsceneEnded(__instance, false);
                return false;
            }

            if (__instance.gravship == null)
            {
                GravshipCutsceneSync.EndWithoutGravship(__instance, takeoff: false);
                return false;
            }

            var map = __instance.map;

            // Normally runs in a command of the landing map which already provides its RNG state (and saves it after).
            // Otherwise (e.g. called while loading) keep it from consuming an unrelated RNG state.
            if (!Multiplayer.ExecutingCmds && !Multiplayer.Ticking)
            {
                Rand.PushState();
                Rand.StateCompressed = map.AsyncTime().randState;
                __state.randPushed = true;
            }

            GravshipCutsceneSync.SyncedEndExecuting();
            GravshipTravelUtils.CloseSessionAt(__instance.gravship.destinationTile);

            SetNormalSpeedAfterLanding(map);

            // Forced: the command's faction (whichever player's end command came first) may already be the gravship's
            if (Multiplayer.GameComp.multifaction && __instance.gravship.Faction is { } faction)
            {
                map.PushFaction(faction, force: true);
                __state.factionMap = map;
            }

            return true;
        }

        static void Finalizer((Map factionMap, bool randPushed) __state)
        {
            __state.factionMap?.PopFaction();

            if (__state.randPushed)
                Rand.PopState();
        }

        // LandingEnded sets Find.TickManager.CurTimeSpeed = TimeSpeed.Normal, which MP overrides with its own speeds.
        // Without async time all tickables share one speed (like a time speed command sets it), otherwise only the
        // landing map's is changed.
        internal static void SetNormalSpeedAfterLanding(Map map)
        {
            if (Multiplayer.GameComp.asyncTime)
                map.AsyncTime().DesiredTimeSpeed = TimeSpeed.Normal;
            else
                Multiplayer.AsyncWorldTime.SetTimeEverywhere(TimeSpeed.Normal);
        }
    }
    #endregion

    // TODO: Check what this actually does and whether it’s still necessary
    // Stop the landing co message from showing every game tick
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.PlayerCanControl), MethodType.Getter)]
    public static class PatchTickmanagerPlayerCanControlGetter
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
