using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using Multiplayer.API;
using Multiplayer.Client.Patches;
using UnityEngine;
using Verse;
using static Verse.Widgets;

namespace Multiplayer.Client.Persistent
{
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(TextAnchor) })]
    static class MakeCancelFormingButtonRed
    {
        static void Prefix(string label, ref bool __state)
        {
            if (CaravanFormingProxy.drawing == null) return;
            if (label != "CancelButton".Translate()) return;

            GUI.color = new Color(1f, 0.3f, 0.35f);
            __state = true;
        }

        static void Postfix(bool __state, ref bool __result)
        {
            if (!__state) return;

            GUI.color = Color.white;
            if (__result)
            {
                CaravanFormingProxy.drawing.Session?.Cancel();
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonTextWorker))]
    static class FormCaravanHandleReset
    {
        static void Prefix(string label, ref bool __state)
        {
            if (CaravanFormingProxy.drawing == null) return;
            if (label != "ResetButton".Translate()) return;

            __state = true;
        }

        static void Postfix(bool __state, ref DraggableResult __result)
        {
            if (!__state) return;

            if (__result.AnyPressed())
            {
                CaravanFormingProxy.drawing.Session?.Reset();
                __result = DraggableResult.Idle;
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_FormCaravan), nameof(Dialog_FormCaravan.DrawAutoSelectCheckbox))]
    static class DrawAutoSelectCheckboxPatch
    {
        // This is merely hiding it and enabling manual transfer as a side effect.
        static bool Prefix(Dialog_FormCaravan __instance, Rect rect)
        {
            if (Multiplayer.InInterface && __instance is CaravanFormingProxy dialog)
            {
                rect.yMin += 37f;
                rect.height = 35f;

                bool autoSelectFoodAndMedicine = dialog.autoSelectTravelSupplies;
                dialog.travelSuppliesTransfer.readOnly = autoSelectFoodAndMedicine;

                Widgets.CheckboxLabeled(rect, "AutomaticallySelectTravelSupplies".Translate(), ref dialog.autoSelectTravelSupplies, placeCheckboxNearText: true);

                if (autoSelectFoodAndMedicine != dialog.autoSelectTravelSupplies)
                    dialog.Session?.SetAutoSelectTravelSupplies(dialog.autoSelectTravelSupplies);

                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_FormCaravan), nameof(Dialog_FormCaravan.TryFormAndSendCaravan))]
    static class TryFormAndSendCaravanPatch
    {
        static bool Prefix(Dialog_FormCaravan __instance)
        {
            if (Multiplayer.InInterface && __instance is CaravanFormingProxy dialog)
            {
                dialog.Session?.TryFormAndSendCaravan();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_FormCaravan), nameof(Dialog_FormCaravan.DebugTryFormCaravanInstantly))]
    static class DebugTryFormCaravanInstantlyPatch
    {
        static bool Prefix(Dialog_FormCaravan __instance)
        {
            if (Multiplayer.InInterface && __instance is CaravanFormingProxy dialog)
            {
                dialog.Session?.DebugTryFormCaravanInstantly();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_FormCaravan), nameof(Dialog_FormCaravan.TryReformCaravan))]
    static class TryReformCaravanPatch
    {
        static bool Prefix(Dialog_FormCaravan __instance)
        {
            if (Multiplayer.InInterface && __instance is CaravanFormingProxy dialog)
            {
                dialog.Session?.TryReformCaravan();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_FormCaravan), nameof(Dialog_FormCaravan.Notify_ChoseRoute))]
    static class Notify_ChoseRoutePatch
    {
        static bool Prefix(Dialog_FormCaravan __instance, int destinationTile)
        {
            if (Multiplayer.InInterface && __instance is CaravanFormingProxy dialog)
            {
                dialog.Session?.ChooseRoute(destinationTile);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    static class CancelDialogFormCaravan
    {
        static bool Prefix(Window window)
        {
            if (Multiplayer.Client != null && window.GetType() == typeof(Dialog_FormCaravan))
                return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_FormCaravan), MethodType.Constructor)]
    [HarmonyPatch(new[] { typeof(Map), typeof(bool), typeof(Action), typeof(bool), typeof(IntVec3?) })]
    static class DialogFormCaravanCtorPatch
    {
        static void Prefix(Dialog_FormCaravan __instance, Map map, bool reform, Action onClosed, bool mapAboutToBeRemoved, IntVec3? designatedMeetingPoint)
        {
            if (Multiplayer.Client == null)
                return;

            if (__instance.GetType() != typeof(Dialog_FormCaravan))
                return;

            // Handles showing the dialog from TimedForcedExit.CompTick -> TimedForcedExit.ForceReform
            // (note TimedForcedExit is obsolete)
            if (Multiplayer.ExecutingCmds || Multiplayer.Ticking)
            {
                var comp = map.MpComp();
                if (comp.sessionManager.GetFirstOfType<CaravanFormingSession>() == null)
                    comp.CreateCaravanFormingSession(reform, onClosed, mapAboutToBeRemoved, designatedMeetingPoint);
            }
            else // Handles opening from the interface: forming gizmos, reforming gizmos and caravan hitching spots
            {
                StartFormingCaravan(map, reform, designatedMeetingPoint);
            }
        }

        [SyncMethod]
        internal static void StartFormingCaravan(Map map, bool reform = false, IntVec3? designatedMeetingPoint = null, int? routePlannerWaypoint = null)
        {
            var comp = map.MpComp();
            var session = comp.CreateCaravanFormingSession(reform, null, false, designatedMeetingPoint);

            if (TickPatch.currentExecutingCmdIssuedBySelf)
            {
                var dialog = session.OpenWindow();
                if (routePlannerWaypoint is { } tile)
                {
                    try
                    {
                        UniqueIdsPatch.useLocalIdsOverride = true;

                        // Just to be safe
                        // RNG shouldn't be invoked but TryAddWaypoint is quite complex and calls pathfinding
                        Rand.PushState();

                        var worldRoutePlanner = Find.WorldRoutePlanner;
                        worldRoutePlanner.Start(dialog);
                        worldRoutePlanner.TryAddWaypoint(tile);
                    }
                    finally
                    {
                        Rand.PopState();
                        UniqueIdsPatch.useLocalIdsOverride = false;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(FormCaravanGizmoUtility), nameof(FormCaravanGizmoUtility.DialogFromToSettlement))]
    static class HandleFormCaravanShowRoutePlanner
    {
        static bool Prefix(Map origin, int tile)
        {
            if (Multiplayer.Client == null)
                return true;

            // Override behavior in multiplayer
            DialogFormCaravanCtorPatch.StartFormingCaravan(origin, routePlannerWaypoint: tile);

            return false;
        }
    }

    [HarmonyPatch(typeof(TimedForcedExit), nameof(TimedForcedExit.CompTick))]
    static class TimedForcedExitTickPatch
    {
        static bool Prefix(TimedForcedExit __instance)
        {
            if (Multiplayer.Client != null && __instance.parent is MapParent mapParent && mapParent.HasMap)
                return !mapParent.Map.AsyncTime().Paused;

            return true;
        }
    }
}
