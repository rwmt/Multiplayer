using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client.Factions;
using Multiplayer.Client.Persistent;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client
{
    public class MultiplayerMapComp : IExposable, IHasSessionData
    {
        public static bool tickingFactions;

        public Map map;

        // SortedDictionary to ensure determinism
        public SortedDictionary<int, FactionMapData> factionData = new();
        public SortedDictionary<int, CustomFactionMapData> customFactionData = new();

        public SessionManager sessionManager;
        public List<PersistentDialog> mapDialogs = new();
        public int autosaveCounter;

        // for SaveCompression
        public List<Thing> tempLoadedThings;

        public MultiplayerMapComp(Map map)
        {
            this.map = map;
            sessionManager = new(map);
        }

        public CaravanFormingSession CreateCaravanFormingSession(Faction faction, bool reform, Action onClosed, bool mapAboutToBeRemoved, IntVec3? meetingSpot = null)
        {
            var caravanForming = sessionManager.GetFirstOfType<CaravanFormingSession>();
            if (caravanForming == null)
            {
                caravanForming = new CaravanFormingSession(faction, map, reform, onClosed, mapAboutToBeRemoved, meetingSpot);
                if (!sessionManager.AddSession(caravanForming))
                {
                    // Shouldn't happen if the session doesn't exist already, show an error just in case
                    Log.Error($"Failed trying to created a session of type {nameof(CaravanFormingSession)} - prior session did not exist and creating session failed.");
                    return null;
                }
            }
            return caravanForming;
        }

        public TransporterLoading CreateTransporterLoadingSession(Faction faction, List<CompTransporter> transporters)
        {
            var transporterLoading = sessionManager.GetFirstOfType<TransporterLoading>();
            if (transporterLoading == null)
            {
                transporterLoading = new TransporterLoading(faction, map, transporters);
                if (!sessionManager.AddSession(transporterLoading))
                {
                    // Shouldn't happen if the session doesn't exist already, show an error just in case
                    Log.Error($"Failed trying to created a session of type {nameof(TransporterLoading)} - prior session did not exist and creating session failed.");
                    return null;
                }
            }
            return transporterLoading;
        }

        public RitualSession CreateRitualSession(RitualData data)
        {
            var ritualSession = sessionManager.GetFirstOfType<RitualSession>();
            if (ritualSession == null)
            {
                ritualSession = new RitualSession(map, data);
                if (!sessionManager.AddSession(ritualSession))
                {
                    // Shouldn't happen if the session doesn't exist already, show an error just in case
                    Log.Error($"Failed trying to created a session of type {nameof(RitualSession)} - prior session did not exist and creating session failed.");
                    return null;
                }
            }
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

            sessionManager.ExposeSessions();

            Scribe_Collections.Look(ref mapDialogs, "mapDialogs", LookMode.Deep, map);
            if (Scribe.mode == LoadSaveMode.LoadingVars && mapDialogs == null)
                mapDialogs = new List<PersistentDialog>();

            ExposeFactionData();
            ExposeCustomFactionData();
        }

        public int currentFactionId;

        private void ExposeFactionData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                int currentFactionId = GetFactionId(map.zoneManager);
                Scribe_Custom.LookValue(currentFactionId, "currentFactionId");

                var savedFactionData = new SortedDictionary<int, FactionMapData>(factionData);
                savedFactionData.Remove(currentFactionId);
                Scribe_Custom.LookValueDeep(ref savedFactionData, "factionMapData", map);
            }
            else
            {
                // The faction whose data is currently set
                Scribe_Values.Look(ref currentFactionId, "currentFactionId");

                Scribe_Custom.LookValueDeep(ref factionData, "factionMapData", map);
                factionData ??= new SortedDictionary<int, FactionMapData>();
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                factionData[currentFactionId] = FactionMapData.NewFromMap(map, currentFactionId);
            }
        }

        private void ExposeCustomFactionData()
        {
            Scribe_Custom.LookValueDeep(ref customFactionData, "customFactionMapData", map);
            customFactionData ??= new SortedDictionary<int, CustomFactionMapData>();
        }

        public void WriteSessionData(ByteWriter writer)
        {
            writer.WriteInt32(autosaveCounter);

            sessionManager.WriteSessionData(writer);
        }

        public void ReadSessionData(ByteReader reader)
        {
            autosaveCounter = reader.ReadInt32();

            sessionManager.ReadSessionData(reader);
        }

        public int GetFactionId(ZoneManager zoneManager)
        {
            return factionData.First(kv => kv.Value.zoneManager == zoneManager).Key;
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
            if (!Multiplayer.WorldComp.trading.NullOrEmpty()) return;
            // playerNegotiator == null can only happen during loading? Is this a resuming check?
            Multiplayer.WorldComp.trading.FirstOrDefault(t => t.playerNegotiator == null)?.OpenWindow();
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
