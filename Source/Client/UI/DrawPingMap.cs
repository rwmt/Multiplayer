using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_BeforeMainTabs))]
    static class DrawPingMap
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null || Multiplayer.arbiterInstance || TickPatch.Simulating) return;
            if (Find.CurrentMap == null) return;
            if (Event.current.type != EventType.Repaint) return;
            if (WorldRendererUtility.WorldSelected) return;

            var size = LocationPings.OnScreenPingSize;
            var loc = Multiplayer.session?.locationPings;
            if (loc == null) return;

            // Markers under pings so a fresh signal isn't hidden behind a static annotation.
            var mapId = Find.CurrentMap.uniqueID;
            foreach (var marker in loc.Markers)
            {
                if (marker.mapId != mapId) continue;
                marker.DrawAt(marker.mapLoc.MapToUIPosition(), size);
            }

            foreach (var ping in loc.pings)
            {
                if (ping.mapId != mapId) continue;
                if (ping.PlayerInfo == null) continue;
                ping.DrawAt(ping.mapLoc.MapToUIPosition(), size);
            }
        }
    }
}
