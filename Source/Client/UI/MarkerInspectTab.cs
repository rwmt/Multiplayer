using System.Collections.Generic;
using Multiplayer.Client.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client;

// Surfaces marker actions inside vanilla's WorldInspectPane when a tile is selected alongside markers -
// the tile pane otherwise covers our pane (see PingSelectionUI.UpdatePingInspectPaneVisibility).
public class MarkerInspectTab : InspectTabBase
{
    // labelKey is re-translated by InspectPaneUtility, so we ship the raw key + register a runtime
    // fallback (PingRuntimeTranslations); pre-translating here breaks under dev-mode pseudo-localization.
    public MarkerInspectTab()
    {
        labelKey = "MpMarkerInspectTab_Label";
        size = new Vector2(580f, 280f);
    }

    private Vector2 listScroll;

    // Returns null when no on-planet markers are selected (the tab stays hidden); otherwise the
    // shared cached list. Read directly - do not mutate. Vanilla pumps this through IsVisible /
    // StillValid every frame, so the cache must hit on unchanged state.
    public static List<PingInfo> CollectSelectedPlanetMarkers()
    {
        var loc = Multiplayer.session?.locationPings;
        if (loc == null) return null;

        var markersV = Multiplayer.game?.gameComp?.markersVersion ?? 0;
        var selectionV = loc.selectionVersion;
        if (loc.cachedPlanetMarkersV == markersV && loc.cachedPlanetSelectionV == selectionV)
            return loc.cachedPlanetMarkersHasResult ? loc.cachedPlanetMarkers : null;

        loc.cachedPlanetMarkers.Clear();
        if (loc.selectedMarkerIds.Count > 0)
        {
            foreach (var m in loc.Markers)
                if (m.mapId == -1
                    && loc.selectedMarkerIds.Contains(m.markerId)
                    && m.IsVisible())
                    loc.cachedPlanetMarkers.Add(m);
        }
        loc.cachedPlanetMarkersHasResult = loc.cachedPlanetMarkers.Count > 0;
        loc.cachedPlanetMarkersV = markersV;
        loc.cachedPlanetSelectionV = selectionV;
        return loc.cachedPlanetMarkersHasResult ? loc.cachedPlanetMarkers : null;
    }

    public override bool IsVisible => CollectSelectedPlanetMarkers() != null;

    public override float PaneTopY
    {
        get
        {
            // Same anchor as WorldInspectPane.
            const float PaneHeight = 165f;
            const float PaneBottomGap = 35f;
            return UI.screenHeight - PaneHeight - PaneBottomGap;
        }
    }

    public override bool StillValid => CollectSelectedPlanetMarkers() != null;

    // Match vanilla WITab: X collapses the tab, marker selection stays (Deselect clears it).
    public override void CloseTab()
    {
        Find.World?.UI?.inspectPane?.CloseOpenTab();
        SoundDefOf.TabClose.PlayOneShotOnCamera();
    }

    public override void FillTab()
    {
        var loc = Multiplayer.session?.locationPings;
        if (loc == null) return;

        var selected = CollectSelectedPlanetMarkers();
        if (selected == null) return;

        const float Pad = 8f;
        var inner = new Rect(Pad, Pad, size.x - Pad * 2f, size.y - Pad * 2f);

        var headerRect = new Rect(inner.x, inner.y, inner.width, 22f);
        using (MpStyle.Set(GameFont.Small).Set(TextAnchor.UpperLeft))
            Widgets.Label(headerRect, HeaderLabel(selected.Count));

        // Bottom action row reserved first so the list shrinks to fill remaining space. Height
        // covers two rows so DrawInlineActionsRow can wrap when 5+ buttons appear (own-marker
        // case: Delete, Rename, Fade, Hide-for-me, Deselect).
        const float ActionRowH = 56f;
        const float Gap = 6f;
        var actionRowRect = new Rect(inner.x, inner.yMax - ActionRowH, inner.width, ActionRowH);
        PingSelectionUI.DrawInlineActionsRow(actionRowRect, selected, loc);

        var listRect = new Rect(inner.x, headerRect.yMax + Gap,
            inner.width, actionRowRect.y - headerRect.yMax - Gap * 2f);
        DrawMarkerList(listRect, selected, loc);
    }

    private const float RowH = 26f;

    private void DrawMarkerList(Rect outRect, List<PingInfo> markers, LocationPings loc)
    {
        var viewH = markers.Count * RowH + 4f;
        var viewRect = new Rect(0f, 0f, outRect.width - 16f, viewH);
        Widgets.BeginScrollView(outRect, ref listScroll, viewRect);

        var stride = RowH;
        var firstVisible = Mathf.Max(0, (int)(listScroll.y / stride) - 1);
        var lastVisible = Mathf.Min(markers.Count, firstVisible + (int)(outRect.height / stride) + 3);
        for (var i = firstVisible; i < lastVisible; i++)
        {
            var rowRect = new Rect(0f, i * stride, viewRect.width, stride - 2f);
            DrawMarkerRow(rowRect, markers[i], loc);
        }

        Widgets.EndScrollView();
    }

    private static void DrawMarkerRow(Rect rect, PingInfo info, LocationPings loc)
    {
        var isSelected = loc.IsMarkerSelected(info.markerId);
        Widgets.DrawHighlightIfMouseover(rect);
        if (isSelected) Widgets.DrawHighlightSelected(rect);

        // 4px placer color stripe.
        var stripe = info.BaseColor;
        Widgets.DrawBoxSolid(new Rect(rect.x + 2f, rect.y + 3f, 4f, rect.height - 6f),
            new Color(stripe.r, stripe.g, stripe.b, 0.95f));

        // Category icon.
        var iconRect = new Rect(rect.x + 12f, rect.y + 4f, 18f, 18f);
        var iconTex = info.category.Icon();
        if (iconTex != null)
        {
            using (MpStyle.Set(info.category.Tint()))
                GUI.DrawTexture(iconRect, iconTex);
        }

        // Show "Category - Label" if the marker has a user label, otherwise just the category name.
        var primary = string.IsNullOrEmpty(info.label)
            ? info.category.DisplayName()
            : $"{info.category.DisplayName()} - {info.label}";

        var placer = info.placedByUsername ?? "?";
        var factionName = info.placedByFactionLoadId >= 0
            ? Find.FactionManager?.GetById(info.placedByFactionLoadId)?.Name
            : null;
        var secondary = string.IsNullOrEmpty(factionName) ? placer : $"{placer}  ·  {factionName}";

        // NoWrap - otherwise a long player or faction name wraps at the " · " separator and stacks vertically.
        const float SecondaryW = PingInfo.LabelWidth;
        var primaryRect = new Rect(iconRect.xMax + 6f, rect.y, rect.width - iconRect.xMax - 6f - SecondaryW - 6f, rect.height);
        using (MpStyle.Set(GameFont.Small).Set(TextAnchor.MiddleLeft).Set(WordWrap.NoWrap))
            Widgets.Label(primaryRect, primary);

        var secondaryRect = new Rect(rect.xMax - SecondaryW - 4f, rect.y, SecondaryW, rect.height);
        using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.MiddleRight).Set(WordWrap.NoWrap).Set(new Color(0.75f, 0.75f, 0.75f)))
            Widgets.Label(secondaryRect, secondary);

        if (Widgets.ButtonInvisible(rect))
        {
            var shift = Selector.ShiftIsHeld;
            if (shift)
            {
                if (isSelected) loc.ToggleSelection(info);
                else loc.SelectInfo(info, additive: true);
            }
            else
            {
                loc.SelectInfo(info, additive: false);
            }
            SoundDefOf.Click.PlayOneShotOnCamera();
        }
    }

    private static string HeaderLabel(int count)
        => MpTranslate.Fallback("MpMarkerInspectTab_Header",
            count == 1 ? "1 marker on selected tile" : $"{count} markers on selected tile",
            count);
}
