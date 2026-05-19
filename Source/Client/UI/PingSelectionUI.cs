using System.Collections.Generic;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client;

// Marker/ping selection helpers: bracket overlay, inspect-pane lifecycle, gizmo factory.
public static class PingSelectionUI
{
    public static bool IsSelected(PingInfo info)
    {
        var loc = Multiplayer.session?.locationPings;
        if (loc == null) return false;
        return info.isMarker
            ? loc.IsMarkerSelected(info.markerId)
            : loc.IsPingSelected(info.player);
    }

    public static void DrawSelectionBrackets(PingInfo info, Vector2 screenCenter, float size)
    {
        var rectSize = size * 1.35f;
        var rect = new Rect(screenCenter.x - rectSize / 2f, screenCenter.y - rectSize / 2f,
            rectSize, rectSize);
        SelectionDrawerUtility.DrawSelectionOverlayOnGUI(info, rect, scale: 0.4f, selectedTextJump: 20f);
    }

    public static void UpdatePingInspectPaneVisibility()
    {
        if (Multiplayer.arbiterInstance) return;
        // OnGUI can fire before UpdatePing clears selection on replay entry.
        if (Multiplayer.IsReplay) return;
        var loc = Multiplayer.session?.locationPings;
        if (loc == null) return;

        // No Event.Use() - same press still clears vanilla in a mixed selection.
        if (loc.HasSelection && KeyBindingDefOf.Cancel.KeyDownEvent)
            loc.ClearSelection();

        // Vanilla's selection-blocks check is view-scoped - planet selection only blocks on planet view.
        var onPlanet = WorldRendererUtility.WorldSelected;
        var vanillaSelectorBlocks = onPlanet
            ? Find.WorldSelector != null
              && (Find.WorldSelector.NumSelectedObjects > 0 || Find.WorldSelector.SelectedTile.Valid)
            : (Find.Selector?.NumSelected ?? 0) > 0;
        var openOurPane = loc.HasSelection && !vanillaSelectorBlocks;
        var open = PingInspectPane.Opened;

        if (openOurPane && open == null)
            Find.WindowStack.Add(new PingInspectPane());
        else if (!openOurPane && open != null)
            open.Close(doCloseSound: false);
    }

    // No intersect-with sweep - selectedMarkerIds may also contain map-bound ids the planet view doesn't see.
    public static List<PingInfo> CollectSelectedOnPlanet(LocationPings loc)
    {
        var result = new List<PingInfo>();

        if (loc.selectedMarkerIds.Count > 0)
            foreach (var m in loc.Markers)
                if (m.mapId == -1 && loc.selectedMarkerIds.Contains(m.markerId) && m.IsVisible())
                    result.Add(m);

        if (loc.selectedPingPlayerIds.Count > 0)
            foreach (var p in loc.pings)
                if (p.mapId == -1 && loc.selectedPingPlayerIds.Contains(p.player) && p.IsVisible())
                    result.Add(p);

        return result;
    }

    // Filter-hidden markers stay in the set but are skipped here so brackets/gizmos ignore them.
    public static List<PingInfo> CollectSelectedOnCurrentMap(LocationPings loc)
    {
        var result = new List<PingInfo>();
        if (Find.CurrentMap == null) return result;
        var mapId = Find.CurrentMap.uniqueID;

        if (loc.selectedMarkerIds.Count > 0)
        {
            var stillAlive = new HashSet<int>();
            foreach (var m in loc.Markers)
                if (m.mapId == mapId && loc.selectedMarkerIds.Contains(m.markerId))
                {
                    stillAlive.Add(m.markerId);
                    if (m.IsVisible())
                        result.Add(m);
                }
            if (stillAlive.Count != loc.selectedMarkerIds.Count)
            {
                loc.selectedMarkerIds.IntersectWith(stillAlive);
                loc.selectionVersion++;
            }
        }

        if (loc.selectedPingPlayerIds.Count > 0)
        {
            var stillAlive = new HashSet<int>();
            foreach (var p in loc.pings)
                if (p.mapId == mapId && loc.selectedPingPlayerIds.Contains(p.player))
                {
                    stillAlive.Add(p.player);
                    if (p.IsVisible())
                        result.Add(p);
                }
            if (stillAlive.Count != loc.selectedPingPlayerIds.Count)
            {
                loc.selectedPingPlayerIds.IntersectWith(stillAlive);
                loc.selectionVersion++;
            }
        }

        return result;
    }

    // Cached so the vanilla gizmo-grid reference-cache hits. No hotKey - would double-fire.
    public static List<Gizmo> BuildGizmos(List<PingInfo> selected, LocationPings loc)
    {
        // "Owned" = deletable by the local player (placer OR multifaction map-owner).
        var ownedMarkerCount = 0;
        var foreignMarkerCount = 0;
        var foreignSampleUsername = (string)null;
        var foreignSampleFactionId = -1;
        var foreignSpectatorPresent = false;
        foreach (var info in selected)
        {
            if (!info.isMarker) continue;
            if (LocationPings.CanDeleteMarker(info))
            {
                ownedMarkerCount++;
            }
            else
            {
                foreignMarkerCount++;
                if (foreignSampleUsername == null && !string.IsNullOrEmpty(info.placedByUsername))
                {
                    foreignSampleUsername = info.placedByUsername;
                    foreignSampleFactionId = info.placedByFactionLoadId;
                }
                var spec = Multiplayer.WorldComp?.spectatorFaction;
                if (spec != null && info.placedByFactionLoadId == spec.loadID)
                    foreignSpectatorPresent = true;
            }
        }

        var renameTargetId = 0;
        if (ownedMarkerCount == 1 && foreignMarkerCount == 0)
        {
            var t = FindOnlyOwnedMarker(selected);
            if (t != null) renameTargetId = t.markerId;
        }

        // Faction switch invalidates cache - CanDeleteMarker reads RealPlayerFaction.
        var factionId = Multiplayer.RealPlayerFaction?.loadID ?? -1;
        var markersV = Multiplayer.game?.gameComp?.markersVersion ?? 0;
        var selectionV = loc.selectionVersion;

        if (loc.cachedGizmos != null
            && loc.cachedGizmoKey.Matches(ownedMarkerCount, foreignMarkerCount, renameTargetId, factionId, markersV, selectionV))
            return loc.cachedGizmos;

        var result = new List<Gizmo>();
        if (ownedMarkerCount > 0)
        {
            var label = ownedMarkerCount == 1
                ? DeleteLabel()
                : MultiDeleteLabel(ownedMarkerCount);
            var desc = foreignMarkerCount > 0
                ? MpTranslate.Fallback("MpPingSel_DeleteForeignDesc",
                    $"Removes {ownedMarkerCount} of your markers. {foreignMarkerCount} marker(s) from other players are in this selection and cannot be deleted by you.",
                    ownedMarkerCount, foreignMarkerCount)
                : MpTranslate.Fallback("MpPingSel_DeleteDesc", "Remove this marker from the map.");
            result.Add(new Command_Action
            {
                defaultLabel = label,
                defaultDesc  = desc,
                icon         = TexButton.Delete,
                action       = () => DeleteOwnedFromCurrentSelection(loc),
            });

            // Rename only on a single-owned selection - renaming many markers at once doesn't make sense.
            if (ownedMarkerCount == 1 && foreignMarkerCount == 0)
            {
                var theOne = FindOnlyOwnedMarker(selected);
                if (theOne != null)
                {
                    result.Add(new Command_Action
                    {
                        defaultLabel = RenameLabel(),
                        defaultDesc  = MpTranslate.Fallback("MpPingSel_RenameDesc", "Change this marker's label."),
                        icon         = TexButton.Rename,
                        action       = () => Find.WindowStack.Add(new PingLabelWindow(theOne.markerId, theOne.label)),
                    });
                }
            }
        }

        // Show mute-actions for the first foreign placer only; multi-foreign almost always shares one placer.
        if (foreignSampleUsername != null)
        {
            var settings = Multiplayer.settings;
            var alreadyMutedPlayer = settings != null && settings.hiddenPlayerNames.Contains(foreignSampleUsername);
            result.Add(new Command_Action
            {
                defaultLabel = alreadyMutedPlayer
                    ? UnmutePlayerLabel(foreignSampleUsername)
                    : MutePlayerLabel(foreignSampleUsername),
                defaultDesc  = MpTranslate.Fallback("MpPingSel_MutePlayerDesc",
                    $"Hide all current and future markers and pings placed by {foreignSampleUsername}, including the audible cue when one is dropped.",
                    foreignSampleUsername),
                icon         = MultiplayerStatic.PingMuteIcon,
                action       = () => ToggleMutePlayer(foreignSampleUsername),
            });

            if (foreignSampleFactionId >= 0)
            {
                var factionMan = Find.FactionManager;
                var faction = factionMan?.GetById(foreignSampleFactionId);
                var factionName = faction?.Name ?? "?";
                var alreadyMutedFaction = settings != null && settings.hiddenFactionLoadIds.Contains(foreignSampleFactionId);
                result.Add(new Command_Action
                {
                    defaultLabel = alreadyMutedFaction
                        ? UnmuteFactionLabel(factionName)
                        : MuteFactionLabel(factionName),
                    defaultDesc  = MpTranslate.Fallback("MpPingSel_MuteFactionDesc",
                        $"Hide all markers and pings placed by anyone in {factionName}.",
                        factionName),
                    icon         = MultiplayerStatic.PingMuteIcon,
                    action       = () => ToggleMuteFaction(foreignSampleFactionId),
                });
            }
        }
        if (foreignSpectatorPresent)
        {
            var settings = Multiplayer.settings;
            var alreadyMuted = settings != null && !settings.showSpectatorMarkers;
            result.Add(new Command_Action
            {
                defaultLabel = alreadyMuted ? UnmuteSpectatorsLabel() : MuteSpectatorsLabel(),
                defaultDesc  = MpTranslate.Fallback("MpPingSel_MuteSpectatorsDesc",
                    "Hide all markers and pings placed by spectators (joiners who haven't picked a faction yet)."),
                icon         = MultiplayerStatic.PingMuteIcon,
                action       = ToggleMuteSpectators,
            });
        }

        // Local overrides apply to any selected marker (incl. foreign) - "see past this" is the use case.
        var markerIds = new List<int>();
        foreach (var info in selected)
            if (info.isMarker && info.markerId != 0) markerIds.Add(info.markerId);
        if (markerIds.Count > 0)
        {
            var anyHidden = false;
            var anyDimmed = false;
            foreach (var id in markerIds)
            {
                if (Multiplayer.settings?.locallyHiddenMarkers?.Contains(id) ?? false) anyHidden = true;
                if (Multiplayer.settings?.localMarkerAlpha?.TryGetValue(id, out var a) ?? false)
                    if (a < 0.999f) anyDimmed = true;
            }

            result.Add(new Command_Action
            {
                defaultLabel = TransparencyLabel(),
                defaultDesc  = MpTranslate.Fallback("MpPingSel_TransparencyDesc",
                    "Adjust how much this marker is faded for you, without changing what other players see."),
                icon         = MultiplayerStatic.PingTransparencyIcon,
                action       = () =>
                {
                    Find.WindowStack.Add(new MarkerAlphaWindow(markerIds));
                    SoundDefOf.Click.PlayOneShotOnCamera();
                },
            });

            result.Add(new Command_Action
            {
                defaultLabel = anyHidden ? UnhideLocallyLabel() : HideLocallyLabel(),
                defaultDesc  = MpTranslate.Fallback("MpPingSel_HideLocallyDesc",
                    "Collapse this marker to a small dot in your own view. Click the dot to bring it back. Other players are unaffected."),
                icon         = anyHidden ? MultiplayerStatic.PingShowForMeIcon : MultiplayerStatic.PingHideForMeIcon,
                action       = () =>
                {
                    ToggleHideLocally(markerIds, makeVisible: anyHidden);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                },
            });

            if (anyDimmed || anyHidden)
            {
                result.Add(new Command_Action
                {
                    defaultLabel = ResetLocalAppearanceLabel(),
                    defaultDesc  = MpTranslate.Fallback("MpPingSel_ResetLocalAppearanceDesc",
                        "Clear any transparency or local-hide settings for this marker."),
                    icon         = MultiplayerStatic.PingResetViewIcon,
                    action       = () =>
                    {
                        ResetLocalAppearance(markerIds);
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    },
                });
            }
        }

        // Escape hatch for the drag-select pulls-in-markers behavior.
        result.Add(new Command_Action
        {
            defaultLabel = DeselectAllMarkersLabel(),
            defaultDesc  = MpTranslate.Fallback("MpPingSel_DeselectAllDesc",
                "Drop every marker and ping from your current selection. Vanilla selection (pawns, items, buildings) is not affected."),
            icon         = MultiplayerStatic.PingDeselectIcon,
            action       = () =>
            {
                loc.ClearSelection();
                SoundDefOf.Click.PlayOneShotOnCamera();
            },
        });

        loc.cachedGizmos = result;
        loc.cachedGizmoKey = new LocationPings.GizmoCacheKey
        {
            owned = ownedMarkerCount,
            foreign = foreignMarkerCount,
            renameTargetId = renameTargetId,
            factionId = factionId,
            markersVersion = markersV,
            selectionVersion = selectionV,
        };
        return result;
    }

    // Planet-view has no gizmo grid - inline row mirrors what BuildGizmos shows on map-view.
    // Both the analysis and the resulting actions list are cached on the same (markersV,
    // selectionV, factionId) key; every settings toggle that affects mute/hide state bumps
    // markersVersion via BumpMarkersVersion, so the closure captures stay in sync.
    public static void DrawInlineActionsRow(Rect rowRect, List<PingInfo> selected, LocationPings loc)
    {
        var markersV = Multiplayer.game?.gameComp?.markersVersion ?? 0;
        var factionId = Multiplayer.RealPlayerFaction?.loadID ?? -1;
        if (loc.cachedInlineMarkersV != markersV
            || loc.cachedInlineSelectionV != loc.selectionVersion
            || loc.cachedInlineFactionId != factionId)
        {
            loc.cachedInlineOwnedCount = 0;
            loc.cachedInlineOnlyOwnedSingle = null;
            loc.cachedInlineForeignSampleUsername = null;
            loc.cachedInlineForeignSampleFactionId = -1;
            loc.cachedInlineForeignSpectatorPresent = false;
            loc.cachedInlineMarkerIds.Clear();
            var spec = Multiplayer.WorldComp?.spectatorFaction;
            foreach (var info in selected)
            {
                if (!info.isMarker) continue;
                if (info.markerId != 0)
                    loc.cachedInlineMarkerIds.Add(info.markerId);
                if (LocationPings.CanDeleteMarker(info))
                {
                    loc.cachedInlineOwnedCount++;
                    // Set on first, null on every subsequent - consumer gates on ownedCount == 1.
                    loc.cachedInlineOnlyOwnedSingle = loc.cachedInlineOwnedCount == 1 ? info : null;
                }
                else
                {
                    if (loc.cachedInlineForeignSampleUsername == null && !string.IsNullOrEmpty(info.placedByUsername))
                    {
                        loc.cachedInlineForeignSampleUsername = info.placedByUsername;
                        loc.cachedInlineForeignSampleFactionId = info.placedByFactionLoadId;
                    }
                    if (spec != null && info.placedByFactionLoadId == spec.loadID)
                        loc.cachedInlineForeignSpectatorPresent = true;
                }
            }

            loc.cachedInlineActions = BuildInlineActions(loc);

            loc.cachedInlineMarkersV = markersV;
            loc.cachedInlineSelectionV = loc.selectionVersion;
            loc.cachedInlineFactionId = factionId;
        }

        PackInlineActionButtons(rowRect, loc.cachedInlineActions);
    }

    // Built once per cache invalidation. Lambdas capture loc.cachedInlineMarkerIds by reference;
    // that List is .Clear()-and-refilled in place during rebuild, so a cache hit means the
    // captured contents are still current. Foreign sample / faction id are read out of the
    // analysis cache and captured by value.
    private static List<(string label, System.Action onClick)> BuildInlineActions(LocationPings loc)
    {
        var ownedCount              = loc.cachedInlineOwnedCount;
        var onlyOwnedSingle         = loc.cachedInlineOnlyOwnedSingle;
        var foreignSampleUsername   = loc.cachedInlineForeignSampleUsername;
        var foreignSampleFactionId  = loc.cachedInlineForeignSampleFactionId;
        var foreignSpectatorPresent = loc.cachedInlineForeignSpectatorPresent;
        var markerIds               = loc.cachedInlineMarkerIds;

        // Measured-width row-wrap; fixed-width overflows once 4+ buttons appear.
        var actions = new List<(string label, System.Action onClick)>();

        if (ownedCount > 0)
        {
            actions.Add((ownedCount == 1 ? DeleteLabel() : MultiDeleteLabel(ownedCount),
                () => DeleteOwnedFromCurrentSelection(loc)));

            if (ownedCount == 1 && onlyOwnedSingle != null)
            {
                var theOne = onlyOwnedSingle;
                actions.Add((RenameLabel(),
                    () => Find.WindowStack.Add(new PingLabelWindow(theOne.markerId, theOne.label))));
            }
        }

        if (foreignSampleUsername != null)
        {
            var muted = Multiplayer.settings?.hiddenPlayerNames.Contains(foreignSampleUsername) ?? false;
            actions.Add((muted ? UnmutePlayerLabel(foreignSampleUsername) : MutePlayerLabel(foreignSampleUsername),
                () => ToggleMutePlayer(foreignSampleUsername)));

            if (foreignSampleFactionId >= 0)
            {
                var factionName = Find.FactionManager?.GetById(foreignSampleFactionId)?.Name ?? "?";
                var mutedFaction = Multiplayer.settings?.hiddenFactionLoadIds.Contains(foreignSampleFactionId) ?? false;
                actions.Add((mutedFaction ? UnmuteFactionLabel(factionName) : MuteFactionLabel(factionName),
                    () => ToggleMuteFaction(foreignSampleFactionId)));
            }
        }

        if (foreignSpectatorPresent)
        {
            var muted = Multiplayer.settings != null && !Multiplayer.settings.showSpectatorMarkers;
            actions.Add((muted ? UnmuteSpectatorsLabel() : MuteSpectatorsLabel(),
                ToggleMuteSpectators));
        }

        if (markerIds.Count > 0)
        {
            var anyHidden = false;
            foreach (var id in markerIds)
                if (Multiplayer.settings?.locallyHiddenMarkers?.Contains(id) ?? false) anyHidden = true;

            actions.Add((TransparencyLabel(),
                () => Find.WindowStack.Add(new MarkerAlphaWindow(markerIds))));
            actions.Add((anyHidden ? UnhideLocallyLabel() : HideLocallyLabel(),
                () => ToggleHideLocally(markerIds, makeVisible: anyHidden)));
        }

        actions.Add((DeselectAllMarkersLabel(),
            () => { loc.ClearSelection(); SoundDefOf.Click.PlayOneShotOnCamera(); }));

        return actions;
    }

    // Measured-width pack with row-wrap; caller reserves vertical space (see ActionRowH in MarkerInspectTab.FillTab).
    private static void PackInlineActionButtons(Rect rowRect,
        List<(string label, System.Action onClick)> actions)
    {
        const float BtnH = 24f;
        const float BtnPadX = 10f;
        const float BtnGap = 6f;
        const float RowGap = 4f;

        var x = rowRect.x;
        var y = rowRect.y;
        foreach (var (label, onClick) in actions)
        {
            var w = Text.CalcSize(label).x + BtnPadX * 2f;
            // Always draw the first button on a row even if it's wider than rowRect - clipping
            // there is better than an invisible action.
            if (x > rowRect.x && x + w > rowRect.xMax)
            {
                x = rowRect.x;
                y += BtnH + RowGap;
            }
            if (y + BtnH > rowRect.yMax + 1f) break;
            if (Widgets.ButtonText(new Rect(x, y, w, BtnH), label))
                onClick();
            x += w + BtnGap;
        }
    }

    private static void ToggleHideLocally(List<int> markerIds, bool makeVisible)
    {
        var s = Multiplayer.settings;
        if (s == null) return;
        s.locallyHiddenMarkers ??= new HashSet<int>();
        foreach (var id in markerIds)
        {
            if (makeVisible) s.locallyHiddenMarkers.Remove(id);
            else s.locallyHiddenMarkers.Add(id);
        }
        // markersVersion bump - local-hide doesn't mutate marker, but changes what counts as drawn.
        if (Multiplayer.game?.gameComp != null) Multiplayer.game.gameComp.markersVersion++;
        MultiplayerLoader.Multiplayer.instance?.WriteSettings();
    }

    private static void ResetLocalAppearance(List<int> markerIds)
    {
        var s = Multiplayer.settings;
        if (s == null) return;
        s.locallyHiddenMarkers ??= new HashSet<int>();
        s.localMarkerAlpha ??= new Dictionary<int, float>();
        foreach (var id in markerIds)
        {
            s.locallyHiddenMarkers.Remove(id);
            s.localMarkerAlpha.Remove(id);
        }
        if (Multiplayer.game?.gameComp != null) Multiplayer.game.gameComp.markersVersion++;
        MultiplayerLoader.Multiplayer.instance?.WriteSettings();
    }

    private static void ToggleMutePlayer(string username)
    {
        var s = Multiplayer.settings;
        if (s == null || string.IsNullOrEmpty(username)) return;
        if (!s.hiddenPlayerNames.Add(username))
            s.hiddenPlayerNames.Remove(username);
        BumpMarkersVersion();
        MultiplayerLoader.Multiplayer.instance?.WriteSettings();
        SoundDefOf.Click.PlayOneShotOnCamera();
    }

    private static void ToggleMuteFaction(int factionLoadId)
    {
        var s = Multiplayer.settings;
        if (s == null) return;
        if (!s.hiddenFactionLoadIds.Add(factionLoadId))
            s.hiddenFactionLoadIds.Remove(factionLoadId);
        BumpMarkersVersion();
        MultiplayerLoader.Multiplayer.instance?.WriteSettings();
        SoundDefOf.Click.PlayOneShotOnCamera();
    }

    private static void ToggleMuteSpectators()
    {
        var s = Multiplayer.settings;
        if (s == null) return;
        s.showSpectatorMarkers = !s.showSpectatorMarkers;
        BumpMarkersVersion();
        MultiplayerLoader.Multiplayer.instance?.WriteSettings();
        SoundDefOf.Click.PlayOneShotOnCamera();
    }

    // Mute toggles change what IsVisible() returns; downstream caches key on markersVersion.
    private static void BumpMarkersVersion()
    {
        if (Multiplayer.game?.gameComp != null)
            Multiplayer.game.gameComp.markersVersion++;
    }

    private static PingInfo FindOnlyOwnedMarker(List<PingInfo> selected)
    {
        PingInfo found = null;
        foreach (var info in selected)
        {
            if (!info.isMarker) continue;
            if (!LocationPings.CanDeleteMarker(info)) continue;
            if (found != null) return null;
            found = info;
        }
        return found;
    }

    // Walks loc.selectedMarkerIds directly (no per-map filter) so planet-view delete works too -
    // the inline action row in MarkerInspectTab routes through here when mapId == -1.
    private static void DeleteOwnedFromCurrentSelection(LocationPings loc)
    {
        var ids = new List<int>();
        foreach (var m in loc.Markers)
            if (loc.selectedMarkerIds.Contains(m.markerId) && LocationPings.CanDeleteMarker(m))
                ids.Add(m.markerId);
        if (ids.Count > 0)
            loc.SendDeleteMarkers(ids.ToArray());
        SoundDefOf.Click.PlayOneShotOnCamera();
        loc.ClearSelection();
    }

    // Constrained to fit vanilla's 75 px gizmo cell at GameFont.Tiny - defaultDesc carries the full text.
    private static string DeleteLabel()
        => MpTranslate.Fallback("MpPingSel_Delete",     "Delete");
    private static string RenameLabel()
        => MpTranslate.Fallback("MpPingSel_Rename",     "Rename");
    private static string MultiDeleteLabel(int deletableCount)
        => MpTranslate.Fallback("MpPingSel_MultiDelete", $"Delete ({deletableCount})", deletableCount);

    private static string DeselectAllMarkersLabel()
        => MpTranslate.Fallback("MpPingSel_DeselectAll", "Deselect");

    private static string MutePlayerLabel(string username)
        => MpTranslate.Fallback("MpPingSel_MutePlayer", $"Mute {username}", username);
    private static string UnmutePlayerLabel(string username)
        => MpTranslate.Fallback("MpPingSel_UnmutePlayer", $"Unmute {username}", username);

    private static string MuteFactionLabel(string factionName)
        => MpTranslate.Fallback("MpPingSel_MuteFaction", $"Mute {factionName}", factionName);
    private static string UnmuteFactionLabel(string factionName)
        => MpTranslate.Fallback("MpPingSel_UnmuteFaction", $"Unmute {factionName}", factionName);

    private static string MuteSpectatorsLabel()
        => MpTranslate.Fallback("MpPingSel_MuteSpectators",   "Mute spectators");
    private static string UnmuteSpectatorsLabel()
        => MpTranslate.Fallback("MpPingSel_UnmuteSpectators", "Unmute spectators");

    private static string TransparencyLabel()
        => MpTranslate.Fallback("MpPingSel_Transparency", "Fade");
    private static string HideLocallyLabel()
        => MpTranslate.Fallback("MpPingSel_HideLocally", "Hide for me");
    private static string UnhideLocallyLabel()
        => MpTranslate.Fallback("MpPingSel_UnhideLocally", "Show for me");
    private static string ResetLocalAppearanceLabel()
        => MpTranslate.Fallback("MpPingSel_ResetLocalAppearance", "Reset view");
}
