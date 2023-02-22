using HarmonyLib;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using RimWorld;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Multiplayer.Client.AsyncTime;
using Multiplayer.Client.Comp;
using Multiplayer.Client.Util;
using Multiplayer.Common.Util;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HotSwappable]
    public static class HostUtil
    {
        // Host entry points:
        // - singleplayer save, server browser
        // - singleplayer save, ingame
        // - replay, server browser
        // - replay, ingame
        public static async ClientTask HostServer(ServerSettings settings, bool fromReplay, bool hadSimulation, bool asyncTime)
        {
            Log.Message($"Starting the server");

            CreateSession(settings);

            // Server already pre-inited in HostWindow
            PrepareLocalServer(settings, fromReplay);

            if (!fromReplay)
                SetupGameFromSingleplayer();

            CreateLocalClient();
            PrepareGame();

            Multiplayer.session.dataSnapshot = await CreateGameData(settings, asyncTime);

            MakeHostOnServer();

            // todo handle sending cmds for hosting from loaded replay?
            SaveLoad.SendGameData(Multiplayer.session.dataSnapshot, false);

            StartLocalServer();
        }

        private static void CreateSession(ServerSettings settings)
        {
            var session = new MultiplayerSession();
            if (Multiplayer.session != null) // This is the case when hosting from a replay
                session.dataSnapshot = Multiplayer.session.dataSnapshot;

            Multiplayer.session = session;

            session.myFactionId = Faction.OfPlayer.loadID;
            session.localServerSettings = settings;
            session.gameName = settings.gameName;
        }

        private static void PrepareLocalServer(ServerSettings settings, bool fromReplay)
        {
            var localServer = Multiplayer.LocalServer;
            MultiplayerServer.instance = Multiplayer.LocalServer;

            localServer.hostUsername = Multiplayer.username;
            localServer.worldData.defaultFactionId = Faction.OfPlayer.loadID;

            if (settings.steam)
                localServer.TickEvent += SteamIntegration.ServerSteamNetTick;

            if (fromReplay)
                localServer.gameTimer = TickPatch.Timer;

            localServer.initDataSource = new TaskCompletionSource<ServerInitData>();
            localServer.CompleteInitData(
                ServerInitData.Deserialize(new ByteReader(ClientJoiningState.PackInitData(settings.syncConfigs)))
            );
        }

        private static void PrepareGame()
        {
            foreach (var tickable in TickPatch.AllTickables)
                tickable.Cmds.Clear();

            Find.PlaySettings.usePlanetDayNightSystem = false;

            Multiplayer.game.ChangeRealPlayerFaction(Faction.OfPlayer);
            Multiplayer.session.ReapplyPrefs();

            Find.MainTabsRoot.EscapeCurrentTab(false);

            Multiplayer.session.AddMsg("If you are having any issues with the mod and would like some help resolving them, then please reach out to us on our Discord server:", false);
            Multiplayer.session.AddMsg(new ChatMsg_Url("https://discord.gg/S4bxXpv"), false);
        }

        private static async Task<GameDataSnapshot> CreateGameData(ServerSettings settings, bool asyncTime)
        {
            Multiplayer.WorldTime.SetDesiredTimeSpeed(TimeSpeed.Paused);
            foreach (var map in Find.Maps)
                map.AsyncTime().SetDesiredTimeSpeed(TimeSpeed.Paused);

            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;

            Multiplayer.GameComp.asyncTime = asyncTime;
            Multiplayer.GameComp.debugMode = settings.debugMode;
            Multiplayer.GameComp.logDesyncTraces = settings.desyncTraces;
            Multiplayer.GameComp.pauseOnLetter = settings.pauseOnLetter;
            Multiplayer.GameComp.timeControl = settings.timeControl;

            await LongEventTask.ContinueInLongEvent("MpSaving", false);

            return SaveLoad.CreateGameDataSnapshot(SaveLoad.SaveAndReload());
        }

        private static void SetupGameFromSingleplayer()
        {
            var worldComp = new MultiplayerWorldComp(Find.World);

            Faction NewFaction(int id, string name, FactionDef def)
            {
                Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.loadID == id);

                if (faction == null)
                {
                    faction = new Faction() { loadID = id, def = def };

                    faction.ideos = new FactionIdeosTracker(faction);
                    faction.ideos.ChooseOrGenerateIdeo(new IdeoGenerationParms());

                    foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
                        faction.TryMakeInitialRelationsWith(other);

                    Find.FactionManager.Add(faction);

                    worldComp.factionData[faction.loadID] = FactionWorldData.New(faction.loadID);
                }

                faction.Name = name;
                faction.def = def;

                return faction;
            }

            Faction.OfPlayer.Name = $"{Multiplayer.username}'s faction";
            //comp.factionData[Faction.OfPlayer.loadID] = FactionWorldData.FromCurrent();

            Multiplayer.game = new MultiplayerGame
            {
                gameComp = new MultiplayerGameComp(Current.Game)
                {
                    globalIdBlock = new IdBlock(GetMaxUniqueId(), 1_000_000_000)
                },
                worldTimeComp = new WorldTimeComp(Find.World) { worldTicks = Find.TickManager.TicksGame },
                worldComp = worldComp
            };

            var opponent = NewFaction(Multiplayer.GlobalIdBlock.NextId(), "Opponent", FactionDefOf.PlayerColony);
            opponent.hidden = true;
            opponent.SetRelation(new FactionRelation(Faction.OfPlayer, FactionRelationKind.Hostile));

            foreach (FactionWorldData data in worldComp.factionData.Values)
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
                BeforeMapGeneration.InitNewMapFactionData(map, opponent);

                AsyncTimeComp async = map.AsyncTime();
                async.mapTicks = Find.TickManager.TicksGame;
                async.SetDesiredTimeSpeed(Find.TickManager.CurTimeSpeed);
            }
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
