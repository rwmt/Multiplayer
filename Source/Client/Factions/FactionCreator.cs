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
    public static void CreateFaction(
        int playerId, string factionName, int tile,
        [CanBeNull] ScenarioDef scenarioDef, ChooseIdeoInfo chooseIdeoInfo,
        bool generateMap
    )
    {
        var self = TickPatch.currentExecutingCmdIssuedBySelf;

        LongEventHandler.QueueLongEvent(() =>
        {
            PrepareGameInitData(playerId);

            var scenario = scenarioDef?.scenario ?? Current.Game.Scenario;
            var newFaction = NewFactionWithIdeo(
                factionName,
                scenario.playerFaction.factionDef,
                chooseIdeoInfo
            );

            Map newMap = null;

            if (generateMap)
                using (MpScope.PushFaction(newFaction))
                {
                    foreach (var pawn in StartingPawnUtility.StartingAndOptionalPawns)
                        pawn.ideo.SetIdeo(newFaction.ideos.PrimaryIdeo);

                    newMap = GenerateNewMap(tile, scenario);
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

                // todo setting faction of self
                Multiplayer.Client.Send(
                    Packets.Client_SetFaction,
                    Multiplayer.session.playerId,
                    newFaction.loadID
                );
            }
        }, "GeneratingMap", doAsynchronously: true, GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);
    }

    private static Map GenerateNewMap(int tile, Scenario scenario)
    {
        // This has to be null, otherwise, during map generation, Faction.OfPlayer returns it which breaks FactionContext
        Find.GameInitData.playerFaction = null;
        Find.GameInitData.PrepForMapGen();

        var mapParent = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
        mapParent.Tile = tile;
        mapParent.SetFaction(Faction.OfPlayer);
        Find.WorldObjects.Add(mapParent);

        var prevScenario = Find.Scenario;
        Current.Game.Scenario = scenario;
        generatingMap = true;

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
            generatingMap = false;
            Current.Game.Scenario = prevScenario;
        }
    }

    private static void InitNewGame()
    {
        PawnUtility.GiveAllStartingPlayerPawnsThought(ThoughtDefOf.NewColonyOptimism);
        ResearchUtility.ApplyPlayerStartingResearch();
    }

    private static void PrepareGameInitData(int sessionId)
    {
        Current.Game.InitData = new GameInitData
        {
            startingPawnCount = 3,
            gameToLoad = "dummy" // Prevent special calculation path in GenTicks.TicksAbs
        };

        if (pawnStore.TryGetValue(sessionId, out var pawns))
        {
            Current.Game.InitData.startingAndOptionalPawns = pawns;
            Current.Game.InitData.startingPossessions = new Dictionary<Pawn, List<ThingDefCount>>();
            foreach (var p in pawns)
                Current.Game.InitData.startingPossessions[p] = new List<ThingDefCount>();

            pawnStore.Remove(sessionId);
        }
    }

    private static Faction NewFactionWithIdeo(string name, FactionDef def, ChooseIdeoInfo chooseIdeoInfo)
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
