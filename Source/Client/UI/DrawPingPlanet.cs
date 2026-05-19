using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.Client.Util;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(ExpandableWorldObjectsUtility), nameof(ExpandableWorldObjectsUtility.ExpandableWorldObjectsOnGUI))]
    static class DrawPingPlanet
    {
        // Per-frame reused so the cluster pass doesn't allocate during render.
        private static readonly Dictionary<PlanetTile, List<PingInfo>> tileGroups = new();

        static void Postfix()
        {
            if (Multiplayer.Client == null || Multiplayer.arbiterInstance || TickPatch.Simulating) return;
            if (Event.current.type != EventType.Repaint) return;
            if (!WorldRendererUtility.WorldSelected) return;

            var loc = Multiplayer.session?.locationPings;
            if (loc == null) return;

            // Cluster co-located markers per tile so labels don't stack. Locally-hidden markers
            // bypass clustering so their collapsed-dot branch still runs.
            tileGroups.Clear();
            foreach (var marker in loc.Markers)
            {
                if (marker.mapId != -1) continue;
                if (!marker.IsVisible()) continue;
                if (marker.IsLocallyHidden())
                {
                    DrawOneOnPlanet(marker);
                    continue;
                }
                if (!tileGroups.TryGetValue(marker.planetTile, out var list))
                {
                    list = new List<PingInfo>();
                    tileGroups[marker.planetTile] = list;
                }
                list.Add(marker);
            }

            foreach (var kv in tileGroups)
            {
                if (kv.Value.Count == 1)
                    DrawOneOnPlanet(kv.Value[0]);
                else
                    DrawClusterOnPlanet(kv.Key, kv.Value);
            }
            tileGroups.Clear();

            // Pings stay individual - short-lived, bounce-animated, and clustering would lose that.
            foreach (var ping in loc.pings) DrawOneOnPlanet(ping);
        }

        private static void DrawOneOnPlanet(PingInfo ping)
        {
            if (ping.mapId != -1) return;
            // Markers keep their durable color snapshot if placer left; pings need live PlayerInfo.
            if (!ping.isMarker && ping.PlayerInfo == null) return;
            if (!TryResolveLayerForRender(ping.planetTile)) return;

            var grid = Find.WorldGrid;
            if (grid == null) return;
            var tileCenter = GenWorldUI.WorldToUIPosition(grid.GetTileCenter(ping.planetTile));
            const float size = 30f;
            ping.DrawAt(tileCenter, size);
        }

        // Tile renders if currently-selected layer matches (cross-layer zoom traversal optional).
        private static bool TryResolveLayerForRender(PlanetTile tile)
        {
            PlanetLayer pingLayer;
            // PlanetTile.Layer throws KeyNotFoundException on unknown layerId - see ReceivePing.
            try { pingLayer = tile.Layer; }
            catch (System.Collections.Generic.KeyNotFoundException) { return false; }
            if (pingLayer == null) return false;

            var layer = Find.WorldSelector?.SelectedLayer;
            if (layer == null) return false;
            if (Multiplayer.settings.enableCrossPlanetLayerPings)
            {
                // Cap at 25 to defend against a malformed layer graph forming a cycle.
                for (var i = 0; i < 25; i++)
                {
                    if (layer == null || layer == pingLayer) break;
                    layer = layer.zoomInToLayer;
                }
            }
            return pingLayer == layer;
        }

        // Cluster pin: ring + neutral pin head, count badge, single "N markers" label.
        private static void DrawClusterOnPlanet(PlanetTile tile, List<PingInfo> group)
        {
            if (!TryResolveLayerForRender(tile)) return;

            var grid = Find.WorldGrid;
            if (grid == null) return;
            var tileCenter = GenWorldUI.WorldToUIPosition(grid.GetTileCenter(tile));
            const float size = 30f;

            // Show selection brackets if ANY group member is selected; anchor on a selected one
            // so the bracket animation locks to a stable key.
            PingInfo selectedSample = null;
            for (var i = 0; i < group.Count; i++)
                if (PingSelectionUI.IsSelected(group[i])) { selectedSample = group[i]; break; }
            if (selectedSample != null)
                PingSelectionUI.DrawSelectionBrackets(selectedSample, tileCenter, size);

            // Ring tint: selected member's color (so the local player's own color comes through
            // when they're part of the cluster), else first group member as a stable fallback.
            var ringColor = (selectedSample ?? group[0]).BaseColor;
            var ringSize = size * 1.12f;
            var ringRect = new Rect(tileCenter - new Vector2(ringSize / 2f - 1f, ringSize / 2f),
                new Vector2(ringSize, ringSize));
            using (MpStyle.Set(new Color(0f, 0f, 0f, 0.45f)))
                GUI.DrawTexture(ringRect.ExpandedBy(1.5f), MultiplayerStatic.PingBase);
            var groundRingColor = ringColor; groundRingColor.a = 0.85f;
            using (MpStyle.Set(groundRingColor))
                GUI.DrawTexture(ringRect, MultiplayerStatic.PingBase);

            // Pin head - neutral gray so the count badge stays legible regardless of placer color.
            var pinRect = new Rect(tileCenter - new Vector2(size / 2f, size), new Vector2(size, size));
            using (MpStyle.Set(new Color(0.82f, 0.82f, 0.82f, 1f)))
                GUI.DrawTexture(pinRect, MultiplayerStatic.PingPin);

            // Numeric badge centered on the pin head.
            var countRect = new Rect(pinRect.x, pinRect.y + size * 0.10f, size, size * 0.42f);
            using (MpStyle.Set(GameFont.Small).Set(TextAnchor.MiddleCenter))
                MpUI.LabelOutlined(countRect, group.Count.ToString(),
                    new Color(1f, 1f, 1f, 1f),
                    new Color(0f, 0f, 0f, 0.95f));

            // Single "N markers" label per tile cluster.
            var labelRect = new Rect(tileCenter.x - PingInfo.LabelWidth / 2f, tileCenter.y + size * 0.42f, PingInfo.LabelWidth, 18f);
            using (MpStyle.Set(GameFont.Small).Set(TextAnchor.MiddleCenter))
                MpUI.LabelOutlined(labelRect,
                    MpTranslate.Fallback("MpPingCluster_Label",
                        $"{group.Count} markers", group.Count),
                    new Color(1f, 1f, 1f, 1f),
                    new Color(0f, 0f, 0f, 0.95f));
        }
    }
}
