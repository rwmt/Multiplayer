using Multiplayer.Client.Networking;
using Multiplayer.Common;
using RimWorld;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Multiplayer.Client.AsyncTime;
using Multiplayer.Client.Comp;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public static class HostUtil
    {
        // Host entry points:
        // - singleplayer save, server browser
        // - singleplayer save, ingame
        // - replay, server browser
        // - replay, ingame
        public static async ClientTask HostServer(ServerSettings settings, bool fromReplay)
        {
            Log.Message($"Starting the server");

            CreateSession(settings);

            if (!fromReplay)
                SetupGameFromSingleplayer();

            // Server already pre-inited in HostWindow
            PrepareLocalServer(settings, fromReplay);

            CreateLocalClient();
            PrepareGame();
            SetGameState(settings);

            Multiplayer.session.dataSnapshot = await CreateGameData();

            MakeHostOnServer();

            // todo handle sending cmds for hosting from loaded replay?
            SaveLoad.SendGameData(Multiplayer.session.dataSnapshot, false);

            StartLocalServer();
        }

        private static void CreateSession(ServerSettings settings)
        {
            var session = new MultiplayerSession
            {
                myFactionId = Faction.OfPlayer.loadID,
                localServerSettings = settings,
                gameName = settings.gameName,
                dataSnapshot = Multiplayer.session?.dataSnapshot // This is the case when hosting from a replay
            };

            Multiplayer.session = session;
        }

        private static void PrepareLocalServer(ServerSettings settings, bool fromReplay)
        {
            var localServer = Multiplayer.LocalServer;
            MultiplayerServer.instance = Multiplayer.LocalServer;

            localServer.hostUsername = Multiplayer.username;
            localServer.worldData.hostFactionId = Faction.OfPlayer.loadID;
            localServer.worldData.spectatorFactionId = Multiplayer.WorldComp.spectatorFaction.loadID;

            if (settings.steam)
                localServer.TickEvent += SteamIntegration.ServerSteamNetTick;

            if (fromReplay)
            {
                localServer.gameTimer = TickPatch.Timer;
                localServer.startingTimer = TickPatch.Timer;
            }

            localServer.initDataSource = new TaskCompletionSource<ServerInitData>();
            localServer.CompleteInitData(
                ServerInitData.Deserialize(new ByteReader(ClientJoiningState.PackInitData(settings.syncConfigs)))
            );
        }

        private static void PrepareGame()
        {
            foreach (var tickable in TickPatch.AllTickables)
                tickable.Cmds.Clear();

            Multiplayer.game.ChangeRealPlayerFaction(Faction.OfPlayer);
            Multiplayer.session.ReapplyPrefs();

            Find.MainTabsRoot.EscapeCurrentTab(false);

            Multiplayer.session.AddMsg("If you are having any issues with the mod and would like some help resolving them, then please reach out to us on our Discord server:", false);
            Multiplayer.session.AddMsg(new ChatMsg_Url("https://discord.gg/S4bxXpv"), false);
        }

        private static void SetGameState(ServerSettings settings)
        {
            Multiplayer.AsyncWorldTime.SetDesiredTimeSpeed(TimeSpeed.Paused);
            foreach (var map in Find.Maps)
                map.AsyncTime().SetDesiredTimeSpeed(TimeSpeed.Paused);

            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;

            Multiplayer.GameComp.asyncTime = settings.asyncTime;
            Multiplayer.GameComp.multifaction = settings.multifaction;
            Multiplayer.GameComp.debugMode = settings.debugMode;
            Multiplayer.GameComp.logDesyncTraces = settings.desyncTraces;
            Multiplayer.GameComp.pauseOnLetter = settings.pauseOnLetter;
            Multiplayer.GameComp.timeControl = settings.timeControl;
        }

        private static async Task<GameDataSnapshot> CreateGameData()
        {
            await LongEventTask.ContinueInLongEvent("MpSaving", false);
            return SaveLoad.CreateGameDataSnapshot(SaveLoad.SaveAndReload(), Multiplayer.GameComp.multifaction);
        }

        private static void SetupGameFromSingleplayer()
        {
            var worldComp = new MultiplayerWorldComp(Find.World);

            Multiplayer.game = new MultiplayerGame
            {
                gameComp = new MultiplayerGameComp(),
                asyncWorldTimeComp = new AsyncWorldTimeComp(Find.World) { worldTicks = Find.TickManager.TicksGame },
                worldComp = worldComp
            };

            Faction.OfPlayer.Name = $"{Multiplayer.username}'s faction";

            var spectator = AddNewFaction("Spectator", FactionDefOf.PlayerColony);
            spectator.hidden = true;
            spectator.SetRelation(new FactionRelation(Faction.OfPlayer, FactionRelationKind.Neutral));
            worldComp.spectatorFaction = spectator;

            var playerFactionData = FactionWorldData.FromCurrent(Faction.OfPlayer.loadID);
            worldComp.factionData[Faction.OfPlayer.loadID] = playerFactionData;

            foreach (var faction in Find.FactionManager.AllFactions.Where(f => f.IsPlayer))
                if (faction != Faction.OfPlayer)
                {
                    var factionData = FactionWorldData.New(spectator.loadID);
                    worldComp.factionData[faction.loadID] = factionData;

                    factionData.researchManager.progress = new(playerFactionData.researchManager.progress);
                    factionData.researchManager.techprints = new(playerFactionData.researchManager.techprints);
                }

            foreach (FactionWorldData data in worldComp.factionData.Values)
                data.ReassignIds();

            foreach (Map map in Find.Maps)
            {
                MapSetup.SetupMap(map);

                AsyncTimeComp async = map.AsyncTime();
                async.mapTicks = Find.TickManager.TicksGame;
                async.SetDesiredTimeSpeed(Find.TickManager.CurTimeSpeed);
            }
        }

        public static Faction AddNewFaction(string name, FactionDef def)
        {
            Faction faction = new Faction { loadID = Find.UniqueIDsManager.GetNextFactionID(), def = def };

            faction.ideos = new FactionIdeosTracker(faction);
            faction.ideos.ChooseOrGenerateIdeo(new IdeoGenerationParms());

            foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
                faction.TryMakeInitialRelationsWith(other);

            faction.Name = name;
            faction.def = def;

            Find.FactionManager.Add(faction);

            return faction;
        }

        private static void CreateLocalClient()
        {
            if (Multiplayer.session.localServerSettings.arbiter)
                StartArbiter();

            LocalClientConnection localClient = new LocalClientConnection(Multiplayer.username);
            LocalServerConnection localServerConn = new LocalServerConnection(Multiplayer.username);

            localClient.serverSide = localServerConn;
            localServerConn.clientSide = localClient;

            localClient.ChangeState(ConnectionStateEnum.ClientPlaying);

            Multiplayer.session.client = localClient;
        }

        private static void MakeHostOnServer()
        {
            var server = Multiplayer.LocalServer;
            var player = server.playerManager.OnConnected(((LocalClientConnection)Multiplayer.Client).serverSide);
            server.playerManager.MakeHost(player);
        }

        private static void StartLocalServer()
        {
            Multiplayer.LocalServer.running = true;

            Multiplayer.localServerThread = new Thread(Multiplayer.LocalServer.Run)
            {
                Name = "Local server thread"
            };
            Multiplayer.localServerThread.Start();

            const string text = "Server started.";
            Messages.Message(text, MessageTypeDefOf.SilentInput, false);
            Log.Message(text);
        }

        private static void StartArbiter()
        {
            Multiplayer.session.AddMsg("The Arbiter instance is starting...", false);

            Multiplayer.LocalServer.liteNet.SetupArbiterConnection();

            string args = $"-batchmode -nographics -arbiter -logfile arbiter_log.txt -connect=127.0.0.1:{Multiplayer.LocalServer.liteNet.ArbiterPort}";

            if (GenCommandLine.TryGetCommandLineArg("savedatafolder", out string saveDataFolder))
                args += $" \"-savedatafolder={saveDataFolder}\"";

            string arbiterInstancePath;
            if (Application.platform == RuntimePlatform.OSXPlayer)
            {
                arbiterInstancePath = Application.dataPath + "/MacOS/" + Process.GetCurrentProcess().MainModule!.ModuleName;
            }
            else
            {
                arbiterInstancePath = Process.GetCurrentProcess().MainModule!.FileName;
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
                    Log.Error("Win32 Error Code: " + ((Win32Exception)ex).NativeErrorCode);
                }
            }
        }
    }
}
