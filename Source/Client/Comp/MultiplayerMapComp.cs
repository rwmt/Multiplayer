using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Verse;

namespace Multiplayer.Client
{
    public class MultiplayerMapComp : IExposable
    {
        public static bool tickingFactions;

        public Map map;

        //public IdBlock mapIdBlock;
        public Dictionary<int, FactionMapData> factionMapData = new Dictionary<int, FactionMapData>();

        public CaravanFormingSession caravanForming;
        public TransporterLoading transporterLoading;
        public List<PersistentDialog> mapDialogs = new List<PersistentDialog>();

        // for SaveCompression
        public List<Thing> tempLoadedThings;

        public MultiplayerMapComp(Map map)
        {
            this.map = map;
        }

        public CaravanFormingSession CreateCaravanFormingSession(bool reform, Action onClosed, bool mapAboutToBeRemoved)
        {
            if (caravanForming == null)
                caravanForming = new CaravanFormingSession(map, reform, onClosed, mapAboutToBeRemoved);
            return caravanForming;
        }

        public TransporterLoading CreateTransporterLoadingSession(List<CompTransporter> transporters)
        {
            if (transporterLoading == null)
                transporterLoading = new TransporterLoading(map, transporters);
            return transporterLoading;
        }

        public void DoTick()
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
                return;

            map.designationManager = data.designationManager;
            map.areaManager = data.areaManager;
            map.zoneManager = data.zoneManager;
            map.haulDestinationManager = data.haulDestinationManager;
            map.listerHaulables = data.listerHaulables;
            map.resourceCounter = data.resourceCounter;
            map.listerFilthInHomeArea = data.listerFilthInHomeArea;
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
        }

        private int currentFactionId;

        private void ExposeFactionData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                int currentFactionId = Faction.OfPlayer.loadID;
                ScribeUtil.LookValue(currentFactionId, "currentFactionId");

                var data = new Dictionary<int, FactionMapData>(factionMapData);
                data.Remove(currentFactionId);
                ScribeUtil.LookWithValueKey(ref data, "factionMapData", LookMode.Deep, map);
            }
            else
            {
                // The faction whose data is currently set
                Scribe_Values.Look(ref currentFactionId, "currentFactionId");

                ScribeUtil.LookWithValueKey(ref factionMapData, "factionMapData", LookMode.Deep, map);
                if (factionMapData == null)
                    factionMapData = new Dictionary<int, FactionMapData>();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                factionMapData[currentFactionId] = FactionMapData.FromMap(map, currentFactionId);
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

        public static FactionMapData FromMap(Map map, int factionId)
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
            };
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
        }
    }

    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.Notify_SwitchedMap))]
    static class HandleMissingDialogsGameStart
    {
        static void Prefix()
        {
            if (Multiplayer.Client == null) return;

            // Trading window on resume save
            if (Multiplayer.WorldComp.trading.NullOrEmpty()) return;
            if (Multiplayer.WorldComp.trading.FirstOrDefault(t => t.playerNegotiator == null) is MpTradeSession trade)
            {
                trade.OpenWindow();
            }
        }
    }

    [HarmonyPatch(typeof(WorldInterface), nameof(WorldInterface.WorldInterfaceUpdate))]
    static class HandleDialogsOnWorldRender
    {
        static void Prefix(WorldInterface __instance)
        {
            if (Multiplayer.Client == null) return;

            if (Find.World.renderer.wantedMode == WorldRenderMode.Planet)
            {
                // Hide trading window for map trades
                if (Multiplayer.WorldComp.trading.All(t => t.playerNegotiator?.Map != null))
                {
                    if (Find.WindowStack.IsOpen(typeof(TradingWindow)))
                        Find.WindowStack.TryRemove(typeof(TradingWindow), doCloseSound: false);
                }

                // Hide transport loading window
                if (Find.WindowStack.IsOpen(typeof(MpLoadTransportersWindow)))
                    Find.WindowStack.TryRemove(typeof(MpLoadTransportersWindow), doCloseSound: false);
            }
        }
    }
}
