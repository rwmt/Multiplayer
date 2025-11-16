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
                // Only display pings on the current layer
                if (ping.planetTile.Layer != Find.WorldSelector.SelectedLayer) continue;

                var tileCenter = GenWorldUI.WorldToUIPosition(Find.WorldGrid.GetTileCenter(ping.planetTile));
                const float size = 30f;

                ping.DrawAt(tileCenter, player.color, size);
            }
        }
    }
}
