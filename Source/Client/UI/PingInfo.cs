using System;
using Multiplayer.API;
using Multiplayer.Client.Util;
using Multiplayer.Common.Networking.Packet;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public class PingInfo : IExposable, ISynchronizable
{
    public int player;
    public int mapId; // -1 = planet
    public PlanetTile planetTile;
    public Vector3 mapLoc;

    public PingCategory category;
    public string label = "";
    public bool isMarker;

    // ++nextMarkerId on every receiver: identical packet stream means every client picks the same id.
    public int markerId;

    // Stable identity that survives save/load and the placer disconnecting (runtime `player` is reused on rejoin).
    public string placedByUsername;
    public int placedByFactionLoadId = -1;
    public float placedByR = 1f, placedByG = 1f, placedByB = 1f;

    // Server-echoed placer TicksGame; survives save/load.
    public int placedAtTick;

    public PlayerInfo PlayerInfo => Multiplayer.session?.GetPlayerInfo(player);

    public float y = 1f;
    private float v = -3f;
    // Wall-clock - pings only. Markers short-circuit Update() before reading.
    private float lastTime = Time.time;
    private float bounceAt = Time.time + 2;
    public float timeAlive;

    public float placedAt;

    private float AlphaMult => (isMarker ? 1f : 1f - Mathf.Clamp01(timeAlive - (PingDuration - 1f))) * LocalAlphaMult();

    public GlobalTargetInfo? Target
    {
        get
        {
            if (mapId == -1)
                return new GlobalTargetInfo(planetTile);

            if (Find.Maps.GetById(mapId) is { } map)
                return new GlobalTargetInfo(mapLoc.ToIntVec3(), map);

            return null;
        }
    }

    // Markers: durable RGB snapshot. Pings: live PlayerInfo.color so color edits show immediately.
    public Color BaseColor
    {
        get
        {
            if (isMarker)
                return new Color(placedByR, placedByG, placedByB);

            var pi = PlayerInfo;
            return pi != null ? pi.color : new Color(placedByR, placedByG, placedByB);
        }
    }

    // Default pings use the placer's color. Typed pings use the pure category tint, otherwise placer + category tints muddy each other.
    public Color PinColor
    {
        get
        {
            var b = BaseColor;
            if (category == PingCategory.Default) return b;
            var t = category.Tint();
            return new Color(t.r, t.g, t.b, b.a);
        }
    }

    internal const float PingDuration = 10f;

    internal const float LabelWidth = 200f;

    // Visibility only - filtered markers still exist on every client and still sync.
    public bool IsVisible()
    {
        var s = Multiplayer.settings;
        if (s == null) return true;
        if (!string.IsNullOrEmpty(placedByUsername)
            && s.hiddenPlayerNames != null
            && s.hiddenPlayerNames.Contains(placedByUsername))
            return false;
        if (s.hiddenFactionLoadIds != null
            && s.hiddenFactionLoadIds.Contains(placedByFactionLoadId))
            return false;
        if (!s.showSpectatorMarkers)
        {
            var spec = Multiplayer.WorldComp?.spectatorFaction;
            if (spec != null && placedByFactionLoadId == spec.loadID)
                return false;
        }
        return true;
    }

    // Matches by runtime id OR durable username (survives leaves and save-load).
    public bool IsPlacedBy(int playerId, string username)
    {
        if (player == playerId) return true;
        return !string.IsNullOrEmpty(username) && placedByUsername == username;
    }

    // Placer OR map-owner-faction (multifaction) OR host bypass. UI and Receive handlers share this.
    public bool CanBeModifiedBy(int playerId, string username, int factionLoadId, bool multifaction, bool senderIsHost = false)
    {
        if (senderIsHost) return true;
        if (IsPlacedBy(playerId, username)) return true;
        if (!multifaction || mapId < 0) return false;
        if (Find.Maps.GetById(mapId) is { } map)
            return map.ParentFaction?.loadID == factionLoadId;
        return false;
    }

    public bool IsOwnedByLocalPlayer()
    {
        var sess = Multiplayer.session;
        if (sess == null) return false;
        return IsPlacedBy(sess.playerId, sess.GetPlayerInfo(sess.playerId)?.username);
    }

    // Pings never have a markerId; implicitly returns 1f.
    public float LocalAlphaMult()
    {
        if (!isMarker || markerId == 0) return 1f;
        var s = Multiplayer.settings;
        if (s == null || s.localMarkerAlpha == null) return 1f;
        if (s.localMarkerAlpha.TryGetValue(markerId, out var a))
            return Mathf.Clamp(a, 0.05f, 1f); // 0.05 floor so collapsed-state never goes to nothing
        return 1f;
    }

    // Hidden markers stay clickable (as a dot) so owner can always unhide.
    public bool IsLocallyHidden()
    {
        if (!isMarker || markerId == 0) return false;
        var s = Multiplayer.settings;
        if (s == null || s.locallyHiddenMarkers == null) return false;
        return s.locallyHiddenMarkers.Contains(markerId);
    }

    public bool Update()
    {
        if (isMarker)
        {
            y = 0f;
            v = 0f;
            return false;
        }

        float delta = Mathf.Min(Time.time - lastTime, 0.05f);
        lastTime = Time.time;

        v -= 8f * delta;
        y += v * delta;

        if (y < 0)
        {
            y = 0;
            v = Math.Max(-v / 2f - 0.5f, 0);
        }

        if (Mathf.Abs(v) < 0.0001f && y < 0.05f)
        {
            v = 0f;
            y = 0f;
        }

        if (bounceAt != 0 && Time.time > bounceAt)
        {
            v = 3f;
            bounceAt = Time.time + 2;
        }

        timeAlive += delta;

        return timeAlive > PingDuration;
    }

    public void DrawAt(Vector2 screenCenter, float size)
    {
        if (!IsVisible()) return;

        if (IsLocallyHidden() && isMarker)
        {
            DrawCollapsedDot(screenCenter, size);
            if (PingSelectionUI.IsSelected(this))
                PingSelectionUI.DrawSelectionBrackets(this, screenCenter, size);
            return;
        }

        var baseColor = BaseColor;
        var pinColor  = PinColor;

        // Brackets before the pin so the icon sits inside the bracket frame.
        if (PingSelectionUI.IsSelected(this))
            PingSelectionUI.DrawSelectionBrackets(this, screenCenter, size);

        var ringSize = size * 1.12f;
        var ringRect = new Rect(screenCenter - new Vector2(ringSize / 2f - 1f, ringSize / 2f),
            new Vector2(ringSize, ringSize));
        using (MpStyle.Set(new Color(0f, 0f, 0f, 0.45f * AlphaMult)))
            GUI.DrawTexture(ringRect.ExpandedBy(1.5f), MultiplayerStatic.PingBase);
        var groundRingColor = baseColor;
        groundRingColor.a = 0.85f * AlphaMult;
        using (MpStyle.Set(groundRingColor))
            GUI.DrawTexture(ringRect, MultiplayerStatic.PingBase);

        var pinRect = new Rect(screenCenter - new Vector2(size / 2, size + y * size), new(size, size));

        var pinDrawColor = pinColor;
        pinDrawColor.a = AlphaMult;
        using (MpStyle.Set(pinDrawColor))
            GUI.DrawTexture(pinRect, MultiplayerStatic.PingPin);

        if (category != PingCategory.Default)
        {
            var iconTex = category.Icon();
            if (iconTex != null)
            {
                var iconSize = size * 0.42f * category.IconScale();
                var headCenterY = pinRect.y + size * 0.34f;
                var iconRect = new Rect(
                    pinRect.center.x - iconSize / 2f,
                    headCenterY - iconSize / 2f,
                    iconSize,
                    iconSize);

                using (MpStyle.Set(new Color(0f, 0f, 0f, 0.6f * AlphaMult)))
                    GUI.DrawTexture(new Rect(iconRect.x + 1f, iconRect.y + 1.5f,
                        iconRect.width, iconRect.height), iconTex);
                using (MpStyle.Set(new Color(1f, 1f, 1f, AlphaMult)))
                    GUI.DrawTexture(iconRect, iconTex);
            }
            else
            {
                var glyph = category.Glyph();
                if (glyph.Length > 0)
                {
                    var glyphRect = new Rect(pinRect.x, pinRect.y + size * 0.18f, size, size * 0.32f);
                    using (MpStyle.Set(GameFont.Small).Set(TextAnchor.MiddleCenter))
                        MpUI.LabelOutlined(glyphRect, glyph,
                            new Color(1f, 1f, 1f, AlphaMult),
                            new Color(0f, 0f, 0f, 0.95f * AlphaMult));
                }
            }
        }

        var labelY = screenCenter.y + size * 0.42f;
        if (category != PingCategory.Default)
        {
            var nameRect = new Rect(screenCenter.x - LabelWidth / 2f, labelY, LabelWidth, 18f);
            using (MpStyle.Set(GameFont.Small).Set(TextAnchor.MiddleCenter))
                MpUI.LabelOutlined(nameRect, category.DisplayName(),
                    new Color(1f, 1f, 1f, AlphaMult),
                    new Color(0f, 0f, 0f, 0.95f * AlphaMult));
            labelY += 18f;
        }

        if (!string.IsNullOrEmpty(label))
        {
            var labelRect = new Rect(screenCenter.x - LabelWidth / 2f, labelY, LabelWidth, 16f);
            using (MpStyle.Set(GameFont.Tiny).Set(TextAnchor.MiddleCenter))
                MpUI.LabelOutlined(labelRect, label,
                    new Color(1f, 1f, 1f, AlphaMult),
                    new Color(0f, 0f, 0f, 0.9f * AlphaMult));
        }
    }

    // Small owner-tinted dot for locally-hidden markers.
    private void DrawCollapsedDot(Vector2 screenCenter, float size)
    {
        var dotSize = size * 0.30f;
        var rect = new Rect(screenCenter.x - dotSize / 2f, screenCenter.y - dotSize / 2f, dotSize, dotSize);
        var color = BaseColor;
        color.a = 0.55f;
        using (MpStyle.Set(new Color(0f, 0f, 0f, 0.5f)))
            GUI.DrawTexture(rect.ExpandedBy(1.5f), MultiplayerStatic.PingCircle);
        using (MpStyle.Set(color))
            GUI.DrawTexture(rect, MultiplayerStatic.PingCircle);
    }

    public void Sync(SyncWorker sync)
    {
        sync.Bind(ref player);
        sync.Bind(ref mapId);

        // PlanetTile.layerId is private - round-trip components manually.
        int tileId = planetTile.tileId;
        int layerId = planetTile.layerId;
        sync.Bind(ref tileId);
        sync.Bind(ref layerId);
        if (!sync.isWriting)
            planetTile = new PlanetTile(tileId, layerId);

        sync.Bind(ref mapLoc);

        byte cat = (byte)category;
        sync.Bind(ref cat);
        if (!sync.isWriting)
            category = (PingCategory)cat;

        sync.Bind(ref label);
        sync.Bind(ref isMarker);
        sync.Bind(ref markerId);
        sync.Bind(ref placedByUsername);
        sync.Bind(ref placedByFactionLoadId);
        sync.Bind(ref placedByR);
        sync.Bind(ref placedByG);
        sync.Bind(ref placedByB);
        sync.Bind(ref placedAtTick);
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref player, "player", -1);
        Scribe_Values.Look(ref mapId, "mapId", -1);

        // PlanetTile is a readonly struct with private layerId - no Scribe overload, so manual.
        int tileId = planetTile.tileId;
        int layerId = planetTile.layerId;
        Scribe_Values.Look(ref tileId, "planetTileId", -1);
        Scribe_Values.Look(ref layerId, "planetTileLayer", -1);
        if (Scribe.mode == LoadSaveMode.LoadingVars)
            planetTile = new PlanetTile(tileId, layerId);

        Scribe_Values.Look(ref mapLoc, "mapLoc");
        Scribe_Values.Look(ref category, "category", PingCategory.Default);
        Scribe_Values.Look(ref label, "label", "");
        Scribe_Values.Look(ref isMarker, "isMarker");
        Scribe_Values.Look(ref markerId, "markerId");

        Scribe_Values.Look(ref placedByUsername, "placedByUsername");
        Scribe_Values.Look(ref placedByFactionLoadId, "placedByFactionLoadId", -1);
        Scribe_Values.Look(ref placedByR, "placedByR", 1f);
        Scribe_Values.Look(ref placedByG, "placedByG", 1f);
        Scribe_Values.Look(ref placedByB, "placedByB", 1f);
        Scribe_Values.Look(ref placedAtTick, "placedAtTick");
    }
}
