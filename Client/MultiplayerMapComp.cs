using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace Multiplayer.Client
{
    public class MultiplayerMapComp : MapComponent
    {
        public static bool tickingFactions;

        public IdBlock mapIdBlock;
        public Dictionary<int, FactionMapData> factionMapData = new Dictionary<int, FactionMapData>();

        public CaravanFormingSession caravanForming;

        // for SaveCompression
        public List<Thing> tempLoadedThings;

        public MultiplayerMapComp(Map map) : base(map)
        {
        }

        public void CreateCaravanFormingSession(bool reform, Action onClosed, bool mapAboutToBeRemoved)
        {
            if (caravanForming != null) return;
            caravanForming = new CaravanFormingSession(map, reform, onClosed, mapAboutToBeRemoved);
        }

        public override void MapComponentTick()
        {
            if (Multiplayer.Client == null) return;

            tickingFactions = true;

            foreach (var data in factionMapData)
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
            if (!factionMapData.TryGetValue(faction.loadID, out FactionMapData data))
            {
                if (!Multiplayer.simulating)
                    MpLog.Log("No map faction data for faction {0} on map {1}", faction, map.uniqueID);
                return;
            }

            map.designationManager = data.designationManager;
            map.areaManager = data.areaManager;
            map.zoneManager = data.zoneManager;
            map.haulDestinationManager = data.haulDestinationManager;
            map.listerHaulables = data.listerHaulables;
            map.resourceCounter = data.resourceCounter;
            map.listerFilthInHomeArea = data.listerFilthInHomeArea;
        }

        public override void ExposeData()
        {
            // Data marker
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                bool isPlayerHome = map.IsPlayerHome;
                Scribe_Values.Look(ref isPlayerHome, "isPlayerHome", false, true);
            }

            Scribe_Deep.Look(ref caravanForming, "caravanFormingSession", map);
            Multiplayer.ExposeIdBlock(ref mapIdBlock, "mapIdBlock");

            ExposeFactionData();
        }

        private void ExposeFactionData()
        {
            // The faction whose data is currently set
            int currentFactionId = Faction.OfPlayer.loadID;
            Scribe_Values.Look(ref currentFactionId, "currentFactionId");

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var data = new Dictionary<int, FactionMapData>(factionMapData);
                data.Remove(currentFactionId);
                ScribeUtil.Look(ref data, "factionMapData", LookMode.Deep, map);
            }
            else
            {
                ScribeUtil.Look(ref factionMapData, "factionMapData", LookMode.Deep, map);
                if (factionMapData == null)
                    factionMapData = new Dictionary<int, FactionMapData>();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                factionMapData[currentFactionId] = FactionMapData.FromMap(map);
            }
        }
    }

    public class FactionMapData : IExposable
    {
        public Map map;
        public int factionId;

        // Saved
        public DesignationManager designationManager;
        public AreaManager areaManager;
        public ZoneManager zoneManager;
        public HashSet<Thing> claimed = new HashSet<Thing>();
        public HashSet<Thing> forbidden = new HashSet<Thing>();

        // Not saved
        public HaulDestinationManager haulDestinationManager;
        public ListerHaulables listerHaulables;
        public ResourceCounter resourceCounter;
        public ListerFilthInHomeArea listerFilthInHomeArea;

        // Loading ctor
        public FactionMapData(Map map)
        {
            this.map = map;

            haulDestinationManager = new HaulDestinationManager(map);
            listerHaulables = new ListerHaulables(map);
            resourceCounter = new ResourceCounter(map);
            listerFilthInHomeArea = new ListerFilthInHomeArea(map);
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
            Scribe_Values.Look(ref factionId, "factionId");
            Scribe_Deep.Look(ref designationManager, "designationManager", map);
            Scribe_Deep.Look(ref areaManager, "areaManager", map);
            Scribe_Deep.Look(ref zoneManager, "zoneManager", map);
        }

        public static FactionMapData New(int factionId, Map map)
        {
            return new FactionMapData(factionId, map);
        }

        public static FactionMapData FromMap(Map map)
        {
            return new FactionMapData(map)
            {
                factionId = map.ParentFaction.loadID,

                designationManager = map.designationManager,
                areaManager = map.areaManager,
                zoneManager = map.zoneManager,

                haulDestinationManager = map.haulDestinationManager,
                listerHaulables = map.listerHaulables,
                resourceCounter = map.resourceCounter,
                listerFilthInHomeArea = map.listerFilthInHomeArea,
            };
        }
    }
}
