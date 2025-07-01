using System.Collections.Generic;
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

public static class FactionSidebar
{
    private static ScenarioDef chosenScenario = ScenarioDefOf.Crashlanded;
    private static string factionNameTextField;
    private static bool setupNextMapFromTickZero;

    private static Vector2 scroll;
    private static Rect factionBarRect;
    private static Vector2 factionChooserScrollBarPos;
    private static float buttonHeight = 25f;

    public static void DrawFactionSidebar(Rect rect)
    {
        using var _ = MpStyle.Set(GameFont.Small);

        if (!Layouter.BeginArea(rect, 0))
            return;

        factionBarRect = rect;

        DrawFactionCreator();

        DrawFactionChooser();

        Layouter.EndArea();
    }

    private static void DrawFactionCreator()
    {
        Layouter.BeginVertical(6, false);

        DrawHeadline("Create Faction");

        DrawFactionNameTextfield();

        DrawScenarioChooser();

        DrawDefaultMapTimeCheckbox();

        DrawSettleButton();
    }

    private static void DrawSettleButton()
    {
        Layouter.BeginHorizontal();

        Layouter.FlexibleWidth();

        if (Button("Settle faction", 130, buttonHeight) && FactionCreationCanBeStarted())
        {
            OpenConfigurationPages();
        }

        Layouter.EndHorizontal();

        Layouter.EndVertical();
    }

    private static void DrawDefaultMapTimeCheckbox()
    {
        float spacingLabelCheckbox = 10;
        bool asyncTimeDisabled = !Multiplayer.GameComp.asyncTime;
        StringBuilder sb = new StringBuilder();
        Rect defaultMapTimeRect;

        Layouter.BeginHorizontal(spacingLabelCheckbox);

        Label("Default map time: ");

        defaultMapTimeRect = Layouter.LastRect();

        MpUI.Checkbox(
            (Layouter.LastRect().xMax / 2.0f) + (spacingLabelCheckbox / 2.0f),
            Layouter.LastRect().y,
            ref setupNextMapFromTickZero,
            asyncTimeDisabled,
            defaultMapTimeRect.height - 2);

        Layouter.EndHorizontal();

        if (asyncTimeDisabled)
        {
            sb.Append("Only changeable with async setting active in host menu\n\n");
        }

        sb.AppendLine("This checkbox controls the starting time on the generated map\n");
        sb.AppendLine("Checked: Uses the default start time, similar to singleplayer maps\n");
        sb.AppendLine("Unchecked: Uses the current world time");

        TooltipHandler.TipRegion(defaultMapTimeRect, sb.ToString());
    }

    private static void OpenConfigurationPages()
    {
        var gameConfigurationPages = new List<Page>();
        Page_ChooseIdeo_Multifaction ideologyConfigPage = null;
        Page_ConfigureStartingPawns pawnConfigPage = null;

        InitializeDataForGameConfigurationPages();

        ideologyConfigPage = TryGetIdeologyConfigurationPage();

        pawnConfigPage = new Page_ConfigureStartingPawns_Multifaction()
        {
            nextAct = () =>
            {
                IdeologyData ideologyData = ideologyConfigPage?.GetIdeologyData() ?? new IdeologyData();
                DoCreateFaction(ideologyData, true);
            }
        };

        if (ideologyConfigPage != null)
            gameConfigurationPages.Add(ideologyConfigPage);

        gameConfigurationPages.Add(pawnConfigPage);

        Find.WindowStack.Add(PageUtility.StitchedPages(gameConfigurationPages));
    }

    private static void DoCreateFaction(IdeologyData chooseIdeoInfo, bool generateMap)
    {
        int playerId = Multiplayer.session.playerId;
        var prevState = Current.programStateInt;
        List<Pawn> startingPawns = new List<Pawn>();
        FactionCreationData factionCreationDto = new FactionCreationData();

        // OldComment: This is to force a sync
        // TODO: Make this clearer without a needed comment
        // LookUp Multiplayer.InInterface && Multiplayer.ShouldSync

        Current.programStateInt = ProgramState.Playing;

        try
        {
            if (Current.Game.InitData?.startingAndOptionalPawns is { } pawns)
                for (int i = 0; i < Find.GameInitData.startingPawnCount; i++)
                {
                    FactionCreator.SendPawn(playerId, pawns[i]);
                    startingPawns.Add(pawns[i]);
                }

            factionCreationDto.factionName = factionNameTextField;
            factionCreationDto.startingTile = Find.WorldInterface.SelectedTile;
            factionCreationDto.scenarioDef = chosenScenario;
            factionCreationDto.chooseIdeoInfo = chooseIdeoInfo;
            factionCreationDto.generateMap = generateMap;
            factionCreationDto.startingPossessions = GetStartingPossessions(startingPawns);
            factionCreationDto.setupNextMapFromTickZero = setupNextMapFromTickZero;            

            FactionCreator.CreateFaction(playerId, factionCreationDto);
        }
        finally
        {
            Current.programStateInt = prevState;
        }
    }

    private static void DrawHeadline(string headline)
    {
        using (MpStyle.Set(TextAnchor.MiddleLeft))
        using (MpStyle.Set(GameFont.Medium))
            Label(headline);
    }

    private static void DrawFactionChooser()
    {
        int i = 0;
        float scrollSpacing = 4;
        var allPlayerFactions = Find.FactionManager.AllFactions.
            Where(f => f.def == FactionDefOf.PlayerColony || f.def == FactionDefOf.PlayerTribe).
            Where(f => f.name != "Spectator").
            Where(f => !f.temporary);

        Layouter.BeginVertical(2);

        DrawHeadline("Join Faction");

        Layouter.BeginScroll(ref factionChooserScrollBarPos, scrollSpacing);
        Layouter.Rect(5, scrollSpacing / 2.0f);

        foreach (var playerFaction in allPlayerFactions)
        {
            Layouter.BeginHorizontal();

            if (i % 2 == 0)
            {
                Rect rect = Layouter.GroupRect();
                Widgets.DrawAltRect(new Rect(rect.x, rect.y - scrollSpacing / 2.0f, rect.width, rect.height + scrollSpacing));
            }

            using (MpStyle.Set(TextAnchor.MiddleLeft))
                Label($" {playerFaction.Name}", true);

            if (Button("Join", 70, buttonHeight))
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

        Layouter.Rect(5, scrollSpacing / 2.0f);
        Layouter.EndScroll();
        Layouter.EndVertical();
    }

    private static void DrawFactionNameTextfield()
    {
        float spacing = 10;

        Layouter.BeginHorizontal(spacing);

        using (MpStyle.Set(TextAnchor.MiddleLeft))
            Label("Faction name: ", true);

        factionNameTextField = Widgets.TextField(Layouter.Rect((factionBarRect.width / 2.0f) - (spacing / 2.0f), 20), factionNameTextField);

        Layouter.EndHorizontal();
    }

    private static bool FactionCreationCanBeStarted()
    {
        var tileError = new StringBuilder();

        if (factionNameTextField.NullOrEmpty())
        {
            ShowRejectInputMessage("The faction name can't be empty.");
            return false;
        }
        else if (Find.FactionManager.AllFactions.Any(f => f.Name == factionNameTextField))
        {
            ShowRejectInputMessage("The faction name is already taken");
            return false;
        }
        else if (MpVersion.IsDebug && Event.current.button == 1)
        {
            DrawDebugFactionCreationFloatMenu();
            return false;
        }
        else if (Find.WorldInterface.SelectedTile < 0)
        {
            ShowRejectInputMessage("MustSelectStartingSite".TranslateWithBackup("MustSelectLandingSite"));
            return false;
        }
        else if (!TileFinder.IsValidTileForNewSettlement(Find.WorldInterface.SelectedTile, tileError))
        {
            ShowRejectInputMessage(tileError.ToString());
            return false;
        }

        return true;
    }

    private static void ShowRejectInputMessage(string message)
    {
        Messages.Message(message, MessageTypeDefOf.RejectInput, historical: false);
    }

    private static void DrawDebugFactionCreationFloatMenu()
    {
        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>()
                {
                    new(
                        "Dev: create faction (no base)", () => DoCreateFaction(new IdeologyData(null, null, null), false)
                    )
                }));
    }

    private static Page_ChooseIdeo_Multifaction TryGetIdeologyConfigurationPage()
    {
        Page_ChooseIdeo_Multifaction chooseIdeoPage = null;

        if (ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode)
            chooseIdeoPage = new Page_ChooseIdeo_Multifaction();

        return chooseIdeoPage;
    }

    private static void InitializeDataForGameConfigurationPages()
    {
        Current.Game.Scenario = chosenScenario.scenario;

        Current.Game.InitData = new GameInitData
        {
            startedFromEntry = true,
            gameToLoad = FactionCreator.preventSpecialCalculationPathInGenTicksTicksAbs
        };
    }

    private static void DrawScenarioChooser()
    {
        Layouter.BeginHorizontal();

        using (MpStyle.Set(TextAnchor.MiddleLeft))
        {
            Label($"Scenario: ");
            Label(chosenScenario?.label ?? Find.Scenario.name);
        }

        Layouter.EndHorizontal();

        if (ModsConfig.AnomalyActive)
        {
            chosenScenario = ScenarioDefOf.Crashlanded;
            TooltipHandler.TipRegion(Layouter.LastRect(),"Choosing a scenario is not available for Anomaly");
            return;
        }

        if (Mouse.IsOver(Layouter.LastRect()))
            Widgets.DrawAltRect(Layouter.LastRect());

        if (Widgets.ButtonInvisible(Layouter.LastRect()))
            OpenScenarioChooser();
    }

    private static void OpenScenarioChooser()
    {
        Find.WindowStack.Add(new FloatMenu(
            DefDatabase<ScenarioDef>.AllDefs.
                Except(ScenarioDefOf.Tutorial).
                Select(s =>
                {
                    return new FloatMenuOption(s.label, () =>
                    {
                        chosenScenario = s;                        
                    });
                }).
                ToList()));
    }

    private static List<ThingDefCount> GetStartingPossessions(List<Pawn> startingPawns)
    {
        Dictionary<Pawn, List<ThingDefCount>> allPossessions = Find.GameInitData.startingPossessions;
        List<ThingDefCount> startingPossessions = new List<ThingDefCount>();

        foreach(Pawn pawn in startingPawns)
        {
            startingPossessions.AddRange(allPossessions[pawn]);
        }

        return startingPossessions;
    }

    private static void Label(string text, bool inheritHeight = false)
    {
        GUI.Label(inheritHeight ? Layouter.FlexibleWidth() : Layouter.ContentRect(text), text, Text.CurFontStyle);
    }

    private static bool Button(string text, float width, float height = 35f)
    {
        return Widgets.ButtonText(Layouter.Rect(width, height), text);
    }
}
