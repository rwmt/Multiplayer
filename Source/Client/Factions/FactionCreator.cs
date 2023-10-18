using System.Collections.Generic;
using System.Linq;
using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Factions;

public static class FactionCreator
{
    private static Dictionary<int, List<Pawn>> pawnStore = new();

    public static void ClearData()
    {
        pawnStore.Clear();
    }

    [SyncMethod(exposeParameters = new[] { 1 })]
    public static void SendPawn(int sessionId, Pawn p)
    {
        pawnStore.GetOrAddNew(sessionId).Add(p);
    }

    [SyncMethod]
    public static void CreateFaction(int sessionId, string factionName, int tile, string scenario, FactionRelationKind relation, ChooseIdeoInfo chooseIdeoInfo)
    {
        PrepareState(sessionId);

        var self = TickPatch.currentExecutingCmdIssuedBySelf;

        LongEventHandler.QueueLongEvent(delegate
        {
            int newFactionID = Find.UniqueIDsManager.GetNextFactionID();

            var newFaction = NewFaction(
                newFactionID,
                factionName,
                FactionDefOf.PlayerColony,
                chooseIdeoInfo
            );

            newFaction.hidden = true;

            foreach (var f in Find.FactionManager.AllFactions.Where(f => f.IsPlayer))
                if (f != newFaction)
                {
                    newFaction.SetRelation(new FactionRelation(f, relation));
                }

            FactionContext.Push(newFaction);

            foreach (var pawn in StartingPawnUtility.StartingAndOptionalPawns)
                pawn.ideo.SetIdeo(newFaction.ideos.PrimaryIdeo);

            var newMap = GenerateNewMap(tile, scenario);
            FactionContext.Pop();

            // Add new faction to all maps but the new
            foreach (Map map in Find.Maps)
                if (map != newMap)
                    MapSetup.InitNewFactionData(map, newFaction);

            foreach (Map map in Find.Maps)
                foreach (var f in Find.FactionManager.AllFactions.Where(f => f.IsPlayer))
                    map.attackTargetsCache.Notify_FactionHostilityChanged(f, newFaction);

            FactionContext.Push(newFaction);
            try
            {
                InitNewGame();
            }
            finally
            {
                FactionContext.Pop();
            }

            if (self)
            {
                Current.Game.CurrentMap = newMap;

                Multiplayer.game.ChangeRealPlayerFaction(newFaction);

                // todo setting faction of self
                Multiplayer.Client.Send(
                    Packets.Client_SetFaction,
                    Multiplayer.session.playerId,
                    newFaction.loadID
                );
            }
        }, "GeneratingMap", doAsynchronously: true, GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);
    }

    private static Map GenerateNewMap(int tile, string scenario)
    {
        // This has to be null, otherwise, during map generation, Faction.OfPlayer returns it which breaks FactionContext
        Find.GameInitData.playerFaction = null;
        Find.GameInitData.PrepForMapGen();

        var mapParent = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
        mapParent.Tile = tile;
        mapParent.SetFaction(Faction.OfPlayer);
        Find.WorldObjects.Add(mapParent);

        var prevScenario = Find.Scenario;
        Current.Game.scenarioInt = DefDatabase<ScenarioDef>.AllDefs.First(s => s.defName == scenario).scenario;

        try
        {
            return GetOrGenerateMapUtility.GetOrGenerateMap(
                tile,
                new IntVec3(250, 1, 250),
                null
            );
        }
        finally
        {
            Current.Game.scenarioInt = prevScenario;
        }
    }

    private static void InitNewGame()
    {
        PawnUtility.GiveAllStartingPlayerPawnsThought(ThoughtDefOf.NewColonyOptimism);
        ResearchUtility.ApplyPlayerStartingResearch();
    }

    public static void SetGameInitData()
    {
        Current.Game.InitData = new GameInitData
        {
            startingPawnCount = 3,
            gameToLoad = "dummy" // Prevent special calculation path in GenTicks.TicksAbs
        };
    }

    public static void PrepareState(int sessionId)
    {
        SetGameInitData();

        if (pawnStore.TryGetValue(sessionId, out var pawns))
        {
            Current.Game.InitData.startingAndOptionalPawns = pawns;
            Current.Game.InitData.startingPossessions = new Dictionary<Pawn, List<ThingDefCount>>();
            foreach (var p in pawns)
                Current.Game.InitData.startingPossessions[p] = new List<ThingDefCount>();

            pawnStore.Remove(sessionId);
        }
    }

    private static Faction NewFaction(int id, string name, FactionDef def, ChooseIdeoInfo chooseIdeoInfo)
    {
        Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.loadID == id);

        if (faction == null)
        {
            faction = new Faction { loadID = id, def = def };
            faction.ideos = new FactionIdeosTracker(faction);

            if (!ModsConfig.IdeologyActive || Find.IdeoManager.classicMode)
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
        }

        faction.Name = name;
        faction.def = def;

        return faction;
    }

    private static Ideo GenerateIdeo(ChooseIdeoInfo chooseIdeoInfo)
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
}
