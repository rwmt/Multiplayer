using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    [HotSwappable]
    public class MultiplayerMapComp : IExposable
    {
        public static bool tickingFactions;

        public Map map;

        //public IdBlock mapIdBlock;
        public Dictionary<int, FactionMapData> factionData = new Dictionary<int, FactionMapData>();
        public Dictionary<int, CustomFactionMapData> customFactionData = new Dictionary<int, CustomFactionMapData>();

        public CaravanFormingSession caravanForming;
        public TransporterLoading transporterLoading;
        public List<PersistentDialog> mapDialogs = new List<PersistentDialog>();

        // for SaveCompression
        public List<Thing> tempLoadedThings;

        public MultiplayerMapComp(Map map)
        {
            this.map = map;
        }

        public void CreateCaravanFormingSession(bool reform, Action onClosed, bool mapAboutToBeRemoved)
        {
            if (caravanForming != null) return;
            caravanForming = new CaravanFormingSession(map, reform, onClosed, mapAboutToBeRemoved);
        }

        public void CreateTransporterLoadingSession(List<CompTransporter> transporters)
        {
            if (transporterLoading != null) return;
            transporterLoading = new TransporterLoading(map, transporters);
        }

        public void DoTick()
        {
            if (Multiplayer.Client == null) return;

            tickingFactions = true;

            foreach (var data in factionData)
            {
                map.PushFaction(data.Key);
                data.Value.listerHaulables.ListerHaulablesTick();
                data.Value.resourceCounter.ResourceCounterTick();
                map.PopFaction();
            }

            tickingFactions = false;
        }

        public void SetFaction(Faction faction)
        {
            if (!factionData.TryGetValue(faction.loadID, out FactionMapData data))
                return;

            map.designationManager = data.designationManager;
            map.areaManager = data.areaManager;
            map.zoneManager = data.zoneManager;

            map.haulDestinationManager = data.haulDestinationManager;
            map.listerHaulables = data.listerHaulables;
            map.resourceCounter = data.resourceCounter;
            map.listerFilthInHomeArea = data.listerFilthInHomeArea;
            map.listerMergeables = data.listerMergeables;
        }

        public CustomFactionMapData GetCurrentCustomFactionData()
        {
            return customFactionData[Faction.OfPlayer.loadID];
        }

        public void Notify_ThingDespawned(Thing t)
        {
            foreach (var data in customFactionData.Values)
                data.unforbidden.Remove(t);
        }

        public void ExposeData()
        {
            // Data marker
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                bool isPlayerHome = map.IsPlayerHome;
                Scribe_Values.Look(ref isPlayerHome, "isPlayerHome", false, true);
            }

            Scribe_Deep.Look(ref caravanForming, "caravanFormingSession", map);
            Scribe_Deep.Look(ref transporterLoading, "transporterLoading", map);

            Scribe_Collections.Look(ref mapDialogs, "mapDialogs", LookMode.Deep, map);
            if (Scribe.mode == LoadSaveMode.LoadingVars && mapDialogs == null)
                mapDialogs = new List<PersistentDialog>();

            //Multiplayer.ExposeIdBlock(ref mapIdBlock, "mapIdBlock");

            ExposeFactionData();
            ExposeCustomFactionData();
        }

        public int currentFactionId;

        private void ExposeFactionData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                int currentFactionId = Faction.OfPlayer.loadID;
                ScribeUtil.LookValue(currentFactionId, "currentFactionId");

                var data = new Dictionary<int, FactionMapData>(factionData);
                data.Remove(currentFactionId);
                ScribeUtil.LookValueDeep(ref data, "factionMapData", map);
            }
            else
            {
                // The faction whose data is currently set
                Scribe_Values.Look(ref currentFactionId, "currentFactionId");

                ScribeUtil.LookValueDeep(ref factionData, "factionMapData", map);
                if (factionData == null)
                    factionData = new Dictionary<int, FactionMapData>();
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                factionData[currentFactionId] = FactionMapData.NewFromMap(map, currentFactionId);
            }
        }

        private void ExposeCustomFactionData()
        {
            ScribeUtil.LookValueDeep(ref customFactionData, "customFactionMapData", map);
            if (customFactionData == null)
                customFactionData = new Dictionary<int, CustomFactionMapData>();
        }
    }

    // Per-faction storage for RimWorld managers
    public class FactionMapData : IExposable
    {
        public Map map;
        public int factionId;

        // Saved
        public DesignationManager designationManager;
        public AreaManager areaManager;
        public ZoneManager zoneManager;

        // Not saved
        public HaulDestinationManager haulDestinationManager;
        public ListerHaulables listerHaulables;
        public ResourceCounter resourceCounter;
        public ListerFilthInHomeArea listerFilthInHomeArea;
        public ListerMergeables listerMergeables;

        private FactionMapData() { }

        // Loading ctor
        public FactionMapData(Map map)
        {
            this.map = map;

            haulDestinationManager = new HaulDestinationManager(map);
            listerHaulables = new ListerHaulables(map);
            resourceCounter = new ResourceCounter(map);
            listerFilthInHomeArea = new ListerFilthInHomeArea(map);
            listerMergeables = new ListerMergeables(map);
        }

        private FactionMapData(int factionId, Map map) : this(map)
        {
            this.factionId = factionId;

            designationManager = new DesignationManager(map);
            areaManager = new AreaManager(map);
            zoneManager = new ZoneManager(map);
        }

        public void ExposeData()
        {
            ExposeActor.Register(() => map.PushFaction(factionId));

            Scribe_Values.Look(ref factionId, "factionId");
            Scribe_Deep.Look(ref designationManager, "designationManager", map);
            Scribe_Deep.Look(ref areaManager, "areaManager", map);
            Scribe_Deep.Look(ref zoneManager, "zoneManager", map);

            ExposeActor.Register(() => map.PopFaction());
        }

        public static FactionMapData New(int factionId, Map map)
        {
            return new FactionMapData(factionId, map);
        }

        public static FactionMapData NewFromMap(Map map, int factionId)
        {
            return new FactionMapData(map)
            {
                factionId = factionId,

                designationManager = map.designationManager,
                areaManager = map.areaManager,
                zoneManager = map.zoneManager,

                haulDestinationManager = map.haulDestinationManager,
                listerHaulables = map.listerHaulables,
                resourceCounter = map.resourceCounter,
                listerFilthInHomeArea = map.listerFilthInHomeArea,
                listerMergeables = map.listerMergeables,
            };
        }
    }

    public class CustomFactionMapData : IExposable
    {
        public Map map;
        public int factionId;

        public HashSet<Thing> claimed = new HashSet<Thing>();
        public HashSet<Thing> unforbidden = new HashSet<Thing>();

        // Loading ctor
        public CustomFactionMapData(Map map)
        {
            this.map = map;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionId, "factionId");
            Scribe_Collections.Look(ref unforbidden, "unforbidden", LookMode.Reference);
        }

        public static CustomFactionMapData New(int factionId, Map map)
        {
            return new CustomFactionMapData(map) { factionId = factionId };
        }
    }

    public class ExposeActor : IExposable
    {
        private Action action;

        private ExposeActor(Action action)
        {
            this.action = action;
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                action();
        }

        // This depends on the fact that the implementation of HashSet RimWorld currently uses
        // "preserves" insertion order (as long as elements are only added and not removed
        // [which is the case for Scribe managers])
        public static void Register(Action action)
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Scribe.loader.initer.RegisterForPostLoadInit(new ExposeActor(action));
        }
    }

    [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.DrawMapMesh))]
    static class ForceShowDialogs
    {
        static void Prefix(MapDrawer __instance)
        {
            if (Multiplayer.Client == null) return;

            var comp = __instance.map.MpComp();

            if (comp.mapDialogs.Any())
            {
                var newDialog = comp.mapDialogs.First().Dialog;
                //If NO mapdialogs (Dialog_NodeTrees) are open, add the first one to the window stack
                if (!Find.WindowStack.IsOpen(typeof(Dialog_NodeTree)) && !Find.WindowStack.IsOpen(newDialog.GetType())) {
                    Find.WindowStack.Add(newDialog);
                }
            }
            else if (comp.caravanForming != null)
            {
                if (!Find.WindowStack.IsOpen(typeof(MpFormingCaravanWindow)))
                    comp.caravanForming.OpenWindow(false);
            }
            else if (comp.transporterLoading != null)
            {
                if (!Find.WindowStack.IsOpen(typeof(MpLoadTransportersWindow)))
                    comp.transporterLoading.OpenWindow(false);
            }
            else if (Multiplayer.WorldComp.trading.FirstOrDefault(t => t.playerNegotiator.Map == comp.map) is MpTradeSession trading)
            {
                if (!Find.WindowStack.IsOpen(typeof(TradingWindow)))
                    Find.WindowStack.Add(new TradingWindow() { selectedTab = Multiplayer.WorldComp.trading.IndexOf(trading) });
            }
        }
    }

}
