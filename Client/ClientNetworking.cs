using Harmony;
using LiteNetLib;
using Multiplayer.Common;
using RimWorld;
using Steamworks;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
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

                ConnectionStatusListeners.All.Do(a => a.Connected());

                MpLog.Log("Client connected");
            };

            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                string reason = DisconnectReasonString(info.Reason);
                if (info.SocketErrorCode != SocketError.Success)
                    reason += ": " + info.SocketErrorCode;

                Multiplayer.session.disconnectNetReason = reason;

                ConnectionStatusListeners.All.Do(a => a.Disconnected());

                OnMainThread.StopMultiplayer();
                MpLog.Log("Client disconnected");
            };

            listener.NetworkReceiveEvent += (peer, reader, method) =>
            {
                byte[] data = reader.GetRemainingBytes();
                Multiplayer.HandleReceive(data);
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
                case DisconnectReason.SocketSendError: return "Socket send error";
                case DisconnectReason.SocketReceiveError: return "Socket receive error";
                default: return "Disconnected";
            }
        }

        public static void HostServer(IPAddress addr, int port)
        {
            MpLog.Log("Starting a server");

            MultiplayerWorldComp comp = Find.World.GetComponent<MultiplayerWorldComp>();
            Faction dummyFaction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.loadID == -1);

            if (dummyFaction == null)
            {
                dummyFaction = new Faction() { loadID = -1, def = Multiplayer.DummyFactionDef };

                foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
                    dummyFaction.TryMakeInitialRelationsWith(other);

                Find.FactionManager.Add(dummyFaction);
            }

            Faction.OfPlayer.Name = $"{Multiplayer.username}'s faction";

            comp.factionData[Faction.OfPlayer.loadID] = FactionWorldData.FromCurrent();
            comp.factionData[dummyFaction.loadID] = FactionWorldData.New(dummyFaction.loadID);

            MultiplayerSession session = Multiplayer.session = new MultiplayerSession();
            MultiplayerServer localServer = new MultiplayerServer(addr, port);
            localServer.hostUsername = Multiplayer.username;
            localServer.allowLan = true;
            localServer.coopFactionId = Faction.OfPlayer.loadID;
            MultiplayerServer.instance = localServer;
            session.localServer = localServer;
            session.myFactionId = Faction.OfPlayer.loadID;

            Multiplayer.game = new MultiplayerGame
            {
                dummyFaction = dummyFaction,
                worldComp = comp
            };

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

            Find.PlaySettings.usePlanetDayNightSystem = false;

            Multiplayer.RealPlayerFaction = Faction.OfPlayer;
            localServer.playerFactions[Multiplayer.username] = Faction.OfPlayer.loadID;

            SetupLocalClient();

            Find.MainTabsRoot.EscapeCurrentTab(false);
            session.chat = new ChatWindow();

            LongEventHandler.QueueLongEvent(() =>
            {
                Multiplayer.CacheAndSendGameData(Multiplayer.SaveAndReload());

                localServer.StartListening();

                session.serverThread = new Thread(localServer.Run)
                {
                    Name = "Local server thread"
                };
                session.serverThread.Start();

                Multiplayer.LocalServer.UpdatePlayerList();

                Messages.Message("Server started. Listening at " + addr.ToString() + ":" + MultiplayerServer.DefaultPort, MessageTypeDefOf.SilentInput, false);
            }, "MpSaving", false, null);
        }

        private static void SetupLocalClient()
        {
            LocalClientConnection localClient = new LocalClientConnection(Multiplayer.username);
            LocalServerConnection localServerConn = new LocalServerConnection(Multiplayer.username);

            localServerConn.client = localClient;
            localClient.server = localServerConn;

            localClient.State = ConnectionStateEnum.ClientPlaying;
            localServerConn.State = ConnectionStateEnum.ServerPlaying;

            ServerPlayer serverPlayer = new ServerPlayer(localServerConn);
            localServerConn.serverPlayer = serverPlayer;

            Multiplayer.LocalServer.players.Add(serverPlayer);

            Multiplayer.session.client = localClient;
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

        public override void SendRaw(byte[] raw)
        {
            OnMainThread.Enqueue(() =>
            {
                try
                {
                    server.HandleReceive(raw);
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

        public override void SendRaw(byte[] raw)
        {
            OnMainThread.Enqueue(() =>
            {
                try
                {
                    client.HandleReceive(raw);
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

        public override void SendRaw(byte[] raw)
        {
            SteamNetworking.SendP2PPacket(remoteId, raw, (uint)raw.Length, EP2PSend.k_EP2PSendReliable);
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
