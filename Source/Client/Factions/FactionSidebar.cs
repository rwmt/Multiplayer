using System.Collections.Generic;
using System.Linq;
using System.Text;
using Multiplayer.API;
using Multiplayer.Client.Factions;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public static class FactionSidebar
{
    private static ScenarioDef chosenScenario = ScenarioDefOf.Crashlanded; // null means current game's scenario
    private static string newFactionName;
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
        Layouter.BeginHorizontal();
        // Label($"Scenario: {chosenScenario?.label ?? Find.Scenario.name}");
        //
        // if (Mouse.IsOver(Layouter.LastRect()))
        //     Widgets.DrawAltRect(Layouter.LastRect());
        //
        // if (Widgets.ButtonInvisible(Layouter.LastRect()))
        //     OpenScenarioChooser();

        Layouter.EndHorizontal();

        newFactionName = Widgets.TextField(Layouter.Rect(150, 24), newFactionName);

        if (Button("Settle new faction", 130))
        {
            var tileError = new StringBuilder();

            // todo check faction name not exists
            if (newFactionName.NullOrEmpty())
                Messages.Message("The faction name can't be empty.", MessageTypeDefOf.RejectInput, historical: false);
            else if (Find.WorldInterface.SelectedTile < 0)
                Messages.Message("MustSelectStartingSite".TranslateWithBackup("MustSelectLandingSite"), MessageTypeDefOf.RejectInput, historical: false);
            else if (!TileFinder.IsValidTileForNewSettlement(Find.WorldInterface.SelectedTile, tileError))
                Messages.Message(tileError.ToString(), MessageTypeDefOf.RejectInput, historical: false);
            else
            {
                PreparePawns();

                var pages = new List<Page>();
                Page_ChooseIdeo_Multifaction chooseIdeoPage = null;

                if (ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode)
                    pages.Add(chooseIdeoPage = new Page_ChooseIdeo_Multifaction());

                pages.Add(new Page_ConfigureStartingPawns
                {
                    nextAct = () =>
                    {
                        DoCreateFaction(
                            new ChooseIdeoInfo(
                                chooseIdeoPage?.pageChooseIdeo.selectedIdeo,
                                chooseIdeoPage?.pageChooseIdeo.selectedStructure,
                                chooseIdeoPage?.pageChooseIdeo.selectedStyles
                            )
                        );
                    }
                });

                var page = PageUtility.StitchedPages(pages);
                Find.WindowStack.Add(page);
            }
        }
    }

    private static void OpenScenarioChooser()
    {
        Find.WindowStack.Add(new FloatMenu(
            DefDatabase<ScenarioDef>.AllDefs.
                Where(def => def.scenario.GetHashCode() != Find.Scenario.GetHashCode()).
                Except(ScenarioDefOf.Tutorial).
                Prepend(null).
                Select(s =>
                {
                    return new FloatMenuOption(s?.label ?? Find.Scenario.name, () =>
                    {
                        chosenScenario = s;
                    });
                }).
                ToList()));
    }

    private static void PreparePawns()
    {
        var scenario = chosenScenario?.scenario ?? Current.Game.Scenario;
        var prevState = Current.programStateInt;

        Current.programStateInt = ProgramState.Entry; // Set ProgramState.Entry so that InInterface is false

        Current.Game.InitData = new GameInitData
        {
            startingPawnCount = 3,
            startingPawnKind = scenario.playerFaction.factionDef.basicMemberKind,
            gameToLoad = "dummy" // Prevent special calculation path in GenTicks.TicksAbs
        };

        try
        {
            // Create starting pawns
            new ScenPart_ConfigPage_ConfigureStartingPawns { pawnCount = Current.Game.InitData.startingPawnCount }
                .GenerateStartingPawns();
        }
        finally
        {
            Current.programStateInt = prevState;
        }
    }

    private static void DoCreateFaction(ChooseIdeoInfo chooseIdeoInfo)
    {
        int playerId = Multiplayer.session.playerId;
        var prevState = Current.programStateInt;
        Current.programStateInt = ProgramState.Playing; // This is to force a sync

        try
        {
            foreach (var p in Current.Game.InitData.startingAndOptionalPawns)
                FactionCreator.SendPawn(
                    playerId,
                    p
                );

            FactionCreator.CreateFaction(
                playerId,
                newFactionName,
                Find.WorldInterface.SelectedTile,
                chosenScenario,
                chooseIdeoInfo
            );
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

            using (MpStyle.Set(TextAnchor.MiddleCenter))
                Label(playerFaction.Name, true);

            Layouter.FlexibleWidth();
            if (Button("Join", 70))
            {
                var factionHome = Find.Maps.FirstOrDefault(m => m.ParentFaction == playerFaction);
                if (factionHome != null)
                    Current.Game.CurrentMap = factionHome;

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

public record ChooseIdeoInfo(
    IdeoPresetDef SelectedIdeo,
    MemeDef SelectedStructure,
    List<StyleCategoryDef> SelectedStyles
) : ISyncSimple;
