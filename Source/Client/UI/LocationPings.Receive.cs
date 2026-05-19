using System;
using System.Collections.Generic;
using Multiplayer.Client.Comp;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client;

public partial class LocationPings
{
    // Re-validate every wire field - defends against crafted packets, keeps arbiter in lock-step.
    public void ReceivePing(ServerPingLocPacket packet)
    {
        var data = packet.data;
        var planetTile = new PlanetTile(data.planetTileId, data.planetTileLayer);
        if (data.mapId == -1 && !planetTile.Valid)
            return;
        if (!float.IsFinite(data.x) || !float.IsFinite(data.y) || !float.IsFinite(data.z))
            return;
        // Sender stamps TicksGame >= 0; a crafted negative would render as "in the future" in the pane.
        if (data.placedAtTick < 0)
            return;
        // PlanetTile.Layer throws on unknown layerId - guard against host having a layer mod the joiner lacks.
        if (data.mapId == -1)
        {
            if (data.planetTileLayer < 0) return;
            var layers = Find.WorldGrid?.PlanetLayers;
            if (layers == null || !layers.ContainsKey(data.planetTileLayer))
                return;
        }

        var category = PingCategoryWire.IsValid(data.category) ? (PingCategory)data.category : PingCategory.Default;
        var label = SanitizeLabel(data.label);

        var info = new PingInfo
        {
            player = packet.playerId,
            mapId = data.mapId,
            planetTile = planetTile,
            mapLoc = new Vector3(data.x, data.y, data.z),
            category = category,
            label = label,
            isMarker = data.isMarker,
            placedByUsername = string.IsNullOrEmpty(packet.username) ? null : packet.username,
            placedByFactionLoadId = packet.factionId,
            placedByR = packet.r / 255f,
            placedByG = packet.g / 255f,
            placedByB = packet.b / 255f,
            placedAtTick = data.placedAtTick,
        };

        if (data.isMarker)
        {
            // Scribed - must run on every receiver INCLUDING arbiter, ignoring enablePings.
            var comp = Multiplayer.game?.gameComp;
            if (comp == null) return;
            var bucket = comp.GetOrCreateFactionMarkers(packet.factionId);
            EnforceMarkerCap(comp, bucket, packet.playerId, info.placedByUsername);
            info.markerId = ++comp.nextMarkerId;
            // UI-only wall-clock - restored markers stay at 0 so the fresh-marker alert window stays closed.
            if (!Multiplayer.arbiterInstance) info.placedAt = Time.realtimeSinceStartup;
            bucket.Add(info);
            comp.markersVersion++;
            if (!Multiplayer.arbiterInstance) alertHidden = false;
        }
        else
        {
            // Pings are ephemeral - arbiter is gated below so its list doesn't grow unbounded.
            if (!Multiplayer.settings.enablePings) return;
            if (Multiplayer.arbiterInstance) return;
            pings.RemoveAll(p => p.player == packet.playerId);
            pings.Add(info);
            pingsVersion++;
            alertHidden = false;
        }

        // Mute also suppresses SFX; IsVisible covers the three filter axes plus spectator toggle.
        if (Multiplayer.settings.enablePings
            && Multiplayer.session != null && packet.playerId != Multiplayer.session.playerId
            && !Multiplayer.arbiterInstance
            && info.IsVisible())
            category.Sound().PlayOneShotOnCamera();
    }

    // Mutates scribed state - runs on every receiver INCLUDING the arbiter (no early return).
    public void ReceiveDeleteMarker(ServerDeleteMarkerPacket packet)
    {
        var comp = Multiplayer.game?.gameComp;
        if (comp == null) return;
        var ids = packet.data.markerIds;
        if (ids == null || ids.Length == 0) return;

        var idSet = new HashSet<int>(ids);
        if (idSet.Count == 0) return;

        foreach (var bucket in comp.markersByFaction.Values)
        {
            for (int i = bucket.Count - 1; i >= 0; i--)
            {
                var m = bucket[i];
                if (idSet.Contains(m.markerId)
                    && m.CanBeModifiedBy(packet.playerId, packet.username, packet.factionId, comp.multifaction, packet.senderIsHost))
                {
                    SelectionDrawer.selectTimes.Remove(m);
                    DropLocalAppearanceFor(m.markerId);
                    bucket.RemoveAt(i);
                    comp.markersVersion++;
                }
            }
        }
        foreach (var id in idSet)
            selectedMarkerIds.Remove(id);
        PruneEmptyFactionBuckets(comp);
    }

    // Per-marker overrides are keyed by markerId; sweep when the marker dies.
    private static void DropLocalAppearanceFor(int markerId)
    {
        var s = Multiplayer.settings;
        if (s == null || markerId == 0) return;
        s.localMarkerAlpha?.Remove(markerId);
        s.locallyHiddenMarkers?.Remove(markerId);
    }

    public void ReceiveRenameMarker(ServerRenameMarkerPacket packet)
    {
        var comp = Multiplayer.game?.gameComp;
        if (comp == null) return;
        var markerId = packet.data.markerId;
        if (markerId == 0) return;

        var label = SanitizeLabel(packet.data.label);
        if (label.Length > PingCategoryWire.MaxLabelChars)
            label = label.Substring(0, PingCategoryWire.MaxLabelChars);

        foreach (var bucket in comp.markersByFaction.Values)
        {
            for (int i = 0; i < bucket.Count; i++)
            {
                var m = bucket[i];
                if (m.markerId == markerId
                    && m.CanBeModifiedBy(packet.playerId, packet.username, packet.factionId, comp.multifaction, packet.senderIsHost))
                {
                    m.label = label;
                    comp.markersVersion++;
                    return;
                }
            }
        }
    }

    public void ReceiveClearMarkers(ServerClearMarkersPacket packet)
    {
        if (!PingMarkerClearWire.IsValid(packet.data.mode)) return;
        var comp = Multiplayer.game?.gameComp;
        if (comp == null) return;

        var mode = (PingMarkerClearMode)packet.data.mode;
        var senderId = packet.playerId;
        var senderUsername = packet.username;
        var mapId = packet.data.mapId;

        // Clear is placer-only - letting a faction-mate wipe your markers isn't a useful action.
        switch (mode)
        {
            case PingMarkerClearMode.Mine:
                foreach (var bucket in comp.markersByFaction.Values)
                    RemoveMarkersWhere(comp, bucket, m => m.IsPlacedBy(senderId, senderUsername));
                break;
            case PingMarkerClearMode.OnMap:
                // mapId == -1 is the planet sentinel - never a valid OnMap target.
                if (mapId < 0) break;
                foreach (var bucket in comp.markersByFaction.Values)
                    RemoveMarkersWhere(comp, bucket, m => m.mapId == mapId && m.IsPlacedBy(senderId, senderUsername));
                break;
            case PingMarkerClearMode.FromPlayer:
                // Username is canonical across sessions; anyone can wipe by name (self-policing).
                var target = packet.data.targetUsername;
                if (string.IsNullOrEmpty(target)) break;
                foreach (var bucket in comp.markersByFaction.Values)
                    RemoveMarkersWhere(comp, bucket, m => m.placedByUsername == target);
                break;
            case PingMarkerClearMode.AllMarkers:
                // Host-only blanket wipe; receiver re-checks senderIsHost.
                if (!packet.senderIsHost) break;
                foreach (var bucket in comp.markersByFaction.Values)
                    RemoveMarkersWhere(comp, bucket, _ => true);
                break;
            case PingMarkerClearMode.AllPings:
                if (!packet.senderIsHost) break;
                if (pings.Count > 0)
                {
                    foreach (var p in pings)
                        SelectionDrawer.selectTimes.Remove(p);
                    selectedPingPlayerIds.Clear();
                    pings.Clear();
                    pingsVersion++;
                }
                break;
        }
        PruneEmptyFactionBuckets(comp);
    }

    // Counts across all buckets - faction-switchers' old markers still count toward their cap.
    private static void EnforceMarkerCap(MultiplayerGameComp comp, List<PingInfo> targetBucket, int playerId, string username)
    {
        var cap = MarkerCap;
        bool MatchesPlacer(PingInfo m) => m.IsPlacedBy(playerId, username);

        int Count()
        {
            int n = 0;
            foreach (var b in comp.markersByFaction.Values)
                for (int i = 0; i < b.Count; i++)
                    if (MatchesPlacer(b[i])) n++;
            return n;
        }

        var loc = Multiplayer.session?.locationPings;
        while (Count() >= cap)
        {
            if (!EvictOneOldest(comp, targetBucket, MatchesPlacer, loc)) break;
        }
    }

    // SortedDictionary enumeration order is the same on every client - deterministic fallback.
    private static bool EvictOneOldest(MultiplayerGameComp comp, List<PingInfo> preferred, Predicate<PingInfo> match, LocationPings loc)
    {
        if (TryEvictFrom(comp, preferred, match, loc)) return true;
        foreach (var b in comp.markersByFaction.Values)
            if (b != preferred && TryEvictFrom(comp, b, match, loc)) return true;
        return false;
    }

    private static bool TryEvictFrom(MultiplayerGameComp comp, List<PingInfo> bucket, Predicate<PingInfo> match, LocationPings loc)
    {
        for (int i = 0; i < bucket.Count; i++)
        {
            if (match(bucket[i]))
            {
                SelectionDrawer.selectTimes.Remove(bucket[i]);
                loc?.selectedMarkerIds.Remove(bucket[i].markerId);
                DropLocalAppearanceFor(bucket[i].markerId);
                bucket.RemoveAt(i);
                comp.markersVersion++;
                return true;
            }
        }
        return false;
    }

    private static void PruneEmptyFactionBuckets(MultiplayerGameComp comp)
    {
        List<int> empties = null;
        foreach (var kv in comp.markersByFaction)
            if (kv.Value.Count == 0)
                (empties ??= new List<int>()).Add(kv.Key);
        if (empties == null) return;
        foreach (var key in empties)
            comp.markersByFaction.Remove(key);
        comp.markersVersion++;
    }

    private static int MarkerCap => Mathf.Max(PingMarkerCap.Min, Multiplayer.game?.gameComp?.markerCapPerPlayer ?? PingMarkerCap.Default);

    private void RemoveMarkersWhere(MultiplayerGameComp comp, List<PingInfo> markers, Predicate<PingInfo> match)
    {
        for (int i = markers.Count - 1; i >= 0; i--)
        {
            if (match(markers[i]))
            {
                SelectionDrawer.selectTimes.Remove(markers[i]);
                selectedMarkerIds.Remove(markers[i].markerId);
                DropLocalAppearanceFor(markers[i].markerId);
                markers.RemoveAt(i);
                comp.markersVersion++;
            }
        }
    }
}
