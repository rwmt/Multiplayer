using Harmony;
using LiteNetLib;
using Multiplayer.Common;
using RimWorld;
using Steamworks;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Xml;
using Verse;

namespace Multiplayer.Client
{
    public static class ClientUtil
    {
        public static void TryConnect(IPAddress address, int port)
        {
            EventBasedNetListener listener = new EventBasedNetListener();

            Multiplayer.session = new MultiplayerSession();
            NetManager netClient = new NetManager(listener);
            netClient.Start();
            netClient.ReconnectDelay = 300;
            netClient.MaxConnectAttempts = 8;

            listener.PeerConnectedEvent += peer =>
            {
                IConnection conn = new MpNetConnection(peer);
                conn.username = Multiplayer.username;
                conn.State = ConnectionStateEnum.ClientJoining;
                Multiplayer.session.client = conn;

                ConnectionStatusListeners.TryNotifyAll_Connected();

                MpLog.Log("Client connected");
            };

            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                string reason;

                if (info.AdditionalData.AvailableBytes > 0)
                {
                    reason = info.AdditionalData.GetString();
                }
                else
                {
                    reason = DisconnectReasonString(info.Reason);
                    if (info.SocketErrorCode != SocketError.Success)
                        reason += ": " + info.SocketErrorCode;
                }

                Multiplayer.session.disconnectNetReason = reason;

                ConnectionStatusListeners.TryNotifyAll_Disconnected();

                OnMainThread.StopMultiplayer();
                MpLog.Log("Client disconnected");
            };

            listener.NetworkReceiveEvent += (peer, reader, method) =>
            {
                byte[] data = reader.GetRemainingBytes();
                Multiplayer.HandleReceive(data, method == DeliveryMethod.ReliableOrdered);
            };

            Multiplayer.session.netClient = netClient;
            netClient.Connect(address.ToString(), port, "");
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

        public static void HostServer(ServerSettings settings, bool replay)
        {
            Log.Message($"Starting a server at {settings.address}:{settings.port}");

            MultiplayerWorldComp comp = Find.World.GetComponent<MultiplayerWorldComp>();
            Faction dummyFaction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.loadID == -1);

            if (dummyFaction == null)
            {
                dummyFaction = new Faction() { loadID = -1, def = Multiplayer.DummyFactionDef };
                dummyFaction.Name = "Multiplayer dummy faction";

                foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
                    dummyFaction.TryMakeInitialRelationsWith(other);

                Find.FactionManager.Add(dummyFaction);

                comp.factionData[dummyFaction.loadID] = FactionWorldData.New(dummyFaction.loadID);
            }

            Faction.OfPlayer.Name = $"{Multiplayer.username}'s faction";
            comp.factionData[Faction.OfPlayer.loadID] = FactionWorldData.FromCurrent();

            var session = Multiplayer.session = new MultiplayerSession();
            session.myFactionId = Faction.OfPlayer.loadID;
            session.localSettings = settings;
            session.gameName = settings.gameName;

            var localServer = new MultiplayerServer(settings);
            localServer.hostUsername = Multiplayer.username;
            localServer.coopFactionId = Faction.OfPlayer.loadID;

            if (replay)
                localServer.timer = TickPatch.Timer;

            MultiplayerServer.instance = localServer;
            session.localServer = localServer;

            Multiplayer.game = new MultiplayerGame
            {
                dummyFaction = dummyFaction,
                worldComp = comp
            };

            if (!replay)
            {
                localServer.nextUniqueId = GetMaxUniqueId();
                comp.globalIdBlock = localServer.NextIdBlock(1_000_000_000);

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

            Find.PlaySettings.usePlanetDayNightSystem = false;

            Multiplayer.RealPlayerFaction = Faction.OfPlayer;
            localServer.playerFactions[Multiplayer.username] = Faction.OfPlayer.loadID;

            SetupLocalClient();

            Find.MainTabsRoot.EscapeCurrentTab(false);

            LongEventHandler.QueueLongEvent(() =>
            {
                Multiplayer.CacheGameData(Multiplayer.SaveAndReload());
                Multiplayer.SendCurrentGameData(false);

                localServer.StartListening();

                session.serverThread = new Thread(localServer.Run)
                {
                    Name = "Local server thread"
                };
                session.serverThread.Start();

                string text = "Server started.";
                if (settings.direct || settings.lan)
                    text += $" Listening at {settings.address}:{localServer.LocalPort}";

                Messages.Message(text, MessageTypeDefOf.SilentInput, false);
                Log.Message(text);
            }, "MpSaving", false, null);
        }

        private static void SetupLocalClient()
        {
            if (Multiplayer.session.localSettings.arbiter)
                StartArbiter();

            LocalClientConnection localClient = new LocalClientConnection(Multiplayer.username);
            LocalServerConnection localServerConn = new LocalServerConnection(Multiplayer.username);

            localServerConn.client = localClient;
            localClient.server = localServerConn;

            localClient.State = ConnectionStateEnum.ClientPlaying;
            localServerConn.State = ConnectionStateEnum.ServerPlaying;

            var serverPlayer = Multiplayer.LocalServer.OnConnected(localServerConn);
            serverPlayer.status = PlayerStatus.Playing;
            serverPlayer.SendPlayerList();

            Multiplayer.session.client = localClient;
        }

        private static void StartArbiter()
        {
            Multiplayer.session.AddMsg("The Arbiter instance is starting...");
            Multiplayer.session.hasUnread = false;

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

    public class LocalClientConnection : IConnection
    {
        public LocalServerConnection server;

        public override int Latency { get => 0; set { } }

        public LocalClientConnection(string username)
        {
            this.username = username;
        }

        public override void SendRaw(byte[] raw, bool reliable)
        {
            Multiplayer.LocalServer.Enqueue(() =>
            {
                try
                {
                    server.HandleReceive(raw, reliable);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception handling packet by {this}: {e}");
                }
            });
        }

        public override void Close()
        {
        }

        public override string ToString()
        {
            return "LocalClientConn";
        }
    }

    public class LocalServerConnection : IConnection
    {
        public LocalClientConnection client;

        public override int Latency { get => 0; set { } }

        public LocalServerConnection(string username)
        {
            this.username = username;
        }

        public override void SendRaw(byte[] raw, bool reliable)
        {
            OnMainThread.Enqueue(() =>
            {
                try
                {
                    client.HandleReceive(raw, reliable);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception handling packet by {this}: {e}");
                }
            });
        }

        public override void Close()
        {
        }

        public override string ToString()
        {
            return "LocalServerConn";
        }
    }

    public class SteamConnection : IConnection
    {
        public readonly CSteamID remoteId;

        public SteamConnection(CSteamID remoteId)
        {
            this.remoteId = remoteId;
        }

        public override void SendRaw(byte[] raw, bool reliable)
        {
            SteamNetworking.SendP2PPacket(remoteId, raw, (uint)raw.Length, reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliable, reliable ? 0 : 1);
        }

        public override void Close()
        {
            SteamNetworking.CloseP2PSessionWithUser(remoteId);
        }

        public override string ToString()
        {
            return $"SteamP2P ({remoteId}) ({username})";
        }
    }

}
