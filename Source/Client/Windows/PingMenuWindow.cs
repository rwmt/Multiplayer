using System.Collections.Generic;
using Multiplayer.Client.Comp;
using Multiplayer.Client.Util;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client;

// Draggable drawer: wheel on the left, mode toggle + clear buttons + placed-items list on the right.
public class PingMenuWindow : Window
{
    public static PingMenuWindow Opened => Find.WindowStack?.WindowOfType<PingMenuWindow>();

    public override Vector2 InitialSize => new(820f, 540f);

    private Vector2 listScroll;

    // Cache keys are (markersVersion, pingsVersion); filter toggles don't invalidate because
    // BuildRows / MyMarker* don't call IsVisible (filters apply downstream in render gates).
    private List<PingInfo> cachedRows;
    private int cachedRowsMarkersVersion = -1;
    private int cachedRowsPingsVersion = -1;
    private int cachedMyMarkerCount;
    private int cachedMyMarkerCountVersion = -1;
    private int cachedMyMarkersOnMap;
    private int cachedMyMarkersOnMapVersion = -1;
    private int cachedMyMarkersOnMapMapId = -1;

    private const float WheelSectionW  = 440f;
    private const float SectionGap     = 14f;

    public PingMenuWindow()
    {
        draggable = true;
        resizeable = false;
        closeOnClickedOutside = false;
        closeOnAccept = false;
        closeOnCancel = true;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
        focusWhenOpened = true;
        onlyOneOfTypeAllowed = true;
        soundClose = SoundDefOf.FloatMenu_Cancel;
        doCloseX = true;
        layer = WindowLayer.GameUI;
    }

    public override void PostOpen()
    {
        base.PostOpen();
        if (!Multiplayer.settings.rememberLastCategory) return;
        var loc = Multiplayer.session?.locationPings;
        if (loc == null) return;
        if (loc.armedCategory != null) return;
        if (loc.lastUsedCategory is { } cat)
            loc.ArmPlacement(cat, playSound: false);
    }

    private const float WindowInnerMargin = 18f;
    private const float HeaderBlockH     = 56f;

    public override void SetInitialSizeAndPosition()
    {
        var size = InitialSize;
        var screen = new Vector2(UI.screenWidth, UI.screenHeight);
        const float ScreenMargin = 6f;

        var saved = Multiplayer.settings.pingMenuWindowRect;
        // Validate against current size - a future InitialSize change must not let a stale rect
        // bypass the clamp below.
        if (saved.width > 0f && saved.height > 0f
            && saved.x >= -size.x + ScreenMargin && saved.x <= screen.x - ScreenMargin
            && saved.y >= -size.y + ScreenMargin && saved.y <= screen.y - ScreenMargin)
        {
            windowRect = new Rect(saved.x, saved.y, size.x, size.y);
            return;
        }

        var loc = Multiplayer.session?.locationPings;
        var cursorWheelCenter = loc?.wheelScreenOrigin ?? new Vector2(screen.x / 2f, screen.y / 2f);

        var wheelLocalCenterX = WindowInnerMargin + WheelSectionW / 2f;
        var bodyHeight        = size.y - 2 * WindowInnerMargin - HeaderBlockH;
        var wheelLocalCenterY = WindowInnerMargin + HeaderBlockH + bodyHeight / 2f;

        var desiredX = cursorWheelCenter.x - wheelLocalCenterX;
        var desiredY = cursorWheelCenter.y - wheelLocalCenterY;

        var x = Mathf.Clamp(desiredX, ScreenMargin, screen.x - size.x - ScreenMargin);
        var y = Mathf.Clamp(desiredY, ScreenMargin, screen.y - size.y - ScreenMargin);

        windowRect = new Rect(x, y, size.x, size.y);
    }

    public override void PostClose()
    {
        base.PostClose();
        Multiplayer.session?.locationPings?.DisarmPlacement(playSound: false);
        Multiplayer.settings.pingMenuWindowRect = windowRect;
        MultiplayerLoader.Multiplayer.instance?.WriteSettings();
    }

    public override void DoWindowContents(Rect inRect)
    {
        const float CloseXReserve = 30f;
        const float HeaderBtnGap = 6f;
        const float FiltersBtnW = 90f;
        const float HostBtnW = 120f; // wide enough for "Host Settings"

        var isHost = Multiplayer.LocalServer != null;
        var hostBtnReserve = isHost ? HostBtnW + HeaderBtnGap : 0f;

        var titleRect = new Rect(inRect.x, inRect.y,
            inRect.width - CloseXReserve - FiltersBtnW - HeaderBtnGap - hostBtnReserve, 28f);
        using (MpStyle.Set(GameFont.Medium).Set(TextAnchor.MiddleLeft))
            Widgets.Label(titleRect, MpTranslate.Fallback("MpPingMenuWindow_Header",        "Ping & marker menu"));

        var filtersBtnRect = new Rect(titleRect.xMax + HeaderBtnGap, inRect.y + 2f,
            FiltersBtnW, 24f);
        if (Widgets.ButtonText(filtersBtnRect, MpTranslate.Fallback("MpPingFilters_OpenBtn", "Filters")))
        {
            // To screen-space for dialog anchor.
            var anchor = new Rect(
                windowRect.x + filtersBtnRect.x,
                windowRect.y + filtersBtnRect.y,
                filtersBtnRect.width,
                filtersBtnRect.height);
            if (PingFiltersDialog.Opened == null)
                Find.WindowStack.Add(new PingFiltersDialog(anchor));
            else
                PingFiltersDialog.Opened.Close();
            SoundDefOf.Click.PlayOneShotOnCamera();
            // Stop the click falling through to GUI.DragWindow.
            Event.current.Use();
        }
        TooltipHandler.TipRegion(filtersBtnRect, PingFiltersDialog.OpenTooltipLabel());

        if (isHost)
        {
            var hostBtnRect = new Rect(filtersBtnRect.xMax + HeaderBtnGap, inRect.y + 2f,
                HostBtnW, 24f);
            if (Widgets.ButtonText(hostBtnRect, MpTranslate.Fallback("MpPingHostSettings_OpenBtn", "Host Settings")))
            {
                var anchor = new Rect(
                    windowRect.x + hostBtnRect.x,
                    windowRect.y + hostBtnRect.y,
                    hostBtnRect.width,
                    hostBtnRect.height);
                if (PingHostSettingsDialog.Opened == null)
                    Find.WindowStack.Add(new PingHostSettingsDialog(anchor));
                else
                    PingHostSettingsDialog.Opened.Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
                Event.current.Use();
            }
            TooltipHandler.TipRegion(hostBtnRect, PingHostSettingsDialog.OpenTooltipLabel());
        }

        var subtitleRect = new Rect(inRect.x, titleRect.yMax + 2f,
            inRect.width - CloseXReserve, 18f);
        using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.MiddleLeft).Set(new Color(0.7f, 0.7f, 0.7f)))
            Widgets.Label(subtitleRect, SubtitleLabel());

        var bodyTop = subtitleRect.yMax + 8f;
        var body = new Rect(inRect.x, bodyTop, inRect.width, inRect.yMax - bodyTop);

        var wheelSection = new Rect(body.x, body.y, WheelSectionW, body.height);
        var contentSection = new Rect(wheelSection.xMax + SectionGap, body.y,
            body.width - WheelSectionW - SectionGap, body.height);

        var dividerX = wheelSection.xMax + SectionGap / 2f;
        Widgets.DrawLineVertical(dividerX, body.y + 4f, body.height - 8f);

        DrawWheelSection(wheelSection);
        DrawContentSection(contentSection);
    }

    private void DrawWheelSection(Rect section)
    {
        var loc = Multiplayer.session?.locationPings;
        if (loc == null) return;

        var wheelCenterLocal = new Vector2(section.center.x, section.center.y);

        // Sync screen-space center so DrawArmedCursor and PingMapClickPatch hit-test correctly.
        loc.wheelScreenOrigin = new Vector2(
            windowRect.x + WindowInnerMargin + wheelCenterLocal.x,
            windowRect.y + WindowInnerMargin + wheelCenterLocal.y);

        loc.DrawWheelInDrawer(wheelCenterLocal, Event.current.mousePosition);
    }

    private void DrawContentSection(Rect section)
    {
        var y = section.y;

        var headerRect = new Rect(section.x, y, section.width, 16f);
        using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.MiddleLeft).Set(new Color(0.65f, 0.65f, 0.65f)))
            Widgets.Label(headerRect, MpTranslate.Fallback("MpPingMenuWindow_PlacementType", "Placement type"));
        y = headerRect.yMax + 2f;

        var modeRow = new Rect(section.x, y, section.width, 32f);
        DrawModeRow(modeRow);
        y = modeRow.yMax + 3f;

        var modeDescRect = new Rect(section.x, y, section.width, 14f);
        using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.MiddleLeft).Set(WordWrap.NoWrap).Set(new Color(0.78f, 0.78f, 0.78f)))
            Widgets.Label(modeDescRect, MpTranslate.Fallback("MpPingMode_" + Multiplayer.settings.pingPlaceMode + "_Description",
                Multiplayer.settings.pingPlaceMode switch
                {
                    PingPlaceMode.Ping   => "Briefly flash a spot to call attention. Fades on its own.",
                    PingPlaceMode.Marker => "Drop a pin to mark a spot. Stays until you remove it.",
                    _ => "",
                }));
        y = modeDescRect.yMax + 10f;

        DrawActionStack(section, ref y);
        y += 10f;

        Widgets.DrawLineHorizontal(section.x, y, section.width);
        y += 6f;

        var cap = Multiplayer.game?.gameComp?.markerCapPerPlayer ?? PingMarkerCap.Default;
        var listHeaderRect = new Rect(section.x, y, section.width, 20f);
        var markerCount = MyMarkerCount();
        using (MpStyle.Set(GameFont.Small).Set(TextAnchor.MiddleLeft))
            Widgets.Label(listHeaderRect, MpTranslate.Fallback("MpPingMenuWindow_ListHeaderWithCount",
                $"Your pings & markers ({markerCount}/{cap})",
                markerCount, cap));
        y = listHeaderRect.yMax + 4f;

        var listRect = new Rect(section.x, y, section.width, section.yMax - y);
        DrawList(listRect);
    }

    private static void DrawModeRow(Rect row)
    {
        var mode = Multiplayer.settings.pingPlaceMode;
        var leftRect = new Rect(row.x, row.y, row.width / 2f - 3f, row.height);
        var rightRect = new Rect(row.x + row.width / 2f + 3f, row.y, row.width / 2f - 3f, row.height);

        if (DrawModeTabButton(leftRect, ModeLabel(PingPlaceMode.Ping), PingPlaceMode.Ping, mode == PingPlaceMode.Ping)
            && mode != PingPlaceMode.Ping)
        {
            Multiplayer.settings.pingPlaceMode = PingPlaceMode.Ping;
            MultiplayerLoader.Multiplayer.instance?.WriteSettings();
            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
        }
        if (DrawModeTabButton(rightRect, ModeLabel(PingPlaceMode.Marker), PingPlaceMode.Marker, mode == PingPlaceMode.Marker)
            && mode != PingPlaceMode.Marker)
        {
            Multiplayer.settings.pingPlaceMode = PingPlaceMode.Marker;
            MultiplayerLoader.Multiplayer.instance?.WriteSettings();
            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
        }
    }

    private void DrawActionStack(Rect section, ref float y)
    {
        var myCount = MyMarkerCount();
        var hasMap = Find.CurrentMap != null;
        var onMapCount = hasMap ? MyMarkersOnCurrentMap() : 0;

        var loc = Multiplayer.session?.locationPings;
        const float ButtonH = 28f;
        var clearMineRect = new Rect(section.x, y, section.width, ButtonH);
        if (Widgets.ButtonText(clearMineRect, MpTranslate.Fallback("MpPingDrawer_ClearAllMine",
                myCount > 0 ? $"Clear all my markers ({myCount})" : "Clear all my markers",
                myCount), active: myCount > 0))
        {
            loc?.SendClearMyMarkers();
            SoundDefOf.Click.PlayOneShotOnCamera();
        }
        y = clearMineRect.yMax + 4f;

        var clearOnMapRect = new Rect(section.x, y, section.width, ButtonH);
        if (Widgets.ButtonText(clearOnMapRect, MpTranslate.Fallback("MpPingDrawer_ClearMineOnThisMap",
                onMapCount > 0 ? $"Clear my markers on this map ({onMapCount})" : "Clear my markers on this map",
                onMapCount), active: hasMap && onMapCount > 0))
        {
            loc?.SendClearMyMarkersOnMap(Find.CurrentMap.uniqueID);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }
        y = clearOnMapRect.yMax;
    }

    private void DrawList(Rect outRect)
    {
        var rows = BuildRows();
        const float rowHeight = 36f;
        const float rowGap = 2f;
        var viewRectHeight = rows.Count * (rowHeight + rowGap) + 4f;
        var viewRect = new Rect(0f, 0f, outRect.width - 16f, viewRectHeight);

        Widgets.BeginScrollView(outRect, ref listScroll, viewRect);

        if (rows.Count == 0)
        {
            using (MpStyle.Set(GameFont.Small).Set(TextAnchor.MiddleCenter).Set(new Color(0.7f, 0.7f, 0.7f)))
                Widgets.Label(new Rect(0f, 8f, viewRect.width, 32f), MpTranslate.Fallback("MpPingMenuWindow_Empty",         "(none yet)"));
        }
        else
        {
            // Clip to visible viewport with a 1-row buffer so fast scrolling doesn't show pop-in.
            var stride = rowHeight + rowGap;
            var firstVisible = Mathf.Max(0, (int)(listScroll.y / stride) - 1);
            var lastVisible  = Mathf.Min(rows.Count, firstVisible + (int)(outRect.height / stride) + 3);
            for (var i = firstVisible; i < lastVisible; i++)
            {
                var rowRect = new Rect(0f, i * stride, viewRect.width, rowHeight);
                DrawListRow(rowRect, rows[i]);
            }
        }

        Widgets.EndScrollView();
    }

    private void DrawListRow(Rect rect, PingInfo info)
    {
        var isSelected = info.isMarker
            ? Multiplayer.session.locationPings.IsMarkerSelected(info.markerId)
            : Multiplayer.session.locationPings.IsPingSelected(info.player);

        Widgets.DrawHighlightIfMouseover(rect);
        if (isSelected)
            Widgets.DrawHighlightSelected(rect);

        var stripe = info.BaseColor;
        Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), new Color(stripe.r, stripe.g, stripe.b, 0.95f));

        var iconRect = new Rect(rect.x + 8f, rect.y + 4f, 28f, 28f);
        var iconTex = info.category.Icon();
        if (iconTex != null)
            GUI.DrawTexture(iconRect, iconTex);

        // 154 = Rename(76) + gap(6) + Delete(64) + 8 margin. Pings auto-fade, so no action row.
        var actionsReserved = info.isMarker ? 154f : 0f;
        var bodyRect = new Rect(iconRect.xMax + 6f, rect.y + 2f, rect.width - iconRect.xMax - 6f - actionsReserved, rect.height - 4f);
        var primary = string.IsNullOrEmpty(info.label)
            ? info.category.DisplayName()
            : $"{info.category.DisplayName()} - {info.label}";
        var secondary = $"{TargetDescription(info)} · {info.placedByUsername ?? "?"}{(info.isMarker ? "" : "  " + RemainingTimeLabel(info))}";

        using (MpStyle.Set(GameFont.Small).Set(TextAnchor.UpperLeft)
            .Set(info.isMarker ? Color.white : new Color(1f, 1f, 1f, 0.85f)))
            Widgets.Label(new Rect(bodyRect.x, bodyRect.y, bodyRect.width, 18f), primary);
        using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.UpperLeft).Set(new Color(0.75f, 0.75f, 0.75f)))
            Widgets.Label(new Rect(bodyRect.x, bodyRect.y + 17f, bodyRect.width, 14f), secondary);

        var bodyClickReserved = info.isMarker ? 148f : 0f;
        var bodyClickRect = new Rect(rect.x, rect.y, rect.width - bodyClickReserved, rect.height);
        if (Widgets.ButtonInvisible(bodyClickRect))
        {
            Multiplayer.session.locationPings.JumpToAndSelect(info);
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        if (info.isMarker)
        {
            var canDelete = LocationPings.CanDeleteMarker(info);
            var renameRect = new Rect(rect.xMax - 148f, rect.y + 4f, 76f, rect.height - 8f);
            var deleteRect = new Rect(rect.xMax - 68f,  rect.y + 4f, 64f, rect.height - 8f);
            if (Widgets.ButtonText(renameRect, RenameLabel(), active: canDelete))
            {
                Find.WindowStack.Add(new PingLabelWindow(info.markerId, info.label));
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            if (Widgets.ButtonText(deleteRect, DeleteLabel(), active: canDelete))
            {
                Multiplayer.session?.locationPings?.SendDeleteMarker(info.markerId);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }
    }

    // Atlas alone doesn't read as a tab - add accent bar.
    private static bool DrawModeTabButton(Rect rect, string label, PingPlaceMode mode, bool selected)
    {
        var atlas = selected ? Widgets.ButtonBGAtlasClick
            : (Mouse.IsOver(rect) ? Widgets.ButtonBGAtlasMouseover : Widgets.ButtonBGAtlas);
        Widgets.DrawAtlas(rect, atlas);

        if (selected)
        {
            var accent = ModeAccentColor(mode);
            var bar = new Rect(rect.x + 4f, rect.yMax - 4f, rect.width - 8f, 3f);
            Widgets.DrawBoxSolid(bar, accent);
        }

        using (MpStyle.Set(GameFont.Small).Set(TextAnchor.MiddleCenter)
            .Set(selected ? Color.white : new Color(0.72f, 0.72f, 0.72f)))
            Widgets.Label(rect, label);
        MouseoverSounds.DoRegion(rect);
        return Widgets.ButtonInvisible(rect, false);
    }

    private static Color ModeAccentColor(PingPlaceMode m) => m switch
    {
        PingPlaceMode.Marker => new Color(0.40f, 0.70f, 1.00f),
        _                    => new Color(1.00f, 0.85f, 0.40f),
    };

    // Local player only.
    private List<PingInfo> BuildRows()
    {
        var loc = Multiplayer.session?.locationPings;
        var comp = Multiplayer.game?.gameComp;
        var markersV = comp?.markersVersion ?? 0;
        var pingsV   = loc?.pingsVersion ?? 0;

        if (cachedRows != null
            && cachedRowsMarkersVersion == markersV
            && cachedRowsPingsVersion == pingsV)
        {
#if DEBUG
            AssertRowsCacheStillValid(loc, comp);
#endif
            return cachedRows;
        }

        var rows = ComputeRowsUncached(loc, comp);
        cachedRows = rows;
        cachedRowsMarkersVersion = markersV;
        cachedRowsPingsVersion = pingsV;
        return rows;
    }

    private static List<PingInfo> ComputeRowsUncached(LocationPings loc, MultiplayerGameComp comp)
    {
        var rows = new List<PingInfo>();
        if (loc == null) return rows;

        if (comp != null)
        {
            var mine = new List<PingInfo>();
            foreach (var m in comp.AllMarkers)
                if (m.IsOwnedByLocalPlayer()) mine.Add(m);
            mine.Sort((a, b) => b.markerId.CompareTo(a.markerId));
            rows.AddRange(mine);
        }

        for (var i = loc.pings.Count - 1; i >= 0; i--)
            if (loc.pings[i].IsOwnedByLocalPlayer())
                rows.Add(loc.pings[i]);

        return rows;
    }

    private int MyMarkerCount()
    {
        var comp = Multiplayer.game?.gameComp;
        if (comp == null) return 0;
        if (cachedMyMarkerCountVersion == comp.markersVersion) return cachedMyMarkerCount;

        var n = 0;
        foreach (var m in comp.AllMarkers)
            if (m.IsOwnedByLocalPlayer()) n++;

        cachedMyMarkerCount = n;
        cachedMyMarkerCountVersion = comp.markersVersion;
        return n;
    }

    private int MyMarkersOnCurrentMap()
    {
        var comp = Multiplayer.game?.gameComp;
        if (comp == null || Find.CurrentMap == null) return 0;
        var id = Find.CurrentMap.uniqueID;
        if (cachedMyMarkersOnMapVersion == comp.markersVersion
            && cachedMyMarkersOnMapMapId == id)
            return cachedMyMarkersOnMap;

        var n = 0;
        foreach (var m in comp.AllMarkers)
            if (m.mapId == id && m.IsOwnedByLocalPlayer()) n++;

        cachedMyMarkersOnMap = n;
        cachedMyMarkersOnMapVersion = comp.markersVersion;
        cachedMyMarkersOnMapMapId = id;
        return n;
    }

#if DEBUG
    private int debugAssertFrame;
    private void AssertRowsCacheStillValid(LocationPings loc, MultiplayerGameComp comp)
    {
        // Every 64th frame, assert no mutation site forgot to bump versions.
        if ((debugAssertFrame++ & 63) != 0) return;
        var fresh = ComputeRowsUncached(loc, comp);
        var stale = fresh.Count != cachedRows.Count;
        if (!stale)
            for (var i = 0; i < fresh.Count; i++)
                if (!ReferenceEquals(fresh[i], cachedRows[i])) { stale = true; break; }
        if (stale)
            Log.ErrorOnce(
                $"[MP] PingMenuWindow row cache stale: cached={cachedRows.Count} fresh={fresh.Count}. " +
                "A mutation path missed markersVersion++ or pingsVersion++.", 0x6D7A8B);
    }
#endif

    private static string TargetDescription(PingInfo info)
    {
        if (info.mapId == -1)
            return MpTranslate.Fallback("MpPingMenuWindow_TargetPlanet", "Planet");
        var map = Find.Maps.GetById(info.mapId);
        return map?.Parent?.LabelCap
            ?? MpTranslate.Fallback("MpPingMenuWindow_TargetMap", "Map");
    }

    private static string RemainingTimeLabel(PingInfo p)
    {
        var remaining = Mathf.Max(0f, PingInfo.PingDuration - p.timeAlive);
        return $"{remaining:0.0}s";
    }

    private static string DeleteLabel()
        => MpTranslate.Fallback("MpPingMenuWindow_Delete",        "Delete");
    private static string RenameLabel()
        => MpTranslate.Fallback("MpPingSel_Rename",               "Rename");

    private static string SubtitleLabel()
    {
        var loc = Multiplayer.session?.locationPings;
        var modeWord = ModeWordLower(Multiplayer.settings.pingPlaceMode);

        if (loc?.armedCategory is { } cat)
        {
            var catName = cat.DisplayName();
            return MpTranslate.Fallback("MpPingMenuWindow_Subtitle_Armed",
                $"Click on the map to place a {catName} {modeWord}.",
                catName, modeWord);
        }
        return MpTranslate.Fallback("MpPingMenuWindow_Subtitle_Idle",
            $"Select a category from the wheel to place a {modeWord}.",
            modeWord);
    }

    private static string ModeWordLower(PingPlaceMode m)
        => MpTranslate.Fallback("MpPingMode_" + m + "_LowerWord",
            m == PingPlaceMode.Marker ? "marker" : "ping");

    private static string ModeLabel(PingPlaceMode m)
        => MpTranslate.Fallback("MpPingMode_" + m,
            m switch
            {
                PingPlaceMode.Ping   => "Ping",
                PingPlaceMode.Marker => "Marker",
                _ => m.ToString(),
            });
}
