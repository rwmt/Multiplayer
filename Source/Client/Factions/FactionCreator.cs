using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Multiplayer.API;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Factions;

public static class FactionCreator
{
    private static Dictionary<int, List<Pawn>> pawnStore = new();
    public static bool generatingMap;
    public const string preventSpecialCalculationPathInGenTicksTicksAbs = "dummy";

    public static void ClearData()
    {
        pawnStore.Clear();
    }

    [SyncMethod(exposeParameters = new[] { 1 })]
    public static void SendPawn(int playerId, Pawn p)
    {
        pawnStore.GetOrAddNew(playerId).Add(p);
    }

    [SyncMethod]
    public static void CreateFaction(int playerId, FactionCreationData creationData)
    {
        var executingOnlyOnIssuer = TickPatch.currentExecutingCmdIssuedBySelf;

        LongEventHandler.QueueLongEvent(() =>
        {
            var scenario = creationData.scenarioDef?.scenario ?? Current.Game.Scenario;
            Map newMap = null;

            PrepareGameInitData(playerId, scenario, executingOnlyOnIssuer, creationData.startingPossessions);

            var newFaction = NewFactionWithIdeo(
                creationData.factionName,
                creationData.factionColor,
                scenario.playerFaction.factionDef,
                creationData.chooseIdeoInfo
            );

            if (creationData.generateMap)
                using (MpScope.PushFaction(newFaction))
                {
                    foreach (var pawn in StartingPawnUtility.StartingAndOptionalPawns)
                        pawn.ideo.SetIdeo(newFaction.ideos.PrimaryIdeo);

                    newMap = GenerateNewMap(creationData.startingTile, scenario, creationData.setupNextMapFromTickZero);
                }

            foreach (Map map in Find.Maps)
                foreach (var f in Find.FactionManager.AllFactions.Where(f => f.IsPlayer))
                    map.attackTargetsCache.Notify_FactionHostilityChanged(f, newFaction);

            using (MpScope.PushFaction(newFaction))
                InitNewGame(scenario);

            if (executingOnlyOnIssuer)
            {
                ClearPortraitCacheFromPawnConfigPage();

                Current.Game.CurrentMap = newMap;

                Multiplayer.game.ChangeRealPlayerFaction(newFaction);

                InitLocalVisuals(scenario, newMap);

                // todo setting faction of self
                Multiplayer.Client.Send(
                    Packets.Client_SetFaction,
                    Multiplayer.session.playerId,
                    newFaction.loadID
                );
            }
        }, "GeneratingMap", doAsynchronously: true, GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);
    }

    private static void ClearPortraitCacheFromPawnConfigPage()
    {
        PortraitsCache.Clear();
    }

    private static void InitLocalVisuals(Scenario scenario, Map generatedMap)
    {
        // TODO #587: There may be other logical ScenParts (e.g., ScenPart_StartingResearch) that
        // need to be executed by all players in the game.
        var onlyVisualScenParts = new HashSet<Type>() { typeof(ScenPart_GameStartDialog) };

        PostGameStart(scenario, onlyVisualScenParts);

        CameraJumper.TryJump(MapGenerator.playerStartSpotInt, generatedMap);
    }

    private static Map GenerateNewMap(PlanetTile tile, Scenario scenario, bool setupNextMapFromTickZero)
    {
        // This has to be null, otherwise, during map generation, Faction.OfPlayer returns it which breaks FactionContext
        Find.GameInitData.playerFaction = null;
        Find.GameInitData.PrepForMapGen();

        // ScenPart_PlayerFaction --> PreMapGenerate 

        Settlement settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
        settlement.Tile = tile;
        settlement.SetFaction(Faction.OfPlayer);
        Find.WorldObjects.Add(settlement);

        // ^^^^ Duplicate Code here ^^^^
        Scenario prevScenario = Find.Scenario;
        PlanetTile prevStartingTile = Find.GameInfo.startingTile;

        Current.Game.Scenario = scenario;
        Find.GameInfo.startingTile = tile;
        Find.GameInitData.startingTile = tile;

        MapSetup.SetupNextMapFromTickZero = setupNextMapFromTickZero;

        generatingMap = true;

        try
        {
            Map map = GetOrGenerateMapUtility.GetOrGenerateMap(
                tile,
                new IntVec3(250, 1, 250),
                null
            );

            return map;
        }
        finally
        {
            generatingMap = false;
            Current.Game.Scenario = prevScenario;
            Find.GameInfo.startingTile = prevStartingTile;
            Find.GameInitData.startingTile = prevStartingTile;
        }
    }

    private static void InitNewGame(Scenario scenario)
    {
        PawnUtility.GiveAllStartingPlayerPawnsThought(ThoughtDefOf.NewColonyOptimism);

        ResearchUtility.ApplyPlayerStartingResearch();

        // TODO #587: There may be other only visual ScenParts (e.g., ScenPart_GameStartDialog) that only need to
        // be executed by the local client (the issuer/creator of the new faction).
        PostGameStart(scenario, new HashSet<Type>() { typeof(ScenPart_StartingResearch) });

        // Initialize anomaly. Since the Anomaly comp is currently shared by all players,
        // we need to ensure that any new factions have access to anomaly research if
        // anomaly content was started.
        Find.ResearchManager.Notify_MonolithLevelChanged(Find.Anomaly.Level);
    }

    private static void PostGameStart(Scenario scenario, IEnumerable<Type> scenPartTypesToInvoke)
    {
        foreach (ScenPart part in scenario.AllParts)
        {
            if (scenPartTypesToInvoke.Contains(part.GetType()))
            {
                part.PostGameStart();
            }
        }
    }

    private static void PrepareGameInitData(int sessionId, Scenario scenario, bool self, List<ThingDefCount> startingPossessions)
    {
        if (!self)
        {
            Current.Game.InitData = new GameInitData()
            {
                startingPawnCount = GetStartingPawnsCountForScenario(scenario),
                gameToLoad = preventSpecialCalculationPathInGenTicksTicksAbs
            };
        }

        if (pawnStore.TryGetValue(sessionId, out var pawns))
        {
            Pawn firstPawn = pawns.First();

            GameInitData gameInitData = Current.Game.InitData;
            gameInitData.startingAndOptionalPawns = pawns;
            gameInitData.startingPossessions = new Dictionary<Pawn, List<ThingDefCount>>();

            foreach (var pawn in pawns)
            {
                gameInitData.startingPossessions[pawn] = new List<ThingDefCount>();
            }

            foreach (var possesion in startingPossessions)
            {
                gameInitData.startingPossessions[firstPawn].Add(possesion);
            }

            pawnStore.Remove(sessionId);
        }
    }
    
    private static Faction NewFactionWithIdeo(string name, Color color, FactionDef def, IdeologyData chooseIdeoInfo)
    {
        var faction = new Faction
        {
            loadID = Find.UniqueIDsManager.GetNextFactionID(),
            def = def,
            Name = name,
            color = color,
            hidden = true,        
        };

        faction.ideos = new FactionIdeosTracker(faction);

        if (!ModsConfig.IdeologyActive || Find.IdeoManager.classicMode || chooseIdeoInfo.SelectedIdeo == null)
        {
            faction.ideos.SetPrimary(Faction.OfPlayer.ideos.PrimaryIdeo);
        }
        else
        {
            var newIdeo = GenerateIdeo(chooseIdeoInfo);
            faction.ideos.SetPrimary(newIdeo);
            Find.IdeoManager.Add(newIdeo);
        }

        foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
            faction.TryMakeInitialRelationsWith(other);

        Find.FactionManager.Add(faction);

        var newWorldFactionData = FactionWorldData.New(faction.loadID);
        Multiplayer.WorldComp.factionData[faction.loadID] = newWorldFactionData;
        newWorldFactionData.ReassignIds();

        // Add new faction to all maps (the new map is handled by map generation code)
        foreach (Map map in Find.Maps)
            MapSetup.InitNewFactionData(map, faction);

        foreach (var f in Find.FactionManager.AllFactions.Where(f => f.IsPlayer))
            if (f != faction)
                faction.SetRelation(new FactionRelation(f, FactionRelationKind.Neutral));

        return faction;
    }

    private static Ideo GenerateIdeo(IdeologyData chooseIdeoInfo)
    {
        List<MemeDef> list = chooseIdeoInfo.SelectedIdeo.memes.ToList();

        if (chooseIdeoInfo.SelectedStructure != null)
            list.Add(chooseIdeoInfo.SelectedStructure);
        else if (DefDatabase<MemeDef>.AllDefsListForReading.Where(m => m.category == MemeCategory.Structure && IdeoUtility.IsMemeAllowedFor(m, Find.Scenario.playerFaction.factionDef)).TryRandomElement(out var result))
            list.Add(result);

        Ideo ideo = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms(Find.FactionManager.OfPlayer.def, forceNoExpansionIdeo: false, null, null, list, chooseIdeoInfo.SelectedIdeo.classicPlus, forceNoWeaponPreference: true));
        new Page_ChooseIdeoPreset { selectedStyles = chooseIdeoInfo.SelectedStyles }.ApplySelectedStylesToIdeo(ideo);

        return ideo;
    }

    public static int GetStartingPawnsCountForScenario(Scenario scenario)
    {
        foreach (ScenPart part in scenario.AllParts)
        {
            if (part is ScenPart_ConfigPage_ConfigureStartingPawnsBase startingPawnsConfig)
            {
                return startingPawnsConfig.TotalPawnCount;
            }
        }

        MpLog.Error("No starting pawns config found to access startingPawnCount");

        return 0;
    }
}
public record FactionCreationData : ISyncSimple
{
    public string factionName;
    public PlanetTile startingTile;
    public Color factionColor;
    [CanBeNull] public ScenarioDef scenarioDef;
    public IdeologyData chooseIdeoInfo;
    public bool generateMap;
    public List<ThingDefCount> startingPossessions;
    public bool setupNextMapFromTickZero;
}

