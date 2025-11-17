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

                Log.Message("planetlayer: " + ping.planetTile.Layer.def.defName);

                var tileCenter = GenWorldUI.WorldToUIPosition(Find.WorldGrid.GetTileCenter(ping.planetTile));
                const float size = 30f;

                //Only draw pings for layers that are on the layer we're at or lower
                if (PlanetLayer.Selected.layerId >= ping.planetTile.layerId)
                {

                    ping.DrawAt(tileCenter, player.color, size);
                }
            }
        }
    }
}
