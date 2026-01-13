using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client
{

    [HarmonyPatch(typeof(ExpandableWorldObjectsUtility), nameof(ExpandableWorldObjectsUtility.ExpandableWorldObjectsOnGUI))]
    static class DrawPingPlanet
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null || TickPatch.Simulating) return;

            foreach (var ping in Multiplayer.session.locationPings.pings)
            {
                if (ping.mapId != -1) continue;
                if (ping.PlayerInfo is not { } player) continue;
                if (ping.planetTile.Layer == null) continue;

                var layer = Find.WorldSelector.SelectedLayer;
                // Only display pings on the current layer or (if enabled) on layers we can zoom to.
                if (Multiplayer.settings.enableCrossPlanetLayerPings)
                {
                    // We can either start with the ping layer, and keep zooming out,
                    // or start with the current player layer, and keep zooming in.
                    // Or both. This implementation tries to zoom in from the current player's layer.

                    // Infinite loop prevention.
                    for (var i = 0; i < 25; i++)
                    {
                        // Either can't zoom in more, or we found our target
                        if (layer == null || layer == ping.planetTile.Layer)
                            break;

                        layer = layer.zoomInToLayer;
                    }
                }
                if (ping.planetTile.Layer != layer) continue;

                var tileCenter = GenWorldUI.WorldToUIPosition(Find.WorldGrid.GetTileCenter(ping.planetTile));
                const float size = 30f;

                ping.DrawAt(tileCenter, player.color, size);
            }
        }
    }
}
