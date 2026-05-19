using System.Collections.Generic;
using Multiplayer.Client.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client;

// Per-client visibility filter. Pure render gate; settings persist via MpSettings.
public class PingFiltersDialog : Window
{
    public static PingFiltersDialog Opened => Find.WindowStack?.WindowOfType<PingFiltersDialog>();

    public override Vector2 InitialSize => new(420f, 460f);

    private Vector2 listScroll;

    // Screen-space anchor from trigger button; flips above if clipped.
    private Rect? requestedAnchor;

    private List<Faction> cachedFactionsWithMarkers;
    private int cachedFactionsMarkersV = -1;

    private List<PlayerRowItem> cachedOtherPlayers;
    private int cachedOtherPlayersMarkersV = -1;
    private int cachedOtherPlayersPlayerCount = -1;

    public PingFiltersDialog(Rect? anchor = null)
    {
        requestedAnchor = anchor;
        draggable = true;
        resizeable = false;
        doCloseX = true;
        closeOnClickedOutside = false;
        closeOnAccept = false;
        closeOnCancel = true;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
        focusWhenOpened = true;
        onlyOneOfTypeAllowed = true;
        soundClose = SoundDefOf.FloatMenu_Cancel;
        layer = WindowLayer.GameUI;
    }

    public override void SetInitialSizeAndPosition()
    {
        var size = InitialSize;
        var screen = new Vector2(UI.screenWidth, UI.screenHeight);
        const float ScreenMargin = 6f;

        // Priority: toolbar-anchor (fresh click) > prior drag-position > center.
        Vector2 desired;
        if (requestedAnchor is { } trigger)
        {
            const float Gap = 4f;
            var belowY = trigger.yMax + Gap;
            if (belowY + size.y > screen.y - ScreenMargin)
                desired = new Vector2(trigger.x, trigger.y - size.y - Gap);
            else
                desired = new Vector2(trigger.x, belowY);
            requestedAnchor = null;
        }
        else
        {
            var saved = Multiplayer.settings.pingFiltersDialogRect;
            // Re-validate: InitialSize could have changed.
            if (saved.width > 0f && saved.height > 0f
                && saved.x >= -size.x + ScreenMargin && saved.x <= screen.x - ScreenMargin
                && saved.y >= -size.y + ScreenMargin && saved.y <= screen.y - ScreenMargin)
                desired = new Vector2(saved.x, saved.y);
            else
                desired = new Vector2((screen.x - size.x) / 2f, (screen.y - size.y) / 2f);
        }
        var x = Mathf.Clamp(desired.x, ScreenMargin, screen.x - size.x - ScreenMargin);
        var y = Mathf.Clamp(desired.y, ScreenMargin, screen.y - size.y - ScreenMargin);
        windowRect = new Rect(x, y, size.x, size.y);
    }

    public override void PostClose()
    {
        base.PostClose();
        Multiplayer.settings.pingFiltersDialogRect = windowRect;
        MultiplayerLoader.Multiplayer.instance?.WriteSettings();
    }

    public override void DoWindowContents(Rect inRect)
    {
        var settings = Multiplayer.settings;
        if (settings == null) return;

        var titleRect = new Rect(inRect.x, inRect.y, inRect.width - 30f, 28f);
        using (MpStyle.Set(GameFont.Medium).Set(TextAnchor.MiddleLeft))
            Widgets.Label(titleRect, MpTranslate.Fallback("MpPingFilters_Title",         "Marker visibility"));

        var y = titleRect.yMax + 8f;

        var specRect = new Rect(inRect.x, y, inRect.width, 24f);
        var showSpec = settings.showSpectatorMarkers;
        Widgets.CheckboxLabeled(specRect, MpTranslate.Fallback("MpPingFilters_ShowSpectators","Show spectator markers"), ref showSpec);
        if (showSpec != settings.showSpectatorMarkers)
        {
            settings.showSpectatorMarkers = showSpec;
            MultiplayerLoader.Multiplayer.instance?.WriteSettings();
        }
        y = specRect.yMax + 10f;

        Widgets.DrawLineHorizontal(inRect.x, y, inRect.width);
        y += 6f;

        const float ResetH = 28f;
        const float ResetGap = 8f;
        var listOutRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y - ResetH - ResetGap);

        var factions = ListFactionsWithMarkers();
        var players = ListOtherPlayers();
        var viewH = SectionHeaderH
                    + (factions.Count == 0 ? EmptyRowH : factions.Count * RowH)
                    + SectionGap
                    + SectionHeaderH
                    + (players.Count == 0 ? EmptyRowH : players.Count * RowH)
                    + 4f;
        var viewRect = new Rect(0f, 0f, listOutRect.width - 16f, viewH);

        Widgets.BeginScrollView(listOutRect, ref listScroll, viewRect);

        var yy = 0f;
        DrawSectionHeader(new Rect(0f, yy, viewRect.width, SectionHeaderH), MpTranslate.Fallback("MpPingFilters_FactionsHeader","By faction"));
        yy += SectionHeaderH;

        if (factions.Count == 0)
        {
            DrawEmptyRow(new Rect(0f, yy, viewRect.width, EmptyRowH), MpTranslate.Fallback("MpPingFilters_NoFactions",    "No factions with markers yet"));
            yy += EmptyRowH;
        }
        else
        {
            foreach (var f in factions)
            {
                DrawFactionRow(new Rect(0f, yy, viewRect.width, RowH), f, settings);
                yy += RowH;
            }
        }
        yy += SectionGap;

        DrawSectionHeader(new Rect(0f, yy, viewRect.width, SectionHeaderH), MpTranslate.Fallback("MpPingFilters_PlayersHeader", "By player"));
        yy += SectionHeaderH;

        if (players.Count == 0)
        {
            DrawEmptyRow(new Rect(0f, yy, viewRect.width, EmptyRowH), MpTranslate.Fallback("MpPingFilters_NoOtherPlayers","You're the only player"));
            yy += EmptyRowH;
        }
        else
        {
            foreach (var p in players)
            {
                DrawPlayerRow(new Rect(0f, yy, viewRect.width, RowH), p, settings);
                yy += RowH;
            }
        }

        Widgets.EndScrollView();

        var resetRect = new Rect(inRect.x, listOutRect.yMax + ResetGap, inRect.width, ResetH);
        if (Widgets.ButtonText(resetRect, MpTranslate.Fallback("MpPingFilters_ResetAll",      "Reset all")))
        {
            settings.hiddenFactionLoadIds.Clear();
            settings.hiddenPlayerNames.Clear();
            settings.showSpectatorMarkers = true;
            MultiplayerLoader.Multiplayer.instance?.WriteSettings();
            SoundDefOf.Click.PlayOneShotOnCamera();
        }
    }

    private const float RowH = 28f;
    private const float EmptyRowH = 24f;
    private const float SectionGap = 8f;
    private const float SectionHeaderH = 22f;

    // Vanilla Faction.Color falls back to def.colorSpectrum when Faction.color isn't explicitly
    // set, which gives every MP-created player faction the same stripe. Use a per-loadID palette
    // so factions in the visibility panel are easy to tell apart; user-chosen colors via
    // Dialog_ChooseFactionColor still take priority.
    private static readonly Color[] FactionPalette =
    {
        new(0.40f, 0.78f, 1.00f), // sky blue
        new(1.00f, 0.62f, 0.30f), // orange
        new(0.55f, 0.95f, 0.55f), // green
        new(1.00f, 0.55f, 0.85f), // pink
        new(0.95f, 0.92f, 0.45f), // yellow
        new(0.75f, 0.55f, 1.00f), // violet
        new(0.92f, 0.45f, 0.45f), // red
        new(0.45f, 0.92f, 0.85f), // teal
    };

    private static Color FactionStripeColor(Faction f)
    {
        if (f.color.HasValue) return f.color.Value;
        var len = FactionPalette.Length;
        return FactionPalette[((f.loadID % len) + len) % len];
    }

    private static void DrawSectionHeader(Rect rect, string label)
    {
        using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.MiddleLeft).Set(new Color(0.7f, 0.7f, 0.7f)))
            Widgets.Label(rect, label);
    }

    private static void DrawEmptyRow(Rect rect, string label)
    {
        using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.MiddleLeft).Set(new Color(0.55f, 0.55f, 0.55f)))
            Widgets.Label(rect.ContractedBy(8f, 0f), label);
    }

    private static void DrawFactionRow(Rect rect, Faction f, MpSettings s)
    {
        Widgets.DrawHighlightIfMouseover(rect);

        var stripe = FactionStripeColor(f);
        Widgets.DrawBoxSolid(new Rect(rect.x + 2f, rect.y + 4f, 4f, rect.height - 8f),
            new Color(stripe.r, stripe.g, stripe.b, 0.95f));

        // Reserve 32 on the right (28 checkbox + 4 gap) so long names don't touch the checkbox.
        var labelRect = new Rect(rect.x + 12f, rect.y, rect.width - 12f - 32f, rect.height);
        using (MpStyle.Set(GameFont.Small).Set(TextAnchor.MiddleLeft))
            Widgets.Label(labelRect, f.Name);

        // Checkbox = show (inverse of hidden set).
        var show = !s.hiddenFactionLoadIds.Contains(f.loadID);
        var prev = show;
        var checkRect = new Rect(rect.xMax - 28f, rect.y + (rect.height - 24f) / 2f, 24f, 24f);
        Widgets.Checkbox(checkRect.position, ref show, 24f);

        var labelClickRect = new Rect(rect.x, rect.y, rect.width - 32f, rect.height);
        if (Widgets.ButtonInvisible(labelClickRect))
        {
            show = !show;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        if (prev != show)
        {
            if (show) s.hiddenFactionLoadIds.Remove(f.loadID);
            else s.hiddenFactionLoadIds.Add(f.loadID);
            MultiplayerLoader.Multiplayer.instance?.WriteSettings();
        }
    }

    private static void DrawPlayerRow(Rect rect, PlayerRowItem p, MpSettings s)
    {
        Widgets.DrawHighlightIfMouseover(rect);

        if (p.isSelf)
            Widgets.DrawBoxSolid(rect, new Color(0.30f, 0.55f, 0.90f, 0.18f));

        var stripe = p.color;
        Widgets.DrawBoxSolid(new Rect(rect.x + 2f, rect.y + 4f, 4f, rect.height - 8f),
            new Color(stripe.r, stripe.g, stripe.b, 0.95f));

        // No self-mute (nonsensical).
        var canShowMute = !p.isSelf;
        // FromPlayer-clear: host can target anyone; non-host only themselves.
        var canShowClear = !string.IsNullOrEmpty(p.username) && (Multiplayer.LocalServer != null || p.isSelf);

        // Clear button sits at xMax-56; reserving 60 covers it (and the checkbox column to its right).
        var labelRightReserve = canShowClear ? 60f : (canShowMute ? 32f : 0f);
        var labelRect = new Rect(rect.x + 12f, rect.y, rect.width - 12f - labelRightReserve, rect.height);
        var labelColor = p.isSelf ? new Color(stripe.r, stripe.g, stripe.b, 1f) : GUI.color;
        using (MpStyle.Set(GameFont.Small).Set(TextAnchor.MiddleLeft).Set(labelColor))
            Widgets.Label(labelRect, p.label);

        var checkRect = new Rect(rect.xMax - 28f, rect.y + (rect.height - 24f) / 2f, 24f, 24f);
        var clearRect = new Rect(rect.xMax - 56f, rect.y + (rect.height - 24f) / 2f, 22f, 22f);

        if (string.IsNullOrEmpty(p.username))
        {
            // Empty username = disabled controls only.
            var phantom = true;
            using (MpStyle.Set(new Color(GUI.color.r, GUI.color.g, GUI.color.b, 0.4f)))
            {
                Widgets.Checkbox(checkRect.position, ref phantom, 24f);
                GUI.DrawTexture(clearRect, TexButton.Delete);
            }
            return;
        }

        if (canShowClear)
        {
            TooltipHandler.TipRegion(clearRect, MpTranslate.Fallback("MpPingFilters_ClearPlayerMarkers",
                $"Clear every marker placed by {p.username}.",
                new NamedArgument(p.username, "USERNAME")));
            if (Widgets.ButtonImage(clearRect, TexButton.Delete))
            {
                Multiplayer.session?.locationPings?.SendClearMarkersFromPlayer(p.username);
                SoundDefOf.Click.PlayOneShotOnCamera();
                // Stop the click falling through to GUI.DragWindow.
                Event.current.Use();
            }
        }

        if (!canShowMute) return;

        var show = !s.hiddenPlayerNames.Contains(p.username);
        var prev2 = show;
        Widgets.Checkbox(checkRect.position, ref show, 24f);

        // Exclude clear-button hit area.
        var labelClickRect = new Rect(rect.x, rect.y, rect.width - 60f, rect.height);
        if (Widgets.ButtonInvisible(labelClickRect))
        {
            show = !show;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        if (prev2 != show)
        {
            if (show) s.hiddenPlayerNames.Remove(p.username);
            else s.hiddenPlayerNames.Add(p.username);
            MultiplayerLoader.Multiplayer.instance?.WriteSettings();
        }
    }

    // Spectator handled by master toggle.
    private List<Faction> ListFactionsWithMarkers()
    {
        // Multiplayer.GameComp / WorldComp throw on null - must use the safe-nav form because
        // the dialog can outlive a packet-disconnect that nulls Multiplayer.game.
        var comp = Multiplayer.game?.gameComp;
        var factionMan = Find.FactionManager;
        if (comp == null || factionMan == null)
            return cachedFactionsWithMarkers ??= new List<Faction>();

        if (cachedFactionsWithMarkers != null && cachedFactionsMarkersV == comp.markersVersion)
            return cachedFactionsWithMarkers;

        cachedFactionsWithMarkers ??= new List<Faction>();
        cachedFactionsWithMarkers.Clear();
        var spectator = Multiplayer.game?.worldComp?.spectatorFaction;
        foreach (var entry in comp.markersByFaction)
        {
            if (entry.Value == null || entry.Value.Count == 0) continue;
            var f = factionMan.GetById(entry.Key);
            if (f == null) continue;
            if (spectator != null && f.loadID == spectator.loadID) continue;
            cachedFactionsWithMarkers.Add(f);
        }
        cachedFactionsMarkersV = comp.markersVersion;
        return cachedFactionsWithMarkers;
    }

    private readonly struct PlayerRowItem
    {
        public readonly string username;
        public readonly string label;
        public readonly Color color;
        public readonly bool isSelf;
        public PlayerRowItem(string username, string label, Color color, bool isSelf = false)
        { this.username = username; this.label = label; this.color = color; this.isSelf = isSelf; }
    }

    // Self first, then connected players, then offline placers from markersByFaction. No arbiter.
    private List<PlayerRowItem> ListOtherPlayers()
    {
        if (Multiplayer.session == null)
            return cachedOtherPlayers ??= new List<PlayerRowItem>();

        var comp = Multiplayer.game?.gameComp;
        var markersV = comp?.markersVersion ?? 0;
        var playerCount = Multiplayer.session.players?.Count ?? 0;

        if (cachedOtherPlayers != null
            && cachedOtherPlayersMarkersV == markersV
            && cachedOtherPlayersPlayerCount == playerCount)
            return cachedOtherPlayers;

        cachedOtherPlayers ??= new List<PlayerRowItem>();
        cachedOtherPlayers.Clear();

        var meId = Multiplayer.session.playerId;
        var mePi = Multiplayer.session.GetPlayerInfo(meId);
        var meName = mePi?.username;
        var seenUsernames = new HashSet<string>();

        if (mePi != null)
            cachedOtherPlayers.Add(new PlayerRowItem(meName ?? "", (meName ?? "?") + " " + MpTranslate.Fallback("MpPingFilters_YouSuffix",     "(you)"),
                mePi.color, isSelf: true));
        if (!string.IsNullOrEmpty(meName)) seenUsernames.Add(meName!);

        var others = new List<PlayerRowItem>();
        foreach (var p in Multiplayer.session.players)
        {
            if (p.id == meId) continue;
            if (p.IsArbiter) continue;
            var name = p.username ?? "";
            others.Add(new PlayerRowItem(name, p.username ?? "?", p.color));
            if (!string.IsNullOrEmpty(name)) seenUsernames.Add(name);
        }

        if (comp != null)
        {
            foreach (var m in comp.AllMarkers)
            {
                var name = m.placedByUsername;
                if (string.IsNullOrEmpty(name)) continue;
                if (!seenUsernames.Add(name)) continue;
                var color = new Color(m.placedByR, m.placedByG, m.placedByB);
                others.Add(new PlayerRowItem(name, name + " " + MpTranslate.Fallback("MpPingFilters_OfflineSuffix", "(offline)"), color));
            }
        }

        others.Sort((a, b) => string.CompareOrdinal(a.username, b.username));
        cachedOtherPlayers.AddRange(others);
        cachedOtherPlayersMarkersV = markersV;
        cachedOtherPlayersPlayerCount = playerCount;
        return cachedOtherPlayers;
    }

    public static string OpenTooltipLabel()
        => MpTranslate.Fallback("MpPingFilters_OpenTooltip",
            "Choose which players' and factions' markers to show");
}
