using Multiplayer.Client.Desyncs;
using Multiplayer.Client.EarlyPatches;
using RimWorld;
using RimWorld.BaseGen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class MultiplayerGame
    {
        public SyncCoordinator sync = new SyncCoordinator();

        public MultiplayerWorldComp worldComp;
        public List<MultiplayerMapComp> mapComps = new List<MultiplayerMapComp>();
        public List<MapAsyncTimeComp> asyncTimeComps = new List<MapAsyncTimeComp>();
        public SharedCrossRefs sharedCrossRefs = new SharedCrossRefs();

        public Faction dummyFaction;
        private Faction myFaction;
        public Faction myFactionLoading;

        public Dictionary<int, PlayerDebugState> playerDebugState = new Dictionary<int, PlayerDebugState>();

        public Faction RealPlayerFaction
        {
            get => myFaction ?? myFactionLoading;

            set
            {
                myFaction = value;
                FactionContext.Set(value);
                worldComp.SetFaction(value);

                foreach (Map m in Find.Maps)
                    m.MpComp().SetFaction(value);
            }
        }

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

            Prefs.PauseOnLoad = false; // causes immediate desyncs on load if misaligned between host and clients

            foreach (var field in typeof(DebugSettings).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (!field.IsLiteral && field.FieldType == typeof(bool))
                    field.SetValue(null, default(bool));

            typeof(DebugSettings).TypeInitializer.Invoke(null, null);

            foreach (var resolver in DefDatabase<RuleDef>.AllDefs.SelectMany(r => r.resolvers))
                if (resolver is SymbolResolver_EdgeThing edgeThing)
                    edgeThing.randomRotations = new List<int>() { 0, 1, 2, 3 };

            typeof(SymbolResolver_SingleThing).TypeInitializer.Invoke(null, null);
        }

        public static void ClearPortraits()
        {
            foreach (var portraitParams in PortraitsCache.cachedPortraits)
            {
                foreach (var portrait in portraitParams.CachedPortraits.ToList())
                {
                    var cached = portrait.Value;
                    cached.LastUseTime = Time.time - 2f; // RimWorld expires portraits that have been unused for more than 1 second
                    portraitParams.CachedPortraits[portrait.Key] = cached;
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
    }
}
