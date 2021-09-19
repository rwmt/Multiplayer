using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client.Persistent;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client
{
    public class MultiplayerMapComp : IExposable, IHasSemiPersistentData
    {
        public static bool tickingFactions;

        public Map map;

        //public IdBlock mapIdBlock;
        public Dictionary<int, FactionMapData> factionData = new Dictionary<int, FactionMapData>();
        public Dictionary<int, CustomFactionMapData> customFactionData = new Dictionary<int, CustomFactionMapData>();

        public CaravanFormingSession caravanForming;
        public TransporterLoading transporterLoading;
        public RitualSession ritualSession;
        public List<PersistentDialog> mapDialogs = new List<PersistentDialog>();
        public int autosaveCounter;

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

        public RitualSession CreateRitualSession(RitualData data)
        {
            if (ritualSession == null)
                ritualSession = new RitualSession(map, data);
            return ritualSession;
        }

        public void DoTick()
        {
            autosaveCounter++;

            tickingFactions = true;

            try
            {
                foreach (var data in factionData)
                {
                    map.PushFaction(data.Key);
                    data.Value.listerHaulables.ListerHaulablesTick();
                    data.Value.resourceCounter.ResourceCounterTick();
                    map.PopFaction();
                }
            }
            finally
            {
                tickingFactions = false;
            }
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
                Scribe_Custom.LookValue(currentFactionId, "currentFactionId");

                var data = new Dictionary<int, FactionMapData>(factionData);
                data.Remove(currentFactionId);
                Scribe_Custom.LookValueDeep(ref data, "factionMapData", map);
            }
            else
            {
                // The faction whose data is currently set
                Scribe_Values.Look(ref currentFactionId, "currentFactionId");

                Scribe_Custom.LookValueDeep(ref factionData, "factionMapData", map);
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
            Scribe_Custom.LookValueDeep(ref customFactionData, "customFactionMapData", map);
            customFactionData ??= new Dictionary<int, CustomFactionMapData>();
        }

        public void WriteSemiPersistent(ByteWriter writer)
        {
            writer.WriteInt32(autosaveCounter);

            writer.WriteBool(ritualSession != null);
            ritualSession?.Write(writer);
        }

        public void ReadSemiPersistent(ByteReader reader)
        {
            autosaveCounter = reader.ReadInt32();

            var hasRitual = reader.ReadBool();
            if (hasRitual)
            {
                var session = new RitualSession(map);
                session.Read(reader);
                ritualSession = session;
            }
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
                if (Find.WindowStack.IsOpen(typeof(TransporterLoadingProxy)))
                    Find.WindowStack.TryRemove(typeof(TransporterLoadingProxy), doCloseSound: false);
            }
        }
    }

    [HarmonyPatch(typeof(ListerHaulables))]
    [HarmonyPatch(nameof(ListerHaulables.ListerHaulablesTick))]
    public static class HaulablesTickPatch
    {
        static bool Prefix() => Multiplayer.Client == null || MultiplayerMapComp.tickingFactions;
    }

    [HarmonyPatch(typeof(ResourceCounter))]
    [HarmonyPatch(nameof(ResourceCounter.ResourceCounterTick))]
    public static class ResourcesTickPatch
    {
        static bool Prefix() => Multiplayer.Client == null || MultiplayerMapComp.tickingFactions;
    }
}
