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
            MpConnectionState.RegisterState(typeof(ServerSteamState));
            MpConnectionState.RegisterState(typeof(ServerJoiningState));
            MpConnectionState.RegisterState(typeof(ServerPlayingState));
        }

        public static MultiplayerServer instance;

        public const int DefaultPort = 30502;

        public byte[] savedGame; // Compressed game save
        public Dictionary<int, int> mapTiles = new Dictionary<int, int>(); // World tile to map id
        public Dictionary<int, byte[]> mapData = new Dictionary<int, byte[]>(); // Map id to compressed map data
        public Dictionary<int, List<byte[]>> mapCmds = new Dictionary<int, List<byte[]>>(); // Map id to serialized cmds list
        public List<byte[]> globalCmds = new List<byte[]>(); // Serialized global cmds
        public Dictionary<string, int> playerFactions = new Dictionary<string, int>(); // Username to faction id

        public List<ServerPlayer> players = new List<ServerPlayer>();
        public IEnumerable<ServerPlayer> PlayingPlayers => players.Where(p => p.IsPlaying);

        public int timer;
        public ActionQueue queue = new ActionQueue();
        public string host;
        public string saveFolder;
        public string worldId;
        public IPAddress addr;
        public int port;
        public volatile bool running = true;
        public bool allowLan;

        public int keepAliveId;
        public Stopwatch lastKeepAlive = Stopwatch.StartNew();

        private NetManager server;

        public int nextUniqueId;

        public MultiplayerServer(IPAddress addr, int port = DefaultPort)
        {
            this.addr = addr;
            this.port = port;

            EventBasedNetListener listener = new EventBasedNetListener();
            server = new NetManager(listener, 32, "");

            listener.PeerConnectedEvent += peer => Enqueue(() => NetPeerConnected(peer));
            listener.PeerDisconnectedEvent += (peer, info) => Enqueue(() => NetPeerDisconnected(peer, info));
            listener.NetworkLatencyUpdateEvent += (peer, ping) => Enqueue(() => NetPeerUpdateLatency(peer, ping));

            listener.NetworkReceiveEvent += (peer, reader) =>
            {
                byte[] data = reader.Data;
                Enqueue(() => MessageReceived(peer, data));
            };
        }

        public void Run()
        {
            Stopwatch time = Stopwatch.StartNew();
            double lag = 0;
            double timePerTick = 1000.0 / 60.0;

            while (running)
            {
                double elapsed = time.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
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
                SendToAll(Packets.SERVER_TIME_CONTROL, new object[] { timer });

            if (allowLan && timer % 60 == 0)
                server.SendDiscoveryRequest(Encoding.UTF8.GetBytes("mp-server"), 5100);

            timer++;

            if (timer % 180 == 0)
            {
                UpdatePlayerList();

                keepAliveId++;
                SendToAll(Packets.SERVER_KEEP_ALIVE, new object[] { keepAliveId });
                lastKeepAlive.Restart();
            }
        }

        public void StartListening()
        {
            server.Start(port);
        }

        public void DoAutosave()
        {
            Enqueue(() =>
            {
                SendCommand(CommandType.AUTOSAVE, ScheduledCommand.NoFaction, ScheduledCommand.Global, new byte[0]);

                globalCmds.Clear();
                foreach (int mapId in mapCmds.Keys)
                    mapCmds[mapId].Clear();
            });
        }

        public void UpdatePlayerList()
        {
            string[] playerList = PlayingPlayers.
                Select(p => $"{p.Username} ({p.Latency}ms)")
                .ToArray();

            SendToAll(Packets.SERVER_PLAYER_LIST, new object[] { playerList });
        }

        public void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }

        private void NetPeerUpdateLatency(NetPeer peer, int ping)
        {
            peer.GetConnection().Latency = ping;
        }

        private void NetPeerConnected(NetPeer peer)
        {
            IConnection conn = new MpNetConnection(peer);
            conn.State = new ServerJoiningState(conn);
            peer.Tag = conn;
            OnConnected(conn);
        }

        public void OnConnected(IConnection conn)
        {
            players.Add(new ServerPlayer(conn));
            MpLog.Log($"New connection: {conn}");
        }

        private void NetPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            IConnection conn = peer.GetConnection();
            OnDisconnected(conn);
        }

        public void OnDisconnected(IConnection conn)
        {
            ServerPlayer player = GetPlayer(conn);
            if (player == null) return;

            players.Remove(player);

            if (player.IsPlaying)
            {
                if (!players.Any(p => p.FactionId == player.FactionId))
                {
                    byte[] data = ByteWriter.GetBytes(player.FactionId);
                    SendCommand(CommandType.FACTION_OFFLINE, ScheduledCommand.NoFaction, ScheduledCommand.Global, data);
                }

                SendToAll(Packets.SERVER_NOTIFICATION, new object[] { "Player " + conn.Username + " disconnected." });
                UpdatePlayerList();
            }

            MpLog.Log($"Disconnected: " + conn);
        }

        public void MessageReceived(NetPeer peer, byte[] data)
        {
            IConnection conn = peer.GetConnection();
            if (GetPlayer(conn) != null)
                conn.HandleReceive(data);
        }

        public void UpdateLatency(NetPeer peer, int latency)
        {
            IConnection conn = peer.GetConnection();
            conn.Latency = latency;
        }

        public void Disconnect(IConnection conn, string reason)
        {
            conn.Send(Packets.SERVER_DISCONNECT_REASON, reason);
            conn.Close();
            OnDisconnected(conn);
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
                player.connection.Send(id, data);
        }

        public ServerPlayer GetPlayer(string username)
        {
            return players.Find(player => player.Username == username);
        }

        public ServerPlayer GetPlayer(IConnection conn)
        {
            return players.Find(player => player.connection == conn);
        }

        public IdBlock NextIdBlock()
        {
            int blockSize = 30000;
            int blockStart = nextUniqueId;
            nextUniqueId = nextUniqueId + blockSize;
            MpLog.Log("New id block " + blockStart + " of size " + blockSize);

            return new IdBlock(blockStart, blockSize);
        }

        public void SendCommand(CommandType cmd, int factionId, int mapId, byte[] data, string sourcePlayer = null)
        {
            byte[] toSave = new ScheduledCommand(cmd, timer, factionId, mapId, data).GetBytes();

            // todo cull target players if not global
            if (mapId < 0)
                globalCmds.Add(toSave);
            else
                mapCmds.GetOrAddDefault(mapId).Add(toSave);

            byte[] toSend = toSave.Append(new byte[] { 0 });
            byte[] toSendSource = toSave.Append(new byte[] { 1 });

            foreach (ServerPlayer player in PlayingPlayers)
            {
                player.connection.Send(
                    Packets.SERVER_COMMAND,
                    sourcePlayer == player.Username ? toSendSource : toSend
                );
            }
        }
    }

    public class ServerPlayer
    {
        public IConnection connection;

        public string Username => connection.Username;
        public int Latency => connection.Latency;
        public int FactionId => MultiplayerServer.instance.playerFactions[Username];
        public bool IsPlaying => connection?.State?.GetType() == typeof(ServerPlayingState);

        public ServerPlayer(IConnection connection)
        {
            this.connection = connection;
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
