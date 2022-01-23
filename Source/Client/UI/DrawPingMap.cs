using System;
using HarmonyLib;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HotSwappable]
    [HarmonyPatch(typeof(BeautyDrawer), nameof(BeautyDrawer.BeautyDrawerOnGUI))]
    static class DrawPingMap
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null || TickPatch.Simulating) return;

            var size = Math.Min(UI.CurUICellSize() * 4, 32f);

            foreach (var ping in Multiplayer.session.cursorAndPing.pings)
            {
                if (ping.mapId != Find.CurrentMap.uniqueID) continue;
                if (Multiplayer.session.GetPlayerInfo(ping.player) is not { } player) continue;

                ping.DrawAt(ping.mapLoc.MapToUIPosition(), player.color, size);
            }
        }
    }
}
