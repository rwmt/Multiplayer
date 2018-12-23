using Harmony;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Targeter), nameof(Targeter.TargeterOnGUI))]
    [HotSwappable]
    static class DrawPlayerCursors
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null || !MultiplayerMod.settings.showCursors || TickPatch.skipTo >= 0) return;

            var curMap = Find.CurrentMap.Index;

            foreach (var player in Multiplayer.session.players)
            {
                if (player.username == Multiplayer.username) continue;
                if (player.map != curMap) continue;

                GUI.color = new Color(1, 1, 1, 0.5f);
                var pos = Vector3.Lerp(player.lastCursor, player.cursor, (float)(Multiplayer.Clock.ElapsedMillisDouble() - player.updatedAt) / 50f).MapToUIPosition();

                var icon = Multiplayer.icons.ElementAtOrDefault(player.cursorIcon);
                var drawIcon = icon ?? CustomCursor.CursorTex;
                var iconRect = new Rect(pos, new Vector2(24f * drawIcon.width / drawIcon.height, 24f));

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(pos, new Vector2(100, 30)).CenterOn(iconRect).Down(20f).Left(icon != null ? 0f : 5f), player.username);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;

                if (icon != null && Multiplayer.iconInfos[player.cursorIcon].hasStuff)
                    GUI.color = new Color(0.5f, 0.4f, 0.26f, 0.5f);

                GUI.DrawTexture(iconRect, drawIcon);

                GUI.color = Color.white;
            }
        }
    }
}
