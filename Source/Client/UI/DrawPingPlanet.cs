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

            foreach (var ping in Multiplayer.session.cursorAndPing.pings)
            {
                if (ping.mapId != -1) continue;
                if (Multiplayer.session.GetPlayerInfo(ping.player) is not { } player) continue;

                var tileCenter = GenWorldUI.WorldToUIPosition(Find.WorldGrid.GetTileCenter(ping.planetTile));
                const float size = 30f;

                ping.DrawAt(tileCenter, player.color, size);
            }
        }
    }
}
