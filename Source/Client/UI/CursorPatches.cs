using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Targeter), nameof(Targeter.TargeterOnGUI))]
    static class DrawPlayerCursors
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null || !Multiplayer.settings.showCursors || TickPatch.Simulating) return;

            var curMap = Find.CurrentMap.Index;

            foreach (var player in Multiplayer.session.players)
            {
                if (player.username == Multiplayer.username) continue;
                if (player.map != curMap) continue;
                if (player.factionId != Multiplayer.RealPlayerFaction.loadID) continue;

                if (Multiplayer.settings.transparentPlayerCursors)
                    GUI.color = player.color * new Color(1, 1, 1, 0.5f);
                else
                    GUI.color = player.color * new Color(1, 1, 1, 1);

                var pos = Vector3.Lerp(
                    player.lastCursor,
                    player.cursor,
                    (float)(Multiplayer.clock.ElapsedMillisDouble() - player.updatedAt) / 50f
                ).MapToUIPosition();

                var icon = MultiplayerData.icons.ElementAtOrDefault(player.cursorIcon);
                var drawIcon = icon ?? CustomCursor.CursorTex;
                var iconRect = new Rect(pos, new Vector2(24f * drawIcon.width / drawIcon.height, 24f));

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(pos, new Vector2(100, 30)).CenterOn(iconRect).Down(20f).Left(icon != null ? 0f : 5f), player.username);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;

                if (icon != null && MultiplayerData.iconInfos[player.cursorIcon].hasStuff)
                    GUI.color = new Color(0.5f, 0.4f, 0.26f, 0.5f); // Stuff color for wood

                GUI.DrawTexture(iconRect, drawIcon);

                if (player.dragStart != PlayerInfo.Invalid)
                {
                    GUI.color = player.color * new Color(1, 1, 1, 0.2f);
                    Widgets.DrawBox(new Rect() { min = player.dragStart.MapToUIPosition(), max = pos }, 2);
                }

                GUI.color = Color.white;
            }
        }
    }

    [HarmonyPatch(typeof(SelectionDrawer), nameof(SelectionDrawer.DrawSelectionOverlays))]
    [StaticConstructorOnStartup]
    static class SelectionBoxPatch
    {
        static HashSet<int> drawnThisUpdate = new HashSet<int>();
        static Dictionary<object, float> selTimes = new Dictionary<object, float>();

        static void Postfix()
        {
            if (Multiplayer.Client == null || TickPatch.Simulating) return;

            foreach (var t in Find.Selector.SelectedObjects.OfType<Thing>())
                drawnThisUpdate.Add(t.thingIDNumber);

            var tempTimes = SelectionDrawer.selectTimes;
            try
            {
                SelectionDrawer.selectTimes = selTimes;

                foreach (var player in Multiplayer.session.players)
                {
                    if (player.factionId != Multiplayer.RealPlayerFaction.loadID) continue;

                    foreach (var sel in player.selectedThings)
                    {
                        if (!drawnThisUpdate.Add(sel.Key)) continue;
                        if (!ThingsById.thingsById.TryGetValue(sel.Key, out Thing thing)) continue;
                        if (thing.MapHeld != Find.CurrentMap) continue;

                        selTimes[thing] = sel.Value;
                        SelectionDrawer.DrawSelectionBracketFor(thing, player.selectionBracketMaterial);
                        selTimes.Clear();
                    }
                }
            }
            finally
            {
                SelectionDrawer.selectTimes = tempTimes;
            }

            drawnThisUpdate.Clear();
        }
    }

    [HarmonyPatch(typeof(InspectPaneFiller), nameof(InspectPaneFiller.DrawInspectStringFor))]
    static class DrawInspectPaneStringMarker
    {
        public static ISelectable drawingFor;

        static void Prefix(ISelectable sel) => drawingFor = sel;
        static void Postfix() => drawingFor = null;
    }

    [HarmonyPatch(typeof(InspectPaneFiller), nameof(InspectPaneFiller.DrawInspectString))]
    static class DrawInspectStringPatch
    {
        static void Prefix(ref string str)
        {
            if (Multiplayer.Client == null) return;
            if (!(DrawInspectPaneStringMarker.drawingFor is Thing thing)) return;

            List<string> players = new List<string>();

            foreach (var player in Multiplayer.session.players)
                if (player.selectedThings.ContainsKey(thing.thingIDNumber))
                    players.Add(player.username);

            if (players.Count > 0)
                str += $"\nSelected by: {players.Join()}";
        }
    }
}
