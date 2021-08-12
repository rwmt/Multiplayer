using HarmonyLib;
using Ionic.Zlib;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public static class HostUtil
    {
        public static void HostServer(ServerSettings settings, bool fromReplay, bool withSimulation = false, bool debugMode = false, bool logDesyncTraces = false)
        {
            Log.Message($"Starting the server");

            var session = Multiplayer.session = new MultiplayerSession();
            session.myFactionId = Faction.OfPlayer.loadID;
            session.localSettings = settings;
            session.gameName = settings.gameName;

            var localServer = new MultiplayerServer(settings);

            if (withSimulation)
            {
                localServer.savedGame = GZipStream.CompressBuffer(OnMainThread.cachedGameData);
                localServer.semiPersistent = GZipStream.CompressBuffer(OnMainThread.cachedSemiPersistent);
                localServer.mapData = OnMainThread.cachedMapData.ToDictionary(kv => kv.Key, kv => GZipStream.CompressBuffer(kv.Value));
                localServer.mapCmds = OnMainThread.cachedMapCmds.ToDictionary(kv => kv.Key, kv => kv.Value.Select(c => c.Serialize()).ToList());
            }
            else
            {
                OnMainThread.ClearCaches();
            }

            localServer.debugMode = debugMode;
            localServer.debugOnlySyncCmds = Sync.handlers.Where(h => h.debugOnly).Select(h => h.syncId).ToHashSet();
            localServer.hostOnlySyncCmds = Sync.handlers.Where(h => h.hostOnly).Select(h => h.syncId).ToHashSet();
            localServer.hostUsername = Multiplayer.username;
            localServer.coopFactionId = Faction.OfPlayer.loadID;

            localServer.rwVersion = /*session.mods.remoteRwVersion =*/ VersionControl.CurrentVersionString; // todo
            // localServer.modNames = session.mods.remoteModNames = LoadedModManager.RunningModsListForReading.Select(m => m.Name).ToArray();
            // localServer.modIds = session.mods.remoteModIds = LoadedModManager.RunningModsListForReading.Select(m => m.PackageId).ToArray();
            // localServer.workshopModIds = session.mods.remoteWorkshopModIds = ModManagement.GetEnabledWorkshopMods().ToArray();
            localServer.defInfos = /*session.mods.defInfo =*/ Multiplayer.localDefInfos;
            localServer.serverData = JoinData.WriteServerData();

            //Log.Message($"MP Host modIds: {string.Join(", ", localServer.modIds)}");
            //Log.Message($"MP Host workshopIds: {string.Join(", ", localServer.workshopModIds)}");

            if (settings.steam)
                localServer.NetTick += SteamIntegration.ServerSteamNetTick;

            if (fromReplay)
                localServer.gameTimer = TickPatch.Timer;

            MultiplayerServer.instance = localServer;
            session.localServer = localServer;

            if (!fromReplay)
                SetupGameFromSingleplayer();

            foreach (var tickable in TickPatch.AllTickables)
                tickable.Cmds.Clear();

            Find.PlaySettings.usePlanetDayNightSystem = false;

            Multiplayer.RealPlayerFaction = Faction.OfPlayer;
            localServer.playerFactions[Multiplayer.username] = Faction.OfPlayer.loadID;

            SetupLocalClient();

            Find.MainTabsRoot.EscapeCurrentTab(false);

            Multiplayer.session.AddMsg("If you are having any issues with the mod and would like some help resolving them, then please reach out to us on our Discord server:", false);
            Multiplayer.session.AddMsg(new ChatMsg_Url("https://discord.gg/S4bxXpv"), false);

            if (withSimulation)
            {
                StartServerThread();
            }
            else
            {
                var timeSpeed = TimeSpeed.Paused;

                Multiplayer.WorldComp.TimeSpeed = timeSpeed;
                foreach (var map in Find.Maps)
                    map.AsyncTime().TimeSpeed = timeSpeed;
                Multiplayer.WorldComp.UpdateTimeSpeed();

                Multiplayer.WorldComp.debugMode = debugMode;
                Multiplayer.WorldComp.logDesyncTraces = logDesyncTraces;

                LongEventHandler.QueueLongEvent(() =>
                {
                    SaveLoad.CacheGameData(SaveLoad.SaveAndReload());
                    SaveLoad.SendCurrentGameData(false);

                    StartServerThread();
                }, "MpSaving", false, null);
            }

            void StartServerThread()
            {
                var netStarted = localServer.StartListeningNet();
                var lanStarted = localServer.StartListeningLan();

                string text = "Server started.";

                if (netStarted != null)
                    text += (netStarted.Value ? $" Direct at {settings.bindAddress}:{localServer.NetPort}." : " Couldn't bind direct.");

                if (lanStarted != null)
                    text += (lanStarted.Value ? $" LAN at {settings.lanAddress}:{localServer.LanPort}." : " Couldn't bind LAN.");

                session.serverThread = new Thread(localServer.Run)
                {
                    Name = "Local server thread"
                };
                session.serverThread.Start();

                Messages.Message(text, MessageTypeDefOf.SilentInput, false);
                Log.Message(text);
            }
        }

        private static void SetupGameFromSingleplayer()
        {
            MultiplayerWorldComp comp = new MultiplayerWorldComp(Find.World);

            Faction NewFaction(int id, string name, FactionDef def)
            {
                Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.loadID == id);

                if (faction == null)
                {
                    faction = new Faction() { loadID = id, def = def };

                    foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
                        faction.TryMakeInitialRelationsWith(other);

                    Find.FactionManager.Add(faction);

                    comp.factionData[faction.loadID] = FactionWorldData.New(faction.loadID);
                }

                faction.Name = name;
                faction.def = def;

                return faction;
            }

            Faction.OfPlayer.Name = $"{Multiplayer.username}'s faction";
            //comp.factionData[Faction.OfPlayer.loadID] = FactionWorldData.FromCurrent();

            Multiplayer.game = new MultiplayerGame
            {
                worldComp = comp
            };

            comp.globalIdBlock = new IdBlock(GetMaxUniqueId(), 1_000_000_000);

            //var opponent = NewFaction(Multiplayer.GlobalIdBlock.NextId(), "Opponent", Multiplayer.FactionDef);
            //opponent.TrySetRelationKind(Faction.OfPlayer, FactionRelationKind.Hostile, false);

            foreach (FactionWorldData data in comp.factionData.Values)
            {
                foreach (DrugPolicy p in data.drugPolicyDatabase.policies)
                    p.uniqueId = Multiplayer.GlobalIdBlock.NextId();

                foreach (Outfit o in data.outfitDatabase.outfits)
                    o.uniqueId = Multiplayer.GlobalIdBlock.NextId();

                foreach (FoodRestriction o in data.foodRestrictionDatabase.foodRestrictions)
                    o.id = Multiplayer.GlobalIdBlock.NextId();
            }

            foreach (Map map in Find.Maps)
            {
                //mapComp.mapIdBlock = localServer.NextIdBlock();

                BeforeMapGeneration.SetupMap(map);
                //BeforeMapGeneration.InitNewMapFactionData(map, opponent);

                AsyncTimeComp async = map.AsyncTime();
                async.mapTicks = Find.TickManager.TicksGame;
                async.TimeSpeed = Find.TickManager.CurTimeSpeed;
            }

            Multiplayer.WorldComp.UpdateTimeSpeed();
        }

        private static void SetupLocalClient()
        {
            if (Multiplayer.session.localSettings.arbiter)
                StartArbiter();

            LocalClientConnection localClient = new LocalClientConnection(Multiplayer.username);
            LocalServerConnection localServerConn = new LocalServerConnection(Multiplayer.username);

            localServerConn.clientSide = localClient;
            localClient.serverSide = localServerConn;

            localClient.State = ConnectionStateEnum.ClientPlaying;
            localServerConn.State = ConnectionStateEnum.ServerPlaying;

            var serverPlayer = Multiplayer.LocalServer.OnConnected(localServerConn);
            serverPlayer.color = new ColorRGB(255, 0, 0);
            serverPlayer.status = PlayerStatus.Playing;
            serverPlayer.SendPlayerList();

            Multiplayer.session.client = localClient;
            Multiplayer.session.ReapplyPrefs();
        }

        private static void StartArbiter()
        {
            Multiplayer.session.AddMsg("The Arbiter instance is starting...", false);

            Multiplayer.LocalServer.SetupArbiterConnection();

            string args = $"-batchmode -nographics -arbiter -logfile arbiter_log.txt -connect=127.0.0.1:{Multiplayer.LocalServer.ArbiterPort}";

            if (GenCommandLine.TryGetCommandLineArg("savedatafolder", out string saveDataFolder))
                args += $" \"-savedatafolder={saveDataFolder}\"";

            string arbiterInstancePath;
            if (Application.platform == RuntimePlatform.OSXPlayer)
            {
                arbiterInstancePath = Application.dataPath + "/MacOS/" + Process.GetCurrentProcess().MainModule.ModuleName;
            }
            else
            {
                arbiterInstancePath = Process.GetCurrentProcess().MainModule.FileName;
            }

            try
            {
                Multiplayer.session.arbiter = Process.Start(
                    arbiterInstancePath,
                    args
                );
            }
            catch (Exception ex)
            {
                Multiplayer.session.AddMsg("Arbiter failed to start.", false);
                Log.Error("Arbiter failed to start.");
                Log.Error(ex.ToString());
                if (ex.InnerException is Win32Exception)
                {
                    Log.Error("Win32 Error Code: " + ((Win32Exception)ex).NativeErrorCode.ToString());
                }
            }
        }

        public static int GetMaxUniqueId()
        {
            return typeof(UniqueIDsManager)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(int))
                .Select(f => (int)f.GetValue(Find.UniqueIDsManager))
                .Max();
        }

        public static void SetAllUniqueIds(int value)
        {
            typeof(UniqueIDsManager)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(int))
                .Do(f => f.SetValue(Find.UniqueIDsManager, value));
        }
    }
}
