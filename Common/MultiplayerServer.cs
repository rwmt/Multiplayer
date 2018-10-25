using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace Multiplayer.Common
{
    public class MultiplayerServer
    {
        static MultiplayerServer()
        {
            MpConnectionState.SetImplementation(ConnectionStateEnum.ServerSteam, typeof(ServerSteamState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ServerJoining, typeof(ServerJoiningState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ServerPlaying, typeof(ServerPlayingState));
        }

        public static MultiplayerServer instance;

        public const int DefaultPort = 30502;

        public int coopFactionId;
        public byte[] savedGame; // Compressed game save
        public Dictionary<int, int> mapTiles = new Dictionary<int, int>(); // World tile to map id
        public Dictionary<int, byte[]> mapData = new Dictionary<int, byte[]>(); // Map id to compressed map data
        public Dictionary<int, List<byte[]>> mapCmds = new Dictionary<int, List<byte[]>>(); // Map id to serialized cmds list
        public List<byte[]> globalCmds = new List<byte[]>(); // Serialized global cmds
        public Dictionary<string, int> playerFactions = new Dictionary<string, int>(); // Username to faction id

        public List<ServerPlayer> players = new List<ServerPlayer>();
        private IEnumerable<ServerPlayer> PlayingPlayers => players.Where(p => p.IsPlaying);

        public int timer;
        public ActionQueue queue = new ActionQueue();
        public string host;
        public string saveFolder;
        public string worldId;
        public IPAddress addr;
        public int port;
        public volatile bool running = true;
        public volatile bool allowLan;

        public int keepAliveId;
        public Stopwatch lastKeepAlive = Stopwatch.StartNew();

        private NetManager server;

        public int nextUniqueId;

        public MultiplayerServer(IPAddress addr, int port = DefaultPort)
        {
            this.addr = addr;
            this.port = port;

            StartNet();
        }

        private void StartNet()
        {
            EventBasedNetListener listener = new EventBasedNetListener();
            server = new NetManager(listener);

            listener.ConnectionRequestEvent += req => req.Accept();

            listener.PeerConnectedEvent += peer =>
            {
                IConnection conn = new MpNetConnection(peer);
                conn.State = ConnectionStateEnum.ServerJoining;
                peer.Tag = conn;
                OnConnected(conn);
            };

            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                IConnection conn = peer.GetConnection();
                OnDisconnected(conn);
            };

            listener.NetworkLatencyUpdateEvent += (peer, ping) =>
            {
                peer.GetConnection().Latency = ping;
            };

            listener.NetworkReceiveEvent += (peer, reader, method) =>
            {
                byte[] data = reader.GetRemainingBytes();
                peer.GetConnection().serverPlayer.HandleReceive(data);
            };
        }

        public void StartListening()
        {
            server.Start(port);
        }

        public void Run()
        {
            Stopwatch time = Stopwatch.StartNew();
            double lag = 0;
            double timePerTick = 1000.0 / 60.0;

            while (running)
            {
                double elapsed = time.ElapsedMillisDouble();
                time.Restart();
                lag += elapsed;

                while (lag >= timePerTick)
                {
                    Tick();
                    lag -= timePerTick;
                }

                Thread.Sleep(10);
            }

            server.Stop();
        }

        public void Tick()
        {
            server.PollEvents();
            queue.RunQueue();

            if (timer % 3 == 0)
                SendToAll(Packets.Server_TimeControl, new object[] { timer });

            if (allowLan && timer % 60 == 0)
                server.SendDiscoveryRequest(Encoding.UTF8.GetBytes("mp-server"), 5100);

            timer++;

            if (timer % 180 == 0)
            {
                UpdatePlayerList();

                keepAliveId++;
                SendToAll(Packets.Server_KeepAlive, new object[] { keepAliveId });
                lastKeepAlive.Restart();
            }
        }

        public void DoAutosave()
        {
            SendCommand(CommandType.Autosave, ScheduledCommand.NoFaction, ScheduledCommand.Global, new byte[0]);

            globalCmds.Clear();
            foreach (int mapId in mapCmds.Keys)
                mapCmds[mapId].Clear();
        }

        public void UpdatePlayerList()
        {
            string[] playerList = PlayingPlayers.
                Select(p => $"{p.Username} ({p.Latency}ms)")
                .ToArray();

            SendToAll(Packets.Server_PlayerList, new object[] { playerList });
        }

        public void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }

        public ServerPlayer OnConnected(IConnection conn)
        {
            if (conn.serverPlayer != null)
                MpLog.Error($"Connection {conn} already has a server player");

            conn.serverPlayer = new ServerPlayer(conn);
            players.Add(conn.serverPlayer);
            MpLog.Log($"New connection: {conn}");

            return conn.serverPlayer;
        }

        public void OnDisconnected(IConnection conn)
        {
            if (conn.State == ConnectionStateEnum.Disconnected) return;

            ServerPlayer player = conn.serverPlayer;
            players.Remove(player);

            if (player.IsPlaying)
            {
                if (!players.Any(p => p.FactionId == player.FactionId))
                {
                    byte[] data = ByteWriter.GetBytes(player.FactionId);
                    SendCommand(CommandType.FactionOffline, ScheduledCommand.NoFaction, ScheduledCommand.Global, data);
                }

                SendToAll(Packets.Server_Notification, new object[] { "Player " + conn.username + " disconnected." });
                UpdatePlayerList();
            }

            conn.State = ConnectionStateEnum.Disconnected;

            MpLog.Log($"Disconnected: " + conn);
        }

        public void SendToAll(Packets id)
        {
            SendToAll(id, new byte[0]);
        }

        public void SendToAll(Packets id, object[] data)
        {
            SendToAll(id, ByteWriter.GetBytes(data));
        }

        public void SendToAll(Packets id, byte[] data)
        {
            foreach (ServerPlayer player in PlayingPlayers)
                player.conn.Send(id, data);
        }

        public ServerPlayer FindPlayer(Predicate<ServerPlayer> match)
        {
            lock (players)
            {
                return players.Find(match);
            }
        }

        public ServerPlayer GetPlayer(string username)
        {
            return FindPlayer(player => player.Username == username);
        }

        public IdBlock NextIdBlock(int blockSize = 30000)
        {
            int blockStart = nextUniqueId;
            nextUniqueId = nextUniqueId + blockSize;
            MpLog.Log("New id block " + blockStart + " of size " + blockSize);

            return new IdBlock(blockStart, blockSize);
        }

        public void SendCommand(CommandType cmd, int factionId, int mapId, byte[] data, string sourcePlayer = null)
        {
            byte[] toSave = new ScheduledCommand(cmd, timer, factionId, mapId, data).Serialize();

            // todo cull target players if not global
            if (mapId < 0)
                globalCmds.Add(toSave);
            else
                mapCmds.GetOrAddNew(mapId).Add(toSave);

            byte[] toSend = toSave.Append(new byte[] { 0 });
            byte[] toSendSource = toSave.Append(new byte[] { 1 });

            foreach (ServerPlayer player in PlayingPlayers)
            {
                player.conn.Send(
                    Packets.Server_Command,
                    sourcePlayer == player.Username ? toSendSource : toSend
                );
            }
        }
    }

    public class ServerPlayer
    {
        public IConnection conn;

        public string Username => conn.username;
        public int Latency => conn.Latency;
        public int FactionId => MultiplayerServer.instance.playerFactions[Username];
        public bool IsPlaying => conn.State == ConnectionStateEnum.ServerPlaying;

        public ServerPlayer(IConnection connection)
        {
            conn = connection;
        }

        public void HandleReceive(byte[] data)
        {
            try
            {
                conn.HandleReceive(data);
            }
            catch (Exception e)
            {
                MpLog.Error($"Error handling packet by {conn}: {e}");
                Disconnect($"Connection error: {e.GetType().Name}");
            }
        }

        public void Disconnect(string reason)
        {
            conn.Send(Packets.Server_DisconnectReason, reason);

            if (conn is MpNetConnection netConn)
                netConn.peer.Flush();

            conn.Close();
            MultiplayerServer.instance.OnDisconnected(conn);
        }
    }

    public class IdBlock
    {
        public int blockStart;
        public int blockSize;
        public int mapId = -1;

        public int current;
        public bool overflowHandled;

        public IdBlock(int blockStart, int blockSize, int mapId = -1)
        {
            this.blockStart = blockStart;
            this.blockSize = blockSize;
            this.mapId = mapId;
        }

        public int NextId()
        {
            // Overflows should be handled by the caller
            current++;
            return blockStart + current;
        }

        public byte[] Serialize()
        {
            ByteWriter writer = new ByteWriter();
            writer.WriteInt32(blockStart);
            writer.WriteInt32(blockSize);
            writer.WriteInt32(mapId);
            writer.WriteInt32(current);

            return writer.GetArray();
        }

        public static IdBlock Deserialize(ByteReader data)
        {
            IdBlock block = new IdBlock(data.ReadInt32(), data.ReadInt32(), data.ReadInt32());
            block.current = data.ReadInt32();
            return block;
        }
    }

    public class ActionQueue
    {
        private Queue<Action> queue = new Queue<Action>();
        private Queue<Action> tempQueue = new Queue<Action>();

        public void RunQueue()
        {
            lock (queue)
            {
                if (queue.Count > 0)
                {
                    foreach (Action a in queue)
                        tempQueue.Enqueue(a);
                    queue.Clear();
                }
            }

            try
            {
                while (tempQueue.Count > 0)
                    tempQueue.Dequeue().Invoke();
            }
            catch (Exception e)
            {
                MpLog.LogLines("Exception while executing action queue", e.ToString());
            }
        }

        public void Enqueue(Action action)
        {
            lock (queue)
                queue.Enqueue(action);
        }
    }
}
