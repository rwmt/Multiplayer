using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client.Factions;
using Multiplayer.Client.Persistent;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class MultiplayerMapComp : IExposable, IHasSemiPersistentData
    {
        public static bool tickingFactions;

        public Map map;

        public Dictionary<int, FactionMapData> factionData = new();
        public Dictionary<int, CustomFactionMapData> customFactionData = new();

        public SessionManager sessionManager = new();
        public List<PersistentDialog> mapDialogs = new List<PersistentDialog>();
        public int autosaveCounter;

        // for SaveCompression
        public List<Thing> tempLoadedThings;

        public MultiplayerMapComp(Map map)
        {
            this.map = map;
        }

        public CaravanFormingSession CreateCaravanFormingSession(bool reform, Action onClosed, bool mapAboutToBeRemoved, IntVec3? meetingSpot = null)
        {
            var caravanForming = sessionManager.GetFirstOfType<CaravanFormingSession>();
            if (caravanForming == null)
            {
                caravanForming = new CaravanFormingSession(map, reform, onClosed, mapAboutToBeRemoved, meetingSpot);
                if (!sessionManager.AddSession(caravanForming))
                    return null;
            }
            return caravanForming;
        }

        public TransporterLoading CreateTransporterLoadingSession(List<CompTransporter> transporters)
        {
            var transporterLoading = sessionManager.GetFirstOfType<TransporterLoading>();
            if (transporterLoading == null)
            {
                transporterLoading = new TransporterLoading(map, transporters);
                if (!sessionManager.AddSession(transporterLoading))
                    return null;
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
                    return null;
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

        [Conditional("DEBUG")]
        public void CheckInvariant()
        {
            if (factionData.TryGetValue(Faction.OfPlayer.loadID, out var data) && map.areaManager != data.areaManager)
                Log.Error($"(Debug) Invariant broken for {Faction.OfPlayer}: {FactionContext.stack.ToStringSafeEnumerable()} {factionData.FirstOrDefault(d => d.Value.areaManager == map.areaManager)} {StackTraceUtility.ExtractStackTrace()}");
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

            sessionManager.WriteSemiPersistent(writer);
        }

        public void ReadSemiPersistent(ByteReader data)
        {
            autosaveCounter = data.ReadInt32();

            sessionManager.ReadSemiPersistent(data);
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
