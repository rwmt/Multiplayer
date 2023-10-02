using System.Linq;
using System.Text;
using Multiplayer.Client.Factions;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public class FactionSidebar
{
    private static string scenario = "Crashlanded";
    private static string factionName;
    private static FactionRelationKind hostility = FactionRelationKind.Neutral;
    private static Vector2 scroll;

    public static void DrawFactionSidebar(Rect rect)
    {
        using var _ = MpStyle.Set(GameFont.Small);

        if (!Layouter.BeginArea(rect))
            return;

        Layouter.BeginScroll(ref scroll, spacing: 0f);

        using (MpStyle.Set(TextAnchor.MiddleLeft))
        using (MpStyle.Set(GameFont.Medium))
            Label("Create faction");

        DrawFactionCreator();

        Layouter.Rect(0, 20);

        using (MpStyle.Set(Color.gray))
            Widgets.DrawLineHorizontal(Layouter.LastRect().x, Layouter.LastRect().center.y, rect.width);

        using (MpStyle.Set(TextAnchor.MiddleLeft))
        using (MpStyle.Set(GameFont.Medium))
            Label("Join faction");

        DrawFactionChooser();

        Layouter.EndScroll();
        Layouter.EndArea();
    }

    private static void DrawFactionCreator()
    {
        factionName = Widgets.TextField(Layouter.Rect(150, 24), factionName);

        if (Button("Settle new faction", 130))
        {
            var tileError = new StringBuilder();

            // todo check faction name not exists
            if (factionName.NullOrEmpty())
                Messages.Message("The faction name can't be empty.", MessageTypeDefOf.RejectInput, historical: false);
            else if (Find.WorldInterface.SelectedTile < 0)
                Messages.Message("MustSelectStartingSite".TranslateWithBackup("MustSelectLandingSite"), MessageTypeDefOf.RejectInput, historical: false);
            else if (!TileFinder.IsValidTileForNewSettlement(Find.WorldInterface.SelectedTile, tileError))
                Messages.Message(tileError.ToString(), MessageTypeDefOf.RejectInput, historical: false);
            else
            {
                PreparePawns();

                Find.WindowStack.Add(new Page_ConfigureStartingPawns
                {
                    nextAct = DoCreateFaction
                });
            }
        }
    }

    private static void PreparePawns()
    {
        var prevState = Current.programStateInt;
        Current.programStateInt = ProgramState.Entry; // Set ProgramState.Entry so that InInterface is false

        try
        {
            FactionCreator.SetInitialInitData();

            // Create starting pawns
            new ScenPart_ConfigPage_ConfigureStartingPawns { pawnCount = Current.Game.InitData.startingPawnCount }
                .GenerateStartingPawns();
        }
        finally
        {
            Current.programStateInt = prevState;
        }
    }

    private static void DoCreateFaction()
    {
        int sessionId = Multiplayer.session.playerId;
        var prevState = Current.programStateInt;
        Current.programStateInt = ProgramState.Playing; // This is to force a sync

        try
        {
            foreach (var p in Current.Game.InitData.startingAndOptionalPawns)
                FactionCreator.SendPawn(
                    sessionId,
                    p
                );

            FactionCreator.CreateFaction(sessionId, factionName, Find.WorldInterface.SelectedTile,
                scenario, hostility);
        }
        finally
        {
            Current.programStateInt = prevState;
        }
    }

    private static void DrawFactionChooser()
    {
        int i = 0;

        foreach (var playerFaction in Find.FactionManager.AllFactions.Where(f => f.def == FactionDefOf.PlayerColony))
        {
            if (playerFaction.Name == "Spectator") continue;

            Layouter.BeginHorizontal();
            if (i % 2 == 0)
                Widgets.DrawAltRect(Layouter.GroupRect());

            if (Mouse.IsOver(Layouter.GroupRect()))
            {
                // todo this doesn't exactly work
                foreach (var settlement in Find.WorldObjects.Settlements)
                    if (settlement.Faction == playerFaction)
                        Graphics.DrawMesh(MeshPool.plane20,
                            Find.WorldGrid.GetTileCenter(settlement.Tile),
                            Quaternion.identity,
                            GenDraw.ArrowMatWhite,
                            0);

                Widgets.DrawRectFast(Layouter.GroupRect(), new Color(0.2f, 0.2f, 0.2f));
            }

            using (MpStyle.Set(TextAnchor.MiddleCenter))
                Label(playerFaction.Name, true);

            Layouter.FlexibleWidth();
            if (Button("Join", 70))
            {
                Current.Game.CurrentMap = Find.Maps.First(m => m.ParentFaction == playerFaction);

                // todo setting faction of self
                Multiplayer.Client.Send(
                    Packets.Client_SetFaction,
                    Multiplayer.session.playerId,
                    playerFaction.loadID
                );
            }
            Layouter.EndHorizontal();
            i++;
        }
    }

    public static void Label(string text, bool inheritHeight = false)
    {
        GUI.Label(inheritHeight ? Layouter.FlexibleWidth() : Layouter.ContentRect(text), text, Text.CurFontStyle);
    }

    public static bool Button(string text, float width, float height = 35f)
    {
        return Widgets.ButtonText(Layouter.Rect(width, height), text);
    }
}
