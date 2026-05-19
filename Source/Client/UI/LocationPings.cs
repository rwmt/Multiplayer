using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Multiplayer.Client.Util;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client;

public partial class LocationPings
{
    public List<PingInfo> pings = new();
    // Bumped on every mutation of `pings`. Paired with markersVersion as PingMenuWindow's row-cache key.
    public int pingsVersion;
    // Null-safe so callers can hit this before MultiplayerGame exists.
    public IReadOnlyList<PingInfo> Markers => Multiplayer.game?.gameComp?.AllMarkers ?? Array.Empty<PingInfo>();

    public bool alertHidden;
    private int pingJumpCycle;

    public bool wheelActive;
    public Vector2 wheelScreenOrigin;
    private int wheelTargetMapId;
    private PlanetTile wheelTargetTile;
    private Vector3 wheelTargetMapLoc;
    private float? pingKeyDownTime;
    private PingCategory hoveredCategory;

    public static bool MenuWindowOpen => PingMenuWindow.Opened != null;

    // Wheel-slice click sets this; LMB on the map drops a ping/marker until disarmed.
    public PingCategory? armedCategory;
    public bool ArmedAsMarker => Multiplayer.settings.pingPlaceMode == PingPlaceMode.Marker;

    // Reset each launch so opening the drawer next session doesn't pre-arm a stale category.
    public PingCategory? lastUsedCategory;

    public HashSet<int> selectedMarkerIds = new();
    public HashSet<int> selectedPingPlayerIds = new();
    // Bumped on every user-initiated selection mutation. Reactive removals (marker delete, ping
    // expire) skip the bump - markersVersion / pingsVersion already invalidates downstream caches.
    public int selectionVersion;

    // Reuse list - vanilla's gizmo grid is reference-cache-keyed.
    internal List<Gizmo> cachedGizmos;
    internal GizmoCacheKey cachedGizmoKey = GizmoCacheKey.Invalid;

    // Reused buffer for the selectedObjects + gizmos merge in PingGizmoInjectPatch. Cleared and
    // refilled each frame so no fresh List allocates while the player has a selection in view.
    // Item refs (Things + cached Gizmos) stay stable between frames on no-op input, keeping
    // vanilla's downstream gizmo-grid cache hot.
    internal readonly List<object> gizmoInjectionBuffer = new();

    // Cached analysis backing PingSelectionUI.DrawInlineActionsRow.
    internal int cachedInlineOwnedCount;
    internal PingInfo cachedInlineOnlyOwnedSingle;
    internal string cachedInlineForeignSampleUsername;
    internal int cachedInlineForeignSampleFactionId;
    internal bool cachedInlineForeignSpectatorPresent;
    internal readonly List<int> cachedInlineMarkerIds = new();
    internal List<(string label, Action onClick)> cachedInlineActions;
    internal int cachedInlineMarkersV = -1;
    internal int cachedInlineSelectionV = -1;
    internal int cachedInlineFactionId = int.MinValue;

    // Cached planet-view marker selection backing MarkerInspectTab.CollectSelectedPlanetMarkers.
    // Vanilla calls IsVisible / StillValid several times per frame; the cache keeps those calls O(1).
    // hasResult disambiguates an empty cache (no markers selected - return null) from a fresh
    // sentinel state. pingsVersion is intentionally excluded - ephemeral pings don't appear here.
    internal readonly List<PingInfo> cachedPlanetMarkers = new();
    internal bool cachedPlanetMarkersHasResult;
    internal int cachedPlanetMarkersV = -1;
    internal int cachedPlanetSelectionV = -1;

    // factionId == -1 sentinel = single-player / pre-handshake. markersVersion picks up local-only
    // mutations (hide/alpha) that don't add or remove markers. selectionVersion picks up reselection
    // when owned/foreign counts happen to match.
    internal struct GizmoCacheKey
    {
        public int owned;
        public int foreign;
        public int renameTargetId;
        public int factionId;
        public int markersVersion;
        public int selectionVersion;

        public static GizmoCacheKey Invalid => new()
        {
            owned = -1, foreign = -1, renameTargetId = 0, factionId = -1,
            markersVersion = -1, selectionVersion = -1,
        };

        public bool Matches(int owned, int foreign, int rename, int faction, int markersV, int selectionV)
            => this.owned == owned && this.foreign == foreign && renameTargetId == rename
               && factionId == faction && markersVersion == markersV && selectionVersion == selectionV;
    }

    public int SelectedCount => selectedMarkerIds.Count + selectedPingPlayerIds.Count;
    public bool HasSelection => SelectedCount > 0;
    public bool IsMarkerSelected(int markerId) => selectedMarkerIds.Contains(markerId);
    public bool IsPingSelected(int playerId)   => selectedPingPlayerIds.Contains(playerId);

    // Local-player view of PingInfo.CanBeModifiedBy. Used by the UI's delete/rename gate; the
    // Receive handlers call CanBeModifiedBy directly with the sender's identity.
    public static bool CanDeleteMarker(PingInfo info)
    {
        var sess = Multiplayer.session;
        if (sess == null) return false;
        var meName = sess.GetPlayerInfo(sess.playerId)?.username;
        var multifaction = Multiplayer.game?.gameComp?.multifaction ?? false;
        var meFactionId = Multiplayer.RealPlayerFaction?.loadID ?? -1;
        return info.CanBeModifiedBy(sess.playerId, meName, meFactionId, multifaction);
    }

    // Caps on-screen pin/ring size as zoom changes. Shared by render + hit-test.
    public static float OnScreenPingSize => Math.Min(UI.CurUICellSize() * 4, 32f);

    public static PingInfo FindMarkerById(int markerId)
    {
        var comp = Multiplayer.game?.gameComp;
        if (comp == null) return null;
        foreach (var bucket in comp.markersByFaction.Values)
            for (int i = 0; i < bucket.Count; i++)
                if (bucket[i].markerId == markerId)
                    return bucket[i];
        return null;
    }

    public void SelectInfo(PingInfo info, bool additive)
    {
        if (!additive) ClearSelection();
        if (info.isMarker) selectedMarkerIds.Add(info.markerId);
        else               selectedPingPlayerIds.Add(info.player);
        selectionVersion++;
        SelectionDrawer.Notify_Selected(info);
    }

    public void ToggleSelection(PingInfo info)
    {
        if (info.isMarker) selectedMarkerIds.Remove(info.markerId);
        else               selectedPingPlayerIds.Remove(info.player);
        selectionVersion++;
    }

    public void UpdatePing()
    {
        // Replay scrub transiently empties Find.Maps; let the replay's section snapshots restore state and skip our sweep.
        if (Multiplayer.IsReplay)
        {
            if (wheelActive) CancelWheel();
            if (armedCategory != null) DisarmPlacement(playSound: false);
            ClearSelection();
            return;
        }

        // Sweep markers whose Target turned null (map unloaded, area removed, etc). Runs on the
        // arbiter too - scribed state, so drops here have to match the host's save.
        if (Multiplayer.game?.gameComp is { } comp)
        {
            foreach (var bucket in comp.markersByFaction.Values)
            {
                for (int i = bucket.Count - 1; i >= 0; i--)
                {
                    var m = bucket[i];
                    m.Update();
                    if (m.Target == null)
                    {
                        SelectionDrawer.selectTimes.Remove(m);
                        selectedMarkerIds.Remove(m.markerId);
                        // Drop the per-client appearance override too - its markerId is dead now.
                        var s = Multiplayer.settings;
                        if (s != null && m.markerId != 0)
                        {
                            s.localMarkerAlpha?.Remove(m.markerId);
                            s.locallyHiddenMarkers?.Remove(m.markerId);
                        }
                        bucket.RemoveAt(i);
                        comp.markersVersion++;
                    }
                }
            }
            PruneEmptyFactionBuckets(comp);
        }

        if (Multiplayer.arbiterInstance) return;

        var pingsEnabled = !TickPatch.Simulating && Multiplayer.settings.enablePings;

        if (!pingsEnabled)
        {
            if (wheelActive) CancelWheel();
        }
        else
        {
            HandleWheelEligibleInput();
            HandleLegacyMouse2();
        }

        if (armedCategory != null)
        {
            var designatorActive = Find.DesignatorManager?.SelectedDesignator != null;
            var targeterActive   = Find.Targeter?.IsTargeting ?? false;
            var worldTargeterActive = Find.WorldTargeter?.IsTargeting ?? false;
            if (designatorActive || targeterActive || worldTargeterActive)
                DisarmPlacement(playSound: false);
        }

        for (int i = pings.Count - 1; i >= 0; i--)
        {
            var ping = pings[i];
            if (ping.Update() || ping.PlayerInfo == null || ping.Target == null)
            {
                selectedPingPlayerIds.Remove(ping.player);
                SelectionDrawer.selectTimes.Remove(ping);
                pings.RemoveAt(i);
                pingsVersion++;
            }
        }

        if (pingsEnabled && KeyTriggered(Multiplayer.settings.jumpToPingButton))
        {
            pingJumpCycle++;

            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                alertHidden = true;
            else if (pings.Count > 0
                && pings[GenMath.PositiveMod(pingJumpCycle, pings.Count)].Target is { } jumpTarget)
                CameraJumper.TryJump(jumpTarget);
        }
    }

    private void HandleWheelEligibleInput()
    {
        if (MenuWindowOpen)
        {
            pingKeyDownTime = null;
            wheelActive = false;
            return;
        }

        var sk = Multiplayer.settings.sendPingButton;
        var skUsable = sk is { } skv && skv != KeyCode.Mouse2;
        var skv2 = sk.GetValueOrDefault();

        bool pressedNow = MultiplayerStatic.PingKeyDef.JustPressed
                          || (skUsable && Input.GetKeyDown(skv2));
        bool heldNow = MultiplayerStatic.PingKeyDef.IsDown
                       || (skUsable && Input.GetKey(skv2));

        if (pressedNow && pingKeyDownTime == null)
        {
            if (TryCaptureTarget(out var mapId, out var tile, out var mapLoc))
            {
                pingKeyDownTime = Time.time;
                wheelTargetMapId = mapId;
                wheelTargetTile = tile;
                wheelTargetMapLoc = mapLoc;
                wheelScreenOrigin = UI.MousePositionOnUIInverted;
                hoveredCategory = PingCategory.Default;
            }
        }

        if (pingKeyDownTime is { } downTime)
        {
            var hold = Time.time - downTime;

            if (!wheelActive && heldNow
                && Multiplayer.settings.enablePingWheel
                && hold >= Multiplayer.settings.pingWheelHoldDelay)
            {
                wheelActive = true;
            }

            if (wheelActive)
                hoveredCategory = ComputeHoveredCategory();

            if (!heldNow)
            {
                // Release in center = cancel; release on a slice = typed ping; quick tap (no wheel) = default.
                PingCategory? toFire = wheelActive
                    ? (hoveredCategory == PingCategory.Default ? null : (PingCategory?)hoveredCategory)
                    : PingCategory.Default;

                if (toFire is { } cat)
                {
                    var asMarker = Multiplayer.settings.pingPlaceMode == PingPlaceMode.Marker;
                    FirePing(wheelTargetMapId, wheelTargetTile, wheelTargetMapLoc, cat, asMarker);
                }

                CancelWheel();
            }
        }
    }

    // Mouse2 hold conflicts with vanilla camera-drag, so it gets the no-wheel quick-tap path.
    private void HandleLegacyMouse2()
    {
        if (Multiplayer.settings.sendPingButton != KeyCode.Mouse2) return;
        if (MenuWindowOpen) return;
        if (!MpInput.Mouse2UpWithoutDrag) return;
        if (!TryCaptureTarget(out var mapId, out var tile, out var mapLoc)) return;
        var asMarker = Multiplayer.settings.pingPlaceMode == PingPlaceMode.Marker;
        FirePing(mapId, tile, mapLoc, PingCategory.Default, asMarker);
    }

    private void ToggleDrawer()
    {
        var existing = PingMenuWindow.Opened;
        if (existing != null)
        {
            // Silent so PostClose's FloatMenu_Cancel isn't doubled.
            DisarmPlacement(playSound: false);
            existing.Close();
            return;
        }

        if (wheelScreenOrigin == Vector2.zero)
            wheelScreenOrigin = new Vector2(UI.screenWidth / 2f, UI.screenHeight / 2f);

        Find.WindowStack.Add(new PingMenuWindow());
        SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();
    }

    public void ArmPlacement(PingCategory category, bool playSound = true)
    {
        armedCategory = category;
        if (playSound)
            SoundDefOf.Click.PlayOneShotOnCamera();
    }

    public void DisarmPlacement(bool playSound = true)
    {
        if (armedCategory == null) return;
        armedCategory = null;
        if (playSound)
            SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
    }

    public bool FireArmedAtMap(int mapId, PlanetTile tile, Vector3 mapLoc)
    {
        if (armedCategory is not { } cat) return false;
        FirePing(mapId, tile, mapLoc, cat, ArmedAsMarker);
        return true;
    }

    public void JumpToAndSelect(PingInfo info)
    {
        if (info.Target is { } target)
            CameraJumper.TryJump(target);

        SelectInfo(info, additive: false);
    }

    // SelectionDrawer.selectTimes uses ref-equality keys; sweep on session-end / load.
    public static void DropStaleSelectTimes()
    {
        var times = SelectionDrawer.selectTimes;
        if (times.Count == 0) return;
        List<object> stale = null;
        foreach (var key in times.Keys)
            if (key is PingInfo)
                (stale ??= new List<object>()).Add(key);
        if (stale != null)
            foreach (var k in stale)
                times.Remove(k);
    }

    public void ClearSelection()
    {
        if (selectedMarkerIds.Count > 0)
            foreach (var m in Markers)
                if (selectedMarkerIds.Contains(m.markerId))
                    SelectionDrawer.selectTimes.Remove(m);
        if (selectedPingPlayerIds.Count > 0)
            foreach (var p in pings)
                if (selectedPingPlayerIds.Contains(p.player))
                    SelectionDrawer.selectTimes.Remove(p);

        selectedMarkerIds.Clear();
        selectedPingPlayerIds.Clear();
        selectionVersion++;
    }

    private bool TryCaptureTarget(out int mapId, out PlanetTile tile, out Vector3 mapLoc)
    {
        mapId = -1;
        tile = PlanetTile.Invalid;
        mapLoc = Vector3.zero;

        if (WorldRendererUtility.WorldSelected)
        {
            var t = GenWorld.MouseTile();
            if (!t.Valid) t = GenWorld.MouseTile(true);
            if (!t.Valid) return false;
            tile = t;
            return true;
        }

        if (Find.CurrentMap != null)
        {
            mapId = Find.CurrentMap.uniqueID;
            mapLoc = UI.MouseMapPosition();
            return true;
        }

        return false;
    }

    private void CancelWheel()
    {
        wheelActive = false;
        pingKeyDownTime = null;
    }

    // Strip control characters - keeps crafted packets from injecting weird text.
    private static string SanitizeLabel(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
            if (!char.IsControl(c)) sb.Append(c);
        return sb.ToString();
    }

    private void FirePing(int mapId, PlanetTile tile, Vector3 mapLoc, PingCategory category, bool asMarker)
    {
        if (Multiplayer.arbiterInstance) return;
        if (Multiplayer.Client == null) return;
        // Stamp from the placer; server relays unchanged so every receiver agrees on the value.
        var tick = Find.TickManager?.TicksGame ?? 0;
        Multiplayer.Client.Send(new ClientPingLocPacket(
            mapId, tile.tileId, tile.layerId,
            mapLoc.x, mapLoc.y, mapLoc.z,
            (byte)category, asMarker, "", tick));
        if (category != PingCategory.Default)
            lastUsedCategory = category;
        category.Sound().PlayOneShotOnCamera();
    }

    // Mouse2 reports the *release* (UpWithoutDrag) because hold is reserved for camera-drag;
    // every other key returns the press edge.
    private static bool KeyTriggered(KeyCode? keyNullable)
    {
        if (keyNullable == KeyCode.Mouse2)
            return MpInput.Mouse2UpWithoutDrag;

        if (keyNullable is not { } key) return false;

        return Input.GetKeyDown(key);
    }

    public void SendRenameMarker(int markerId, string label)
    {
        if (markerId == 0) return;
        // Modal can outlive the connection - drop silently if so.
        if (Multiplayer.Client == null) return;
        var safe = SanitizeLabel(label);
        if (safe.Length > PingCategoryWire.MaxLabelChars)
            safe = safe.Substring(0, PingCategoryWire.MaxLabelChars);
        Multiplayer.Client.Send(new ClientRenameMarkerPacket(markerId, safe));
    }

    public void SendDeleteMarker(int markerId)
    {
        if (markerId == 0 || Multiplayer.Client == null) return;
        Multiplayer.Client.Send(new ClientDeleteMarkerPacket(new[] { markerId }));
    }

    public void SendDeleteMarkers(int[] markerIds)
    {
        if (markerIds == null || markerIds.Length == 0 || Multiplayer.Client == null) return;
        Multiplayer.Client.Send(new ClientDeleteMarkerPacket(markerIds));
    }

    public void SendClearMyMarkers()
    {
        if (Multiplayer.Client == null) return;
        Multiplayer.Client.Send(new ClientClearMarkersPacket((byte)PingMarkerClearMode.Mine, -1, ""));
    }

    public void SendClearMyMarkersOnMap(int mapId)
    {
        if (Multiplayer.Client == null) return;
        Multiplayer.Client.Send(new ClientClearMarkersPacket((byte)PingMarkerClearMode.OnMap, mapId, ""));
    }

    public void SendClearMarkersFromPlayer(string username)
    {
        if (string.IsNullOrEmpty(username) || Multiplayer.Client == null) return;
        Multiplayer.Client.Send(new ClientClearMarkersPacket((byte)PingMarkerClearMode.FromPlayer, -1, username));
    }

    // Host-only - server enforces the gate; client-side this is a UI button on PingHostSettingsDialog.
    public void SendClearAllMarkers()
    {
        if (Multiplayer.Client == null) return;
        Multiplayer.Client.Send(new ClientClearMarkersPacket((byte)PingMarkerClearMode.AllMarkers, -1, ""));
    }

    public void SendClearAllPings()
    {
        if (Multiplayer.Client == null) return;
        Multiplayer.Client.Send(new ClientClearMarkersPacket((byte)PingMarkerClearMode.AllPings, -1, ""));
    }
}
