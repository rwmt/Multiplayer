using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client
{
    // Click-on-marker selection + armed placement.
    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.HandleMapClicks))]
    static class PingMapClickPatch
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        static bool Prefix()
        {
            if (Multiplayer.Client == null || Multiplayer.arbiterInstance || TickPatch.Simulating) return true;
            if (Find.CurrentMap == null) return true;
            // CurrentMap stays non-null on planet view - without this, armed LMB projects onto the hidden map camera.
            if (WorldRendererUtility.WorldSelected) return true;

            if (Find.DesignatorManager?.SelectedDesignator != null) return true;
            if ((Find.Targeter?.IsTargeting ?? false)
                || (Find.WorldTargeter?.IsTargeting ?? false)) return true;

            var ev = Event.current;
            var loc = Multiplayer.session?.locationPings;
            if (loc == null) return true;
            var mouse = UI.MousePositionOnUIInverted;
            var size = LocationPings.OnScreenPingSize;

            var onWheelUi = IsMouseOverWheelOrDrawer(loc, mouse);

            // Swallow LMB on the wheel so vanilla doesn't world-select behind it.
            if (ev.type == EventType.MouseDown && ev.button == 0 && onWheelUi)
            {
                ev.Use();
                return false;
            }

            if (ev.type == EventType.MouseDown && ev.button == 1 && loc.armedCategory != null && !onWheelUi)
            {
                loc.DisarmPlacement();
                ev.Use();
                return false;
            }

            if (ev.type == EventType.MouseDown && ev.button == 0 && loc.armedCategory != null && !onWheelUi)
            {
                var mapLoc = UI.MouseMapPosition();
                if (loc.FireArmedAtMap(Find.CurrentMap.uniqueID, PlanetTile.Invalid, mapLoc))
                {
                    ev.Use();
                    return false;
                }
            }

            // Double-click on MouseDown is safe (no drag-box); single-click lives in PingSelectUnderMousePatch on MouseUp.
            if (ev.type == EventType.MouseDown && ev.button == 0 && ev.clickCount == 2)
            {
                if (TryHitTest(loc, mouse, size, out var hit) && hit.isMarker)
                {
                    if (!Selector.ShiftIsHeld)
                        Find.Selector?.ClearSelection();
                    SelectAllMatchingMarkersOnScreen(loc, hit);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    ev.Use();
                    return false;
                }
            }

            return true;
        }

        private static void SelectAllMatchingMarkersOnScreen(LocationPings loc, PingInfo hit)
        {
            loc.ClearSelection();

            var screenRect = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight);
            var mapId = Find.CurrentMap.uniqueID;

            foreach (var m in loc.Markers)
            {
                if (m.mapId != mapId) continue;
                if (m.category != hit.category) continue;
                if (!screenRect.Contains(m.mapLoc.MapToUIPosition())) continue;
                loc.SelectInfo(m, additive: true);
            }
        }

        internal static bool IsMouseOverWheelOrDrawer(LocationPings loc, Vector2 mouse)
        {
            // Wheel area is only "over UI" while up - otherwise the last wheel position would block marker clicks forever.
            if (loc.wheelActive)
            {
                var dx = mouse.x - loc.wheelScreenOrigin.x;
                var dy = mouse.y - loc.wheelScreenOrigin.y;
                const float ChevronStackH = 32f;
                if (Mathf.Abs(dx) <= LocationPings.WheelBackdropR
                    && dy >= -(LocationPings.WheelBackdropR + ChevronStackH)
                    && dy <= LocationPings.WheelBackdropR)
                    return true;
            }

            // PingInspectPane intentionally excluded; its body click is a no-op and markers under it must stay clickable.
            var w = Find.WindowStack?.GetWindowAt(mouse);
            return w is PingMenuWindow or PingFiltersDialog or PingHostSettingsDialog;
        }

        internal static bool TryHitTest(LocationPings loc, Vector2 mouse, float size, out PingInfo hit)
        {
            var mapId = Find.CurrentMap.uniqueID;

            foreach (var m in loc.Markers)
            {
                if (m.mapId != mapId) continue;
                if (!m.IsVisible()) continue;
                if (HitMarker(m, mouse, size)) { hit = m; return true; }
            }
            for (var i = loc.pings.Count - 1; i >= 0; i--)
            {
                var p = loc.pings[i];
                if (p.mapId != mapId) continue;
                if (p.PlayerInfo == null) continue;
                if (!p.IsVisible()) continue;
                if (HitMarker(p, mouse, size)) { hit = p; return true; }
            }

            hit = null;
            return false;
        }

        // DrawAt anchors the pin above mapLoc; a circle test at mapLoc would miss the pin.
        private static bool HitMarker(PingInfo info, Vector2 mouse, float size)
        {
            var screen = info.mapLoc.MapToUIPosition();
            var halfW = size * 0.6f;
            if (Math.Abs(mouse.x - screen.x) > halfW) return false;
            var pinTop = screen.y - size - info.y * size;
            var ringBot = screen.y + size * 0.6f;
            return mouse.y >= pinTop && mouse.y <= ringBot;
        }
    }

    // Inject marker-action gizmos - DrawGizmoGridFor accepts any Gizmo in selectedObjects.
    [HarmonyPatch(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGridFor))]
    static class PingGizmoInjectPatch
    {
        [HarmonyPriority(MpPriority.MpLast)]
        static void Prefix(ref IEnumerable<object> selectedObjects)
        {
            if (Multiplayer.Client == null || Multiplayer.arbiterInstance || TickPatch.Simulating) return;
            if (WorldRendererUtility.WorldSelected) return;
            var loc = Multiplayer.session?.locationPings;
            if (loc == null || !loc.HasSelection) return;

            var selected = PingSelectionUI.CollectSelectedOnCurrentMap(loc);
            if (selected.Count == 0) return;

            var gizmos = PingSelectionUI.BuildGizmos(selected, loc);
            if (gizmos.Count == 0) return;

            // Materialize into a per-session reused buffer so we don't allocate every frame, and
            // so a later patch can't mutate the source between our return and vanilla's AddRange.
            var buffer = loc.gizmoInjectionBuffer;
            buffer.Clear();
            buffer.AddRange(selectedObjects);
            foreach (var g in gizmos) buffer.Add(g);
            selectedObjects = buffer;
        }
    }

    // Marker equivalent of vanilla's plain-clear-on-click; runs on MouseUp so drag-box can still start near a marker.
    [HarmonyPatch]
    static class PingSelectUnderMousePatch
    {
        // Prepare()=false is canonical skip - returning null from TargetMethod() trips PatchClassProcessor.
        static bool Prepare()
        {
            if (AccessTools.Method(typeof(Selector), "SelectUnderMouse", Type.EmptyTypes) == null)
            {
                Log.Warning("[Multiplayer] PingSelectUnderMousePatch: Selector.SelectUnderMouse not found; marker selection won't follow vanilla's MouseUp-without-drag clear.");
                return false;
            }
            return true;
        }

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Selector), "SelectUnderMouse", Type.EmptyTypes);
        }

        static void Postfix()
        {
            if (PingDragBoxSelectPatch.InsideDragBox) return;
            if (Multiplayer.Client == null || Multiplayer.arbiterInstance || TickPatch.Simulating) return;
            if (Find.CurrentMap == null) return;
            if (WorldRendererUtility.WorldSelected) return;
            var loc = Multiplayer.session?.locationPings;
            if (loc == null) return;

            var mouse = UI.MousePositionOnUIInverted;
            var size = LocationPings.OnScreenPingSize;
            var shift = Selector.ShiftIsHeld;

            if (PingMapClickPatch.TryHitTest(loc, mouse, size, out var hit))
            {
                if (!shift) Find.Selector?.ClearSelection();

                var alreadySelected = hit.isMarker
                    ? loc.IsMarkerSelected(hit.markerId)
                    : loc.IsPingSelected(hit.player);
                if (shift && alreadySelected)
                    loc.ToggleSelection(hit);
                else
                    loc.SelectInfo(hit, additive: shift);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            else if (!shift && loc.HasSelection)
            {
                loc.ClearSelection();
            }
        }
    }

    // Planet-view armed-placement. Vanilla HandleWorldClicks consumes LMB MouseDown for drag-box,
    // so a postfix on SelectUnderMouse is too late.
    [HarmonyPatch(typeof(WorldSelector), "HandleWorldClicks")]
    static class PingWorldClickPatch
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        static bool Prefix()
        {
            if (Multiplayer.Client == null || Multiplayer.arbiterInstance || TickPatch.Simulating) return true;
            if (!WorldRendererUtility.WorldSelected) return true;
            if ((Find.WorldTargeter?.IsTargeting ?? false)
                || (Find.Targeter?.IsTargeting ?? false)
                || Find.DesignatorManager?.SelectedDesignator != null) return true;

            var loc = Multiplayer.session?.locationPings;
            if (loc?.armedCategory == null) return true;

            var mouse = UI.MousePositionOnUIInverted;
            if (PingMapClickPatch.IsMouseOverWheelOrDrawer(loc, mouse)) return true;

            var ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.button == 1)
            {
                loc.DisarmPlacement();
                ev.Use();
                return false;
            }
            if (ev.type == EventType.MouseDown && ev.button == 0)
            {
                var tile = GenWorld.MouseTile();
                if (!tile.Valid) tile = GenWorld.MouseTile(true);
                if (tile.Valid && loc.FireArmedAtMap(-1, tile, Vector3.zero))
                {
                    ev.Use();
                    return false;
                }
            }
            return true;
        }
    }

    // Planet-view companion to PingMapClickPatch: pull markers on the clicked tile into selection so PingInspectPane shows them.
    [HarmonyPatch(typeof(WorldSelector), "SelectUnderMouse")]
    static class PingPlanetSelectUnderMousePatch
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null || Multiplayer.arbiterInstance || TickPatch.Simulating) return;
            if (!WorldRendererUtility.WorldSelected) return;
            var loc = Multiplayer.session?.locationPings;
            if (loc == null) return;

            var tile = GenWorld.MouseTile();
            if (!tile.Valid) return;

            // KeyNotFoundException-safe: PlanetTile.Layer throws on unknown layerIds.
            try
            {
                if (tile.Layer == null) return;
            }
            catch (KeyNotFoundException)
            {
                return;
            }

            var shift = Selector.ShiftIsHeld;
            if (!shift) loc.ClearSelection();

            var anyHit = false;
            foreach (var m in loc.Markers)
            {
                if (m.mapId != -1) continue;
                if (m.planetTile != tile) continue;
                if (!m.IsVisible()) continue;
                loc.SelectInfo(m, additive: true);
                anyHit = true;
            }
            foreach (var p in loc.pings)
            {
                if (p.mapId != -1) continue;
                if (p.planetTile != tile) continue;
                if (p.PlayerInfo == null) continue;
                if (!p.IsVisible()) continue;
                loc.SelectInfo(p, additive: true);
                anyHit = true;
            }

            // Empty-tile click leaves an empty selection - PingInspectPane stays closed.
            if (!anyHit && !shift && loc.HasSelection)
                loc.ClearSelection();
        }
    }

    // Append MarkerInspectTab so the vanilla tile inspector stays visible alongside marker actions.
    [HarmonyPatch(typeof(WorldInspectPane), nameof(WorldInspectPane.CurTabs), MethodType.Getter)]
    static class PingWorldInspectPaneCurTabsPatch
    {
        // Reused across frames so the per-frame getter doesn't allocate a fresh list and Concat
        // iterator. Vanilla calls CurTabs from PaneWidthFor / UpdateTabs / ExtraOnGUI; each call
        // consumes the result fully before the next, so reuse is safe.
        private static readonly List<InspectTabBase> mergedTabs = new();

        static void Postfix(ref IEnumerable<InspectTabBase> __result)
        {
            if (Multiplayer.Client == null || Multiplayer.arbiterInstance || TickPatch.Simulating) return;
            if (MarkerInspectTab.CollectSelectedPlanetMarkers() == null) return;
            var tab = InspectTabManager.GetSharedInstance(typeof(MarkerInspectTab));

            mergedTabs.Clear();
            if (__result != null) mergedTabs.AddRange(__result);
            mergedTabs.Add(tab);
            __result = mergedTabs;
        }
    }

    // Suppress vanilla's bell/alert-bounce - ReceivePing plays our per-category sound and on-map cue.
    [HarmonyPatch(typeof(Alert), nameof(Alert.Notify_Started))]
    static class AlertPingNotifyStartedPatch
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        static bool Prefix(Alert __instance)
        {
            if (Multiplayer.Client == null || Multiplayer.arbiterInstance) return true;
            return __instance is not AlertPing;
        }
    }

    // Drag-box completion: pull markers/pings inside the rect into the selection.
    [HarmonyPatch(typeof(Selector), nameof(Selector.SelectInsideDragBox))]
    static class PingDragBoxSelectPatch
    {
        // True while SelectInsideDragBox is on the stack - lets PingSelectUnderMousePatch skip its hit-test on vanilla's internal call.
        public static bool InsideDragBox;

        static void Prefix() => InsideDragBox = true;
        static void Finalizer() => InsideDragBox = false;

        // MpLast - let other patches finish selecting vanilla objects before we read NumSelected.
        [HarmonyPriority(MpPriority.MpLast)]
        static void Postfix(Selector __instance)
        {
            if (Multiplayer.Client == null || Multiplayer.arbiterInstance || TickPatch.Simulating) return;
            if (Find.CurrentMap == null) return;
            if (WorldRendererUtility.WorldSelected) return;

            if (Find.DesignatorManager?.SelectedDesignator != null) return;
            if ((Find.Targeter?.IsTargeting ?? false)
                || (Find.WorldTargeter?.IsTargeting ?? false)) return;

            var loc = Multiplayer.session?.locationPings;
            if (loc == null) return;
            var rect = __instance.dragBox.ScreenRect;
            var mapId = Find.CurrentMap.uniqueID;

            if (!Selector.ShiftIsHeld) loc.ClearSelection();

            // Markers always join drag-selection regardless of vanilla picks - use the
            // Deselect gizmo / inline action to drop them from a mixed selection.
            foreach (var m in loc.Markers)
            {
                if (m.mapId != mapId) continue;
                if (!m.IsVisible()) continue;
                if (rect.Contains(m.mapLoc.MapToUIPosition()))
                    loc.SelectInfo(m, additive: true);
            }
            for (var i = 0; i < loc.pings.Count; i++)
            {
                var p = loc.pings[i];
                if (p.mapId != mapId) continue;
                if (p.PlayerInfo == null) continue;
                if (!p.IsVisible()) continue;
                if (rect.Contains(p.mapLoc.MapToUIPosition()))
                    loc.SelectInfo(p, additive: true);
            }
        }
    }
}
