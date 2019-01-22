using Harmony;
using Ionic.Zlib;
using LiteNetLib;
using Multiplayer.Common;
using RimWorld;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client
{
    public static class ClientUtil
    {
        public static void TryConnect(IPAddress address, int port)
        {
            Multiplayer.session = new MultiplayerSession();
            NetManager netClient = new NetManager(new MpClientNetListener());

            netClient.Start();
            netClient.ReconnectDelay = 300;
            netClient.MaxConnectAttempts = 8;

            Multiplayer.session.netClient = netClient;
            netClient.Connect(address.ToString(), port, "");
        }

        public static void HostServer(ServerSettings settings, bool fromReplay, bool withSimulation = false, bool debugMode = false)
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
                localServer.mapData = OnMainThread.cachedMapData.ToDictionary(kv => kv.Key, kv => GZipStream.CompressBuffer(kv.Value));
                localServer.mapCmds = OnMainThread.cachedMapCmds.ToDictionary(kv => kv.Key, kv => kv.Value.Select(c => c.Serialize()).ToList());
            }
            else
            {
                OnMainThread.ClearCaches();
            }

            localServer.debugMode = debugMode;
            localServer.debugOnlySyncCmds = new HashSet<int>(Sync.handlers.Where(h => h.debugOnly).Select(h => h.syncId));
            localServer.hostOnlySyncCmds = new HashSet<int>(Sync.handlers.Where(h => h.hostOnly).Select(h => h.syncId));
            localServer.hostUsername = Multiplayer.username;
            localServer.coopFactionId = Faction.OfPlayer.loadID;

            localServer.rwVersion = session.mods.remoteRwVersion = VersionControl.CurrentVersionString;
            localServer.modNames = session.mods.remoteModNames = LoadedModManager.RunningModsListForReading.Select(m => m.Name).ToArray();
            localServer.defInfos = session.mods.defInfo = Multiplayer.localDefInfos;

            if (settings.steam)
                localServer.NetTick += SteamIntegration.ServerSteamNetTick;

            if (fromReplay)
                localServer.gameTimer = TickPatch.Timer;

            MultiplayerServer.instance = localServer;
            session.localServer = localServer;

            if (!fromReplay)
                SetupGame();

            foreach (var tickable in TickPatch.AllTickables)
                tickable.Cmds.Clear();

            Find.PlaySettings.usePlanetDayNightSystem = false;

            Multiplayer.RealPlayerFaction = Faction.OfPlayer;
            localServer.playerFactions[Multiplayer.username] = Faction.OfPlayer.loadID;

            SetupLocalClient();

            Find.MainTabsRoot.EscapeCurrentTab(false);

            Multiplayer.session.AddMsg("Wiki on desyncs:", false);
            Multiplayer.session.AddMsg(new ChatMsg_Url("https://github.com/Zetrith/Multiplayer/wiki/Desyncs"), false);

            if (withSimulation)
            {
                StartServerThread();
            }
            else
            {
                var timeSpeed = Prefs.data.pauseOnLoad ? TimeSpeed.Paused : TimeSpeed.Normal;

                Multiplayer.WorldComp.TimeSpeed = timeSpeed;
                foreach (var map in Find.Maps)
                    map.AsyncTime().TimeSpeed = timeSpeed;

                Multiplayer.WorldComp.debugMode = debugMode;

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

        private static void SetupGame()
        {
            MultiplayerWorldComp comp = new MultiplayerWorldComp(Find.World);
            Faction dummyFaction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.loadID == -1);

            if (dummyFaction == null)
            {
                dummyFaction = new Faction() { loadID = -1, def = Multiplayer.DummyFactionDef };

                foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
                    dummyFaction.TryMakeInitialRelationsWith(other);

                Find.FactionManager.Add(dummyFaction);

                comp.factionData[dummyFaction.loadID] = FactionWorldData.New(dummyFaction.loadID);
            }

            dummyFaction.Name = "Multiplayer dummy faction";
            dummyFaction.def = Multiplayer.DummyFactionDef;

            Faction.OfPlayer.Name = $"{Multiplayer.username}'s faction";
            comp.factionData[Faction.OfPlayer.loadID] = FactionWorldData.FromCurrent();

            Multiplayer.game = new MultiplayerGame
            {
                dummyFaction = dummyFaction,
                worldComp = comp
            };

            comp.globalIdBlock = new IdBlock(GetMaxUniqueId(), 1_000_000_000);

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

                MapAsyncTimeComp async = map.AsyncTime();
                async.mapTicks = Find.TickManager.TicksGame;
                async.TimeSpeed = Find.TickManager.CurTimeSpeed;
            }
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
            serverPlayer.status = PlayerStatus.Playing;
            serverPlayer.SendPlayerList();

            Multiplayer.session.client = localClient;
            Multiplayer.session.ReapplyPrefs();
        }

        private static void StartArbiter()
        {
            Multiplayer.session.AddMsg("The Arbiter instance is starting...", false);

            Multiplayer.LocalServer.SetupArbiterConnection();

            Multiplayer.session.arbiter = Process.Start(
                Process.GetCurrentProcess().MainModule.FileName,
                $"-batchmode -nographics -arbiter -logfile arbiter_log.txt -connect=127.0.0.1:{Multiplayer.LocalServer.ArbiterPort}"
            );
        }

        private static int GetMaxUniqueId()
        {
            return typeof(UniqueIDsManager)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(int))
                .Select(f => (int)f.GetValue(Find.UniqueIDsManager))
                .Max();
        }
    }

    public class MpClientNetListener : INetEventListener
    {
        public void OnPeerConnected(NetPeer peer)
        {
            IConnection conn = new MpNetConnection(peer);
            conn.username = Multiplayer.username;
            conn.State = ConnectionStateEnum.ClientJoining;

            Multiplayer.session.client = conn;
            Multiplayer.session.ReapplyPrefs();

            MpLog.Log("Net client connected");
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError error)
        {
            Log.Warning($"Net client error {error}");
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod method)
        {
            byte[] data = reader.GetRemainingBytes();
            Multiplayer.HandleReceive(new ByteReader(data), method == DeliveryMethod.ReliableOrdered);
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            var reader = new ByteReader(info.AdditionalData.GetRemainingBytes());
            Multiplayer.session.HandleDisconnectReason((MpDisconnectReason)reader.ReadByte(), reader.ReadPrefixedBytes());

            ConnectionStatusListeners.TryNotifyAll_Disconnected();

            OnMainThread.StopMultiplayer();
            MpLog.Log("Net client disconnected");
        }

        private static string DisconnectReasonString(DisconnectReason reason)
        {
            switch (reason)
            {
                case DisconnectReason.ConnectionFailed: return "Connection failed";
                case DisconnectReason.ConnectionRejected: return "Connection rejected";
                case DisconnectReason.Timeout: return "Timed out";
                case DisconnectReason.HostUnreachable: return "Host unreachable";
                case DisconnectReason.InvalidProtocol: return "Invalid library protocol";
                default: return "Disconnected";
            }
        }

        public void OnConnectionRequest(ConnectionRequest request) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    }

    public class LocalClientConnection : IConnection
    {
        public LocalServerConnection serverSide;

        public override int Latency { get => 0; set { } }

        public LocalClientConnection(string username)
        {
            this.username = username;
        }

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            Multiplayer.LocalServer.Enqueue(() =>
            {
                try
                {
                    serverSide.HandleReceive(new ByteReader(raw), reliable);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception handling packet by {serverSide}: {e}");
                }
            });
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
        }

        public override string ToString()
        {
            return "LocalClientConn";
        }
    }

    public class LocalServerConnection : IConnection
    {
        public LocalClientConnection clientSide;

        public override int Latency { get => 0; set { } }

        public LocalServerConnection(string username)
        {
            this.username = username;
        }

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            OnMainThread.Enqueue(() =>
            {
                try
                {
                    clientSide.HandleReceive(new ByteReader(raw), reliable);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception handling packet by {clientSide}: {e}");
                }
            });
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
        }

        public override string ToString()
        {
            return "LocalServerConn";
        }
    }

    public abstract class SteamBaseConn : IConnection
    {
        public readonly CSteamID remoteId;

        public SteamBaseConn(CSteamID remoteId)
        {
            this.remoteId = remoteId;
        }

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            byte[] full = new byte[1 + raw.Length];
            full[0] = reliable ? (byte)2 : (byte)0;
            raw.CopyTo(full, 1);

            SteamNetworking.SendP2PPacket(remoteId, full, (uint)full.Length, reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliable, 0);
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
            Send(Packets.Special_Steam_Disconnect, GetDisconnectBytes(reason, data));
        }

        protected override void HandleReceive(int msgId, int fragState, ByteReader reader, bool reliable)
        {
            if (msgId == (int)Packets.Special_Steam_Disconnect)
            {
                OnDisconnect();
            }
            else
            {
                base.HandleReceive(msgId, fragState, reader, reliable);
            }
        }

        public virtual void OnError(EP2PSessionError error)
        {
            OnDisconnect();
        }

        protected abstract void OnDisconnect();

        public override string ToString()
        {
            return $"SteamP2P ({remoteId}) ({username})";
        }
    }

    public class SteamClientConn : SteamBaseConn
    {
        public SteamClientConn(CSteamID remoteId) : base(remoteId)
        {
            SteamIntegration.ClearChannel(0);

            SteamNetworking.SendP2PPacket(remoteId, new byte[] { 1 }, 1, EP2PSend.k_EP2PSendReliable, 0);
        }

        protected override void HandleReceive(int msgId, int fragState, ByteReader reader, bool reliable)
        {
            if (msgId == (int)Packets.Special_Steam_Disconnect)
                Multiplayer.session.HandleDisconnectReason((MpDisconnectReason)reader.ReadByte(), reader.ReadPrefixedBytes());

            base.HandleReceive(msgId, fragState, reader, reliable);
        }

        public override void OnError(EP2PSessionError error)
        {
            Multiplayer.session.disconnectReasonKey = error == EP2PSessionError.k_EP2PSessionErrorTimeout ? "Connection timed out" : "Connection error";
            base.OnError(error);
        }

        protected override void OnDisconnect()
        {
            ConnectionStatusListeners.TryNotifyAll_Disconnected();
            OnMainThread.StopMultiplayer();
        }
    }

    public class SteamServerConn : SteamBaseConn
    {
        public SteamServerConn(CSteamID remoteId) : base(remoteId)
        {
        }

        protected override void OnDisconnect()
        {
            serverPlayer.Server.OnDisconnected(this, MpDisconnectReason.ClientLeft);
        }
    }

}
