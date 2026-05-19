using System;
using System.Collections.Generic;
using System.Text;
using Multiplayer.Client.Util;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

// Bottom-left pane mirroring MainTabWindow_Inspect; IInspectPane lets PaneWidthFor offset the gizmo grid.
public class PingInspectPane : Window, IInspectPane
{
    public static PingInspectPane Opened => Find.WindowStack?.WindowOfType<PingInspectPane>();

    // Matches MainTabWindow_Inspect.PaneTopY's hardcoded offset.
    private const float PaneBottomGap = 35f;

    public override Vector2 InitialSize => new(InspectPaneUtility.PaneWidthFor(this), InspectPaneUtility.PaneHeight);
    public override float Margin => 0f;

    public PingInspectPane()
    {
        layer                   = WindowLayer.GameUI;
        preventCameraMotion     = false;
        closeOnAccept           = false;
        closeOnCancel           = false;
        closeOnClickedOutside   = false;
        doCloseX                = false;
        doCloseButton           = false;
        forcePause              = false;
        drawShadow              = true;
        focusWhenOpened         = false;
        soundAppear             = null;
        soundClose              = null;
        draggable               = false;
        absorbInputAroundWindow = false;
    }

    public override void SetInitialSizeAndPosition()
    {
        var paneWidth = InspectPaneUtility.PaneWidthFor(this);
        var y = Mathf.Max(0f, UI.screenHeight - InspectPaneUtility.PaneHeight - PaneBottomGap);
        windowRect = new Rect(0f, y, paneWidth, InspectPaneUtility.PaneHeight);
    }

    private readonly List<PingInfo> cachedSelected = new();
    private string cachedPaneLabel;
    private int cachedMarkersV = -1;
    private int cachedPingsV = -1;
    private int cachedSelectionV = -1;
    private bool cachedOnPlanet;
    private int cachedMapId = int.MinValue;

    public override void DoWindowContents(Rect inRect)
    {
        // Mirror InspectPaneOnGUI: keeps RecentHeight non-zero for CameraDriver.
        RecentHeight = InspectPaneUtility.PaneHeight;

        var loc = Multiplayer.session?.locationPings;
        if (loc == null || !loc.HasSelection) return;

        // Planet: no gizmo grid - draw action buttons inline. Map: gizmos own them.
        var onPlanet = WorldRendererUtility.WorldSelected;
        var currentMapId = onPlanet ? -1 : Find.CurrentMap?.uniqueID ?? -1;
        var markersV = Multiplayer.game?.gameComp?.markersVersion ?? 0;
        var pingsV = loc.pingsVersion;
        var selectionV = loc.selectionVersion;

        if (cachedMarkersV != markersV || cachedPingsV != pingsV || cachedSelectionV != selectionV
            || cachedOnPlanet != onPlanet || cachedMapId != currentMapId)
        {
            var fresh = onPlanet
                ? PingSelectionUI.CollectSelectedOnPlanet(loc)
                : PingSelectionUI.CollectSelectedOnCurrentMap(loc);
            cachedSelected.Clear();
            cachedSelected.AddRange(fresh);
            cachedPaneLabel = cachedSelected.Count > 0 ? PaneLabel(cachedSelected) : null;
            cachedMarkersV   = markersV;
            cachedPingsV     = pingsV;
            cachedSelectionV = selectionV;
            cachedOnPlanet   = onPlanet;
            cachedMapId      = currentMapId;
        }

        var selected = cachedSelected;
        if (selected.Count == 0) return;

        // Recompute body text every frame so the "X minutes ago" portion stays accurate while the
        // pane is open. Caching the formatted string would freeze the relative time at selection.
        var bodyText = BodyText(selected);

        var rect = inRect.ContractedBy(InspectPaneUtility.PaneInnerMargin);
        rect.yMin -= 4f;
        rect.yMax += 6f;
        Widgets.BeginGroup(rect);
        try
        {
            var titleXOffset = 0f;
            if (selected.Count == 1)
            {
                var c = selected[0].BaseColor;
                Widgets.DrawBoxSolid(new Rect(0f, 4f, 4f, 26f),
                    new Color(c.r, c.g, c.b, 1f));
                titleXOffset = 10f;
            }

            var labelRect = new Rect(titleXOffset, 0f, rect.width - titleXOffset, 30f);
            using (MpStyle.Set(GameFont.Medium).Set(TextAnchor.UpperLeft))
                Widgets.Label(labelRect, cachedPaneLabel);

            // 52 = two 24-tall rows + 4-tall row gap; matches MarkerInspectTab.ActionRowH so the
            // inline action row can wrap when 5+ buttons appear (e.g. own-marker selection).
            const float ButtonRowH = 52f;
            const float ButtonRowGap = 4f;
            var bodyHeight = rect.height - 28f - (onPlanet ? ButtonRowH + ButtonRowGap : 0f);
            var bodyRect = new Rect(0f, 28f, rect.width, bodyHeight);
            using (MpStyle.Set(GameFont.Small).Set(TextAnchor.UpperLeft))
                Widgets.Label(bodyRect, bodyText);

            if (onPlanet)
            {
                var buttonRowRect = new Rect(0f, bodyRect.yMax + ButtonRowGap, rect.width, ButtonRowH);
                PingSelectionUI.DrawInlineActionsRow(buttonRowRect, selected, loc);
            }
        }
        finally
        {
            Widgets.EndGroup();
        }
    }

    // IInspectPane stubs - registration enables PaneWidthFor's WindowOfType lookup.
    public Type OpenTabType { get; set; }
    public float RecentHeight { get; set; }
    public Vector2 RequestedTabSize => new(InspectPaneUtility.PaneWidthFor(this), InspectPaneUtility.PaneHeight);
    public float PaneTopY => UI.screenHeight - InspectPaneUtility.PaneHeight - PaneBottomGap;
    public bool AnythingSelected => Multiplayer.session?.locationPings?.HasSelection ?? false;
    public bool ShouldShowSelectNextInCellButton => false;
    public bool ShouldShowPaneContents => AnythingSelected;
    public IEnumerable<InspectTabBase> CurTabs => null;

    public void DoInspectPaneButtons(Rect rect, ref float lineEndWidth) { }
    public string GetLabel(Rect rect) => "";
    public void DoPaneContents(Rect rect) { }
    public void SelectNextInCell() { }
    public void CloseOpenTab() => OpenTabType = null;
    public void Reset() => OpenTabType = null;

    private static string PaneLabel(List<PingInfo> selected)
    {
        if (selected.Count == 1)
        {
            var info = selected[0];
            var noun = info.isMarker ? MarkerNoun() : PingNoun();
            var name = info.category == PingCategory.Default
                ? noun.CapitalizeFirst()
                : $"{info.category.DisplayName()} {noun}";
            if (!string.IsNullOrEmpty(info.label))
                name += $" - {info.label}";
            return name;
        }
        return MultiCountLabel(selected.Count);
    }

    private static string BodyText(List<PingInfo> selected)
    {
        if (selected.Count == 1)
        {
            var info = selected[0];
            var sb = new StringBuilder();
            var name = string.IsNullOrEmpty(info.placedByUsername) ? "?" : info.placedByUsername;
            sb.AppendLine(MpTranslate.Fallback("MpPingSel_Attribution", $"Placed by {name}", name));

            var factionName = info.placedByFactionLoadId >= 0
                ? Find.FactionManager?.GetById(info.placedByFactionLoadId)?.Name
                : null;
            if (!string.IsNullOrEmpty(factionName))
                sb.AppendLine(MpTranslate.Fallback("MpPingSel_Faction", $"Faction: {factionName}", factionName));

            // Only markers carry a meaningful tick stamp - pings fade in seconds anyway.
            if (info.isMarker && info.placedAtTick > 0)
                sb.AppendLine(MpTranslate.Fallback("MpPingSel_PlacedAtTick", $"Placed: {FormatPlacedAt(info.placedAtTick)}",
                    FormatPlacedAt(info.placedAtTick)));

            if (!info.isMarker)
                sb.Append(MpTranslate.Fallback("MpPingSel_Fading",     "Ping will fade automatically"));
            return sb.ToString().TrimEnd();
        }

        var counts = new Dictionary<PingCategory, int>();
        var foreignMarkerCount = 0;
        foreach (var info in selected)
        {
            counts.TryGetValue(info.category, out var c);
            counts[info.category] = c + 1;
            if (info.isMarker && !LocationPings.CanDeleteMarker(info))
                foreignMarkerCount++;
        }
        var lines = new List<string>();
        foreach (var cat in PingCategoryExtensions.All)
            if (counts.TryGetValue(cat, out var n) && n > 0)
                lines.Add($"{n} × {cat.DisplayName()}");
        if (foreignMarkerCount > 0)
            lines.Add(ForeignSelectionLabel(foreignMarkerCount));
        return string.Join("\n", lines);
    }

    private static string MarkerNoun()
        => MpTranslate.Fallback("MpPingSel_MarkerNoun", "marker");
    private static string PingNoun()
        => MpTranslate.Fallback("MpPingSel_PingNoun",   "ping");

    private static string MultiCountLabel(int count)
        => MpTranslate.Fallback("MpPingSel_MultiCount", $"{count} selected", count);

    private static string ForeignSelectionLabel(int count)
        => MpTranslate.Fallback("MpPingSel_ForeignInSelection",
            count == 1
                ? "1 marker from another player (you cannot delete it)"
                : $"{count} markers from other players (you cannot delete them)",
            count);

    // Renders a marker's placedAtTick as "N hours/days ago" for recent placements, falling back
    // to an absolute game-date string for older ones. Uses 2500 ticks/hour and 60000 ticks/day
    // (vanilla TicksPerHour / TicksPerDay).
    private static string FormatPlacedAt(int placedAtTick)
    {
        var now = Find.TickManager?.TicksGame ?? 0;
        var delta = now - placedAtTick;
        // Negative delta = clock skew from a joiner whose game tick is behind; show absolute date.
        if (delta < 0) return GenDate.DateFullStringAt(placedAtTick, Vector2.zero);
        if (delta < GenDate.TicksPerHour)
        {
            var mins = Mathf.Max(1, delta / (GenDate.TicksPerHour / 60));
            return MpTranslate.Fallback("MpPingSel_MinutesAgo", $"{mins}m ago", mins);
        }
        if (delta < GenDate.TicksPerDay)
        {
            var hours = delta / GenDate.TicksPerHour;
            return MpTranslate.Fallback("MpPingSel_HoursAgo", $"{hours}h ago", hours);
        }
        if (delta < GenDate.TicksPerDay * 7)
        {
            var days = delta / GenDate.TicksPerDay;
            return MpTranslate.Fallback("MpPingSel_DaysAgo", $"{days}d ago", days);
        }
        return GenDate.DateFullStringAt(placedAtTick, Vector2.zero);
    }
}
