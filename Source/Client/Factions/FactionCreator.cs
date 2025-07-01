using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Multiplayer.API;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
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
        var self = TickPatch.currentExecutingCmdIssuedBySelf;

        LongEventHandler.QueueLongEvent(() =>
        {
            var scenario = creationData.scenarioDef?.scenario ?? Current.Game.Scenario;
            Map newMap = null;

            PrepareGameInitData(playerId, scenario, self, creationData.startingPossessions);

            var newFaction = NewFactionWithIdeo(
                creationData.factionName,
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
                InitNewGame();

            if (self)
            {
                Current.Game.CurrentMap = newMap;

                Multiplayer.game.ChangeRealPlayerFaction(newFaction);

                CameraJumper.TryJump(MapGenerator.playerStartSpotInt, newMap);

                PostGameStart(scenario);

                // todo setting faction of self
                Multiplayer.Client.Send(
                    Packets.Client_SetFaction,
                    Multiplayer.session.playerId,
                    newFaction.loadID
                );
            }
        }, "GeneratingMap", doAsynchronously: true, GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);
    }

    private static void PostGameStart(Scenario scenario)
    {
        /**
        ScenPart_StartingResearch.cs				
        ScenPart_AutoActivateMonolith.cs		
        ScenPart_CreateIncident.cs		
        ScenPart_GameStartDialog.cs			
        ScenPart_PlayerFaction.cs		
        ScenPart_Rule.cs

        Would like to call PostGameStart on all implementations (scenario.PostGameStart) -
        but dont know if it breaks with dlcs other than biotech - especially while only called
        on self
        **/

        HashSet<Type> types = new HashSet<Type>
        {
            typeof(ScenPart_PlayerFaction),
            typeof(ScenPart_GameStartDialog),
            typeof(ScenPart_StartingResearch),
        };

        foreach (ScenPart part in scenario.AllParts)
        {
            if (types.Contains(part.GetType()))
            {
                part.PostGameStart();
            }
        }
    }

    private static Map GenerateNewMap(int tile, Scenario scenario, bool setupNextMapFromTickZero)
    {
        // This has to be null, otherwise, during map generation, Faction.OfPlayer returns it which breaks FactionContext
        Find.GameInitData.playerFaction = null;
        Find.GameInitData.PrepForMapGen();

        // ScenPart_PlayerFaction --> PreMapGenerate 

        var settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
        settlement.Tile = tile;
        settlement.SetFaction(Faction.OfPlayer);
        Find.WorldObjects.Add(settlement);

        // ^^^^ Duplicate Code here ^^^^

        var prevScenario = Find.Scenario;
        var prevStartingTile = Find.GameInfo.startingTile;

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

            SetAllThingWithCompsOnMapForbidden(map);

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

    // TODO: Remove this Workaround (see Issue #535)
    private static void SetAllThingWithCompsOnMapForbidden(Map map)
    {
        foreach (Thing thing in map.listerThings.AllThings)
        {
            if (thing is ThingWithComps)
            {
                thing.SetForbidden(true, false);
            }
        }
    }

    private static void InitNewGame()
    {
        PawnUtility.GiveAllStartingPlayerPawnsThought(ThoughtDefOf.NewColonyOptimism);

        ResearchUtility.ApplyPlayerStartingResearch();
        // Initialize anomaly. Since the Anomaly comp is currently shared by all players,
        // we need to ensure that any new factions have access to anomaly research if
        // anomaly content was started.
        Find.ResearchManager.Notify_MonolithLevelChanged(Find.Anomaly.Level);
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

    private static Faction NewFactionWithIdeo(string name, FactionDef def, IdeologyData chooseIdeoInfo)
    {
        var faction = new Faction
        {
            loadID = Find.UniqueIDsManager.GetNextFactionID(),
            def = def,
            Name = name,
            hidden = true
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
    public int startingTile;
    [CanBeNull] public ScenarioDef scenarioDef;
    public IdeologyData chooseIdeoInfo;
    public bool generateMap;
    public List<ThingDefCount> startingPossessions;
    public bool setupNextMapFromTickZero;
}

