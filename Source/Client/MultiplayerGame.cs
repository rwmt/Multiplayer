using Multiplayer.Client.Desyncs;
using RimWorld;
using RimWorld.BaseGen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Multiplayer.Client.Comp;
using Multiplayer.Client.Persistent;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class MultiplayerGame
    {
        public SyncCoordinator sync = new();

        public MultiplayerWorldComp worldComp;
        public MultiplayerGameComp gameComp;
        public List<MultiplayerMapComp> mapComps = new();
        public List<AsyncTimeComp> asyncTimeComps = new();
        public SharedCrossRefs sharedCrossRefs = new();

        private Faction myFaction;
        public Faction myFactionLoading;

        public Dictionary<int, PlayerDebugState> playerDebugState = new();

        public Faction RealPlayerFaction => myFaction ?? myFactionLoading;

        public MultiplayerGame()
        {
            DeferredStackTracing.acc = 0;

            Toils_Ingest.cardinals = GenAdj.CardinalDirections.ToList();
            Toils_Ingest.diagonals = GenAdj.DiagonalDirections.ToList();
            GenAdj.adjRandomOrderList = null;
            CellFinder.mapEdgeCells = null;
            CellFinder.mapSingleEdgeCells = new List<IntVec3>[4];

            TradeSession.trader = null;
            TradeSession.playerNegotiator = null;
            TradeSession.deal = null;
            TradeSession.giftMode = false;

            DebugTools.curTool = null;
            ClearPortraits();
            RealTime.moteList.Clear();

            Room.nextRoomID = 1;
            District.nextDistrictID = 1;
            Region.nextId = 1;
            ListerHaulables.groupCycleIndex = 0;

            ZoneColorUtility.nextGrowingZoneColorIndex = 0;
            ZoneColorUtility.nextStorageZoneColorIndex = 0;

            SetThingMakerSeed(1);

            foreach (var field in typeof(DebugSettings).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (!field.IsLiteral && field.FieldType == typeof(bool))
                    field.SetValue(null, default(bool));

            typeof(DebugSettings).TypeInitializer.Invoke(null, null);

            foreach (var resolver in DefDatabase<RuleDef>.AllDefs.SelectMany(r => r.resolvers))
                if (resolver is SymbolResolver_EdgeThing edgeThing)
                    edgeThing.randomRotations = new List<int>() { 0, 1, 2, 3 };

            typeof(SymbolResolver_SingleThing).TypeInitializer.Invoke(null, null);

            foreach (var initialOpinion in Multiplayer.session.initialOpinions)
                sync.AddClientOpinionAndCheckDesync(initialOpinion);
            Multiplayer.session.initialOpinions.Clear();
        }

        public static void ClearPortraits()
        {
            foreach (var (_, cachedPortraits) in PortraitsCache.cachedPortraits)
            {
                foreach (var portrait in cachedPortraits.ToList())
                {
                    var cached = portrait.Value;
                    cached.LastUseTime = Time.time - 2f; // RimWorld expires portraits that have been unused for more than 1 second
                    cachedPortraits[portrait.Key] = cached;
                }
            }

            PortraitsCache.RemoveExpiredCachedPortraits();
        }

        public void SetThingMakerSeed(int seed)
        {
            foreach (var maker in CaptureThingSetMakers.captured)
            {
                if (maker is ThingSetMaker_Nutrition n)
                    n.nextSeed = seed;
                if (maker is ThingSetMaker_MarketValue m)
                    m.nextSeed = seed;
            }
        }

        public void OnDestroy()
        {
            FactionContext.Clear();
            ThingContext.Clear();
        }

        public IEnumerable<ISession> GetSessions(Map map)
        {
            foreach (var s in worldComp.trading)
                yield return s;

            if (worldComp.splitSession != null)
                yield return worldComp.splitSession;

            if (map == null) yield break;
            var mapComp = map.MpComp();

            if (mapComp.caravanForming != null)
                yield return mapComp.caravanForming;

            if (mapComp.transporterLoading != null)
                yield return mapComp.transporterLoading;

            if (mapComp.ritualSession != null)
                yield return mapComp.ritualSession;
        }

        public void ChangeRealPlayerFaction(int newFaction)
        {
            ChangeRealPlayerFaction(Find.FactionManager.GetById(newFaction));
        }

        public void ChangeRealPlayerFaction(Faction newFaction)
        {
            myFaction = newFaction;
            FactionContext.Set(newFaction);
            worldComp.SetFaction(newFaction);

            foreach (Map m in Find.Maps)
                m.MpComp().SetFaction(newFaction);

            Find.ColonistBar.MarkColonistsDirty();
            Find.CurrentMap.mapDrawer.RegenerateEverythingNow();
        }
    }
}
