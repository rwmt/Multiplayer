using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Multiplayer.API;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
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

    // Set while SendPawn reads a starting pawn, see NoFallbackIdeoForStartingPawnPatch
    public static bool readingStartingPawn;

    [SyncMethod]
    public static void SendPawn(int playerId, StartingPawnData data)
    {
        if (ReadStartingPawn(data.xml) is { } pawn)
            pawnStore.GetOrAddNew(playerId).Add(pawn);
    }

    // The page's pawns believe in the temporary page faction's ideoligion, which isn't registered: on load the
    // reference didn't resolve, the pawn got a random fallback ideoligion and CreateFaction's SetIdeo then recorded
    // an ideo change and kept the fallback as a previous ideo (#537). Send them without one, CreateFaction sets it.
    public static StartingPawnData WriteStartingPawn(Pawn pawn)
    {
        var tracker = pawn.ideo;
        var ideo = tracker?.ideo;
        if (tracker != null) tracker.ideo = null;

        try
        {
            return new StartingPawnData { xml = ScribeUtil.WriteExposable(pawn) };
        }
        finally
        {
            if (tracker != null) tracker.ideo = ideo;
        }
    }

    private static Pawn ReadStartingPawn(byte[] xml)
    {
        if (xml == null) return null;

        // Vanilla loads pawns only while a game initializes. In Playing the needs tracker's constructor evaluates
        // Need_Food.MaxLevel as a stat over hediffs whose pawn is set only when cross refs resolve (#536).
        var prevState = Current.programStateInt;
        Current.programStateInt = ProgramState.MapInitializing;
        readingStartingPawn = true;

        try
        {
            return ScribeUtil.ReadExposable<Pawn>(xml);
        }
        finally
        {
            readingStartingPawn = false;
            Current.programStateInt = prevState;
        }
    }

    [SyncMethod]
    public static void ChangeFactionColor(Faction faction, Color color)
    {
        if (faction is not { IsPlayer: true })
            return;

        faction.color = color;
    }

    [SyncMethod]
    public static void CreateFaction(int playerId, FactionCreationData creationData)
    {
        var executingOnlyOnIssuer = TickPatch.currentExecutingCmdIssuedBySelf;

        LongEventHandler.QueueLongEvent(() =>
        {
            // Runs in the command's simulation context (SimLongEventContext), which also covers the actions map
            // generation defers with ExecuteWhenFinished. The pop restores that context's faction data, not the
            // local player's (the context's exit re-applies the real player faction, the issuer's new one).
            using var _ = MpScope.PushFaction(Multiplayer.WorldComp.spectatorFaction);

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
                // Runs on the issuer only: e.g. archived dialogs take unique ids
                using var localOnly = LocalOnlyScope.Enter();

                ClearPortraitCacheFromPawnConfigPage();

                Current.Game.CurrentMap = newMap;

                Multiplayer.game.ChangeRealPlayerFaction(newFaction);

                InitLocalVisuals(scenario, newMap);

                // todo setting faction of self
                Multiplayer.Client.Send(new ClientSetFactionPacket(Multiplayer.session.playerId, newFaction.loadID));
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

        // PrepForMapGen adds the custom xenotypes of the static (client-local) starting pawn generation requests
        // to the game's customXenotypeDatabase. Only the issuer has its requests there (others may have stale
        // ones), and the database feeds factionless pawn generation, so clear them on every client. The pawns
        // keep their genes; their custom xenotype just isn't added to the database.
        StartingPawnUtility.StartingAndOptionalPawnGenerationRequests.Clear();
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
            var newIdeo = GenerateIdeo(chooseIdeoInfo, def);
            faction.ideos.SetPrimary(newIdeo);
            Find.IdeoManager.Add(newIdeo);
        }

        foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
            faction.TryMakeInitialRelationsWith(other);

        // Add recaches Faction.OfPlayer to the first player faction, keep the current context
        var ofPlayer = Find.FactionManager.ofPlayer;
        Find.FactionManager.Add(faction);
        Find.FactionManager.ofPlayer = ofPlayer;

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

    // factionDef: the new faction's (from the chosen scenario), not Faction.OfPlayer's or the game scenario's
    private static Ideo GenerateIdeo(IdeologyData chooseIdeoInfo, FactionDef factionDef)
    {
        List<MemeDef> list = chooseIdeoInfo.SelectedIdeo.memes.ToList();

        if (chooseIdeoInfo.SelectedStructure != null)
            list.Add(chooseIdeoInfo.SelectedStructure);
        else if (DefDatabase<MemeDef>.AllDefsListForReading.Where(m => m.category == MemeCategory.Structure && IdeoUtility.IsMemeAllowedFor(m, factionDef)).TryRandomElement(out var result))
            list.Add(result);

        Ideo ideo = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms(factionDef, forceNoExpansionIdeo: false, null, null, list, chooseIdeoInfo.SelectedIdeo.classicPlus, forceNoWeaponPreference: true));
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

// A starting pawn's XML, read inside SendPawn (not by the sync layer) so that the read runs in a load-like state.
// Same wire format as the exposed Pawn parameter it replaces.
public class StartingPawnData : ISynchronizable
{
    public byte[] xml;

    public void Sync(SyncWorker sync)
    {
        if (sync is WritingSyncWorker writer)
            writer.Writer.WritePrefixedBytes(xml);
        else if (sync is ReadingSyncWorker reader)
            xml = reader.Reader.ReadPrefixedBytes();
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
