using System;
using System.Linq;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Factions;

public class FactionsWindow : Window
{
    public override Vector2 InitialSize { get; } = new(700, 600);

    private static Vector2 scroll;

    public FactionsWindow()
    {
        doCloseX = true;
    }

    public override void DoWindowContents(Rect inRect)
    {
        var group = DragAndDropWidget.NewGroup();

        Layouter.BeginArea(inRect);
        Layouter.BeginScroll(ref scroll, spacing: 0f);

        var factions = Find.FactionManager.AllFactions.Where(f => f.IsPlayer).ToList();

        void DrawFactionInLastRect(Faction faction)
        {
            DragAndDropWidget.DropArea(group, Layouter.LastRect(), playerId =>
            {
                Multiplayer.Client.Send(Packets.Client_SetFaction, (int)playerId, faction.loadID);
            }, null);

            Layouter.BeginVerticalInLastRect(spacing: 1f);

            Layouter.BeginHorizontal();
            {
                Layouter.BeginVertical(spacing: 0f, false);
                Layouter.Rect(0f, 5f);

                if (Layouter.Button(">", 20f, 20f))
                    Multiplayer.Client.Send(Packets.Client_SetFaction, Multiplayer.session.playerId, faction.loadID);

                TooltipHandler.TipRegion(Layouter.LastRect(), "Switch faction");

                Layouter.EndVertical();

                using (MpStyle.Set(GameFont.Medium))
                    Layouter.Label(faction.Name);
            }
            Layouter.EndHorizontal();

            foreach (var p in Multiplayer.session.players)
            {
                if (p.factionId != faction.loadID)
                    continue;

                var rect = Layouter.ContentRect(p.username);

                if (Multiplayer.LocalServer != null && DragAndDropWidget.Draggable(group, rect, p.id))
                    Widgets.Label(rect with { position = Event.current.mousePosition }, p.username);
                else
                    Widgets.Label(rect, p.username);
            }

            Layouter.EndVertical();
        }

        for (int i = 0; i < factions.Count; i++)
        {
            Layouter.BeginHorizontal();

            float height = 23 * Multiplayer.session.players.Count(p => p.factionId == factions[i].loadID) + 30f;
            if (i + 1 < factions.Count)
                height = Math.Max(23 * Multiplayer.session.players.Count(p => p.factionId == factions[i+1].loadID) + 30f, height);
            height = Math.Max(200, height);

            Layouter.FixedHeight(height);
            DrawFactionInLastRect(factions[i]);
            i++;

            Layouter.FixedHeight(height);
            if (i < factions.Count) DrawFactionInLastRect(factions[i]);

            Layouter.EndHorizontal();
        }

        Layouter.EndScroll();
        Layouter.EndArea();
    }
}
