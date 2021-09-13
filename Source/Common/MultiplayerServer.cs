using LiteNetLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Verse;

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
        public const int MaxUsernameLength = 15;
        public const int MinUsernameLength = 3;

        public int coopFactionId;
        public byte[] savedGame; // Compressed game save
        public byte[] semiPersistent; // Compressed semi persistent data
        public Dictionary<int, byte[]> mapData = new Dictionary<int, byte[]>(); // Map id to compressed map data

        public Dictionary<int, List<byte[]>> mapCmds = new Dictionary<int, List<byte[]>>(); // Map id to serialized cmds list
        public Dictionary<int, List<byte[]>> tmpMapCmds;

        // todo remove entries
        public Dictionary<string, int> playerFactions = new Dictionary<string, int>(); // Username to faction id

        public List<ServerPlayer> players = new List<ServerPlayer>();
        public IEnumerable<ServerPlayer> PlayingPlayers => players.Where(p => p.IsPlaying);

        public string hostUsername;
        public int gameTimer;
        public bool paused;
        public ActionQueue queue = new ActionQueue();
        public ServerSettings settings;

        public int nextCmdId;

        public volatile bool running = true;

        private Dictionary<string, ChatCmdHandler> chatCmds = new Dictionary<string, ChatCmdHandler>();
        public HashSet<int> debugOnlySyncCmds = new HashSet<int>();
        public HashSet<int> hostOnlySyncCmds = new HashSet<int>();

        public int keepAliveId;
        public Stopwatch lastKeepAlive = Stopwatch.StartNew();

        private Dictionary<object, long> lastConnection = new Dictionary<object, long>();
        private Stopwatch clock = Stopwatch.StartNew();

        public NetManager netManager;
        public NetManager lanManager;
        private NetManager arbiter;

        public int nextUniqueId; // currently unused

        public string rwVersion;
        public string mpVersion;
        public Dictionary<string, DefInfo> defInfos;
        public byte[] serverData;

        public int NetPort => netManager.LocalPort;
        public int LanPort => lanManager.LocalPort;
        public int ArbiterPort => arbiter.LocalPort;

        public bool ArbiterPlaying => PlayingPlayers.Any(p => p.IsArbiter && p.status == PlayerStatus.Playing);

        public event Action<MultiplayerServer> NetTick;

        private float autosaveCountdown;

        public MultiplayerServer(ServerSettings settings)
        {
            this.settings = settings;

            RegisterChatCmd("autosave", new ChatCmdAutosave());
            RegisterChatCmd("kick", new ChatCmdKick());

            if (settings.bindAddress != null)
                netManager = NewNetManager();

            if (settings.lanAddress != null)
                lanManager = NewNetManager();

            autosaveCountdown = settings.autosaveInterval * 60 * 60;
        }

        private NetManager NewNetManager()
        {
            return new NetManager(new MpNetListener(this, false))
            {
                EnableStatistics = true,
                IPv6Enabled = IPv6Mode.Disabled
            };
        }

        public bool? StartListeningNet()
        {
            return netManager?.Start(IPAddress.Parse(settings.bindAddress), IPAddress.IPv6Any, settings.bindPort);
        }

        public bool? StartListeningLan()
        {
            return lanManager?.Start(IPAddress.Parse(settings.lanAddress), IPAddress.IPv6Any, 0);
        }

        public void SetupArbiterConnection()
        {
            arbiter = new NetManager(new MpNetListener(this, true)) { IPv6Enabled = IPv6Mode.Disabled };
            arbiter.Start(IPAddress.Loopback, IPAddress.IPv6Any, 0);
        }

        public void Run()
        {
            Stopwatch time = Stopwatch.StartNew();
            double lag = 0;
            double timePerTick = 1000.0 / 60.0;

            while (running)
            {
                try
                {
                    double elapsed = time.ElapsedMillisDouble();
                    time.Restart();
                    lag += elapsed;

                    while (lag >= timePerTick)
                    {
                        TickNet();
                        if (!paused && PlayingPlayers.Any(p => !p.IsArbiter && p.status == PlayerStatus.Playing))
                            Tick();
                        lag -= timePerTick;
                    }

                    Thread.Sleep(10);
                }
                catch (Exception e)
                {
                    ServerLog.Log($"Exception ticking the server: {e}");
                }
            }

            Stop();
        }

        private void Stop()
        {
            foreach (var player in players)
                player.conn.Close(MpDisconnectReason.ServerClosed);

            netManager?.Stop();
            lanManager?.Stop();
            arbiter?.Stop();

            instance = null;
        }

        public int netTimer;

        public void TickNet()
        {
            netManager?.PollEvents();
            lanManager?.PollEvents();
            arbiter?.PollEvents();

            NetTick?.Invoke(this);

            queue.RunQueue(ServerLog.Error);

            if (lanManager != null && netTimer % 60 == 0)
                lanManager.SendBroadcast(Encoding.UTF8.GetBytes("mp-server"), 5100);

            netTimer++;

            if (netTimer % 180 == 0)
            {
                SendLatencies();

                keepAliveId++;
                SendToAll(Packets.Server_KeepAlive, new object[] { keepAliveId });
                lastKeepAlive.Restart();
            }
        }

        public void Tick()
        {
            if (gameTimer % 2 == 0)
                SendToAll(Packets.Server_TimeControl, ByteWriter.GetBytes(gameTimer, nextCmdId), false);

            gameTimer++;

            // Days autosaving is handled by host's client
            if (settings.autosaveUnit == AutosaveUnit.Days || settings.autosaveInterval <= 0)
                return;

            autosaveCountdown -= 1;

            if (autosaveCountdown <= 0)
                DoAutosave();
        }

        private void SendLatencies()
        {
            var writer = new ByteWriter();
            writer.WriteByte((byte)PlayerListAction.Latencies);

            writer.WriteInt32(PlayingPlayers.Count());
            foreach (var player in PlayingPlayers)
            {
                writer.WriteInt32(player.Latency);
                writer.WriteInt32(player.ticksBehind);
            }

            SendToAll(Packets.Server_PlayerList, writer.ToArray());
        }

        public bool DoAutosave(string saveName = "")
        {
            if (tmpMapCmds != null)
                return false;

            ByteWriter writer = new ByteWriter();
            writer.WriteString(saveName);
            SendCommand(CommandType.Autosave, ScheduledCommand.NoFaction, ScheduledCommand.Global, writer.ToArray());
            tmpMapCmds = new Dictionary<int, List<byte[]>>();

            SendChat("Autosaving...");

            autosaveCountdown = settings.autosaveInterval * 60 * 60;
            return true;
        }

        public void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }

        const long ThrottleMillis = 1000;

        // id can be an IPAddress or CSteamID
        public MpDisconnectReason? OnPreConnect(object id)
        {
            if (id is IPAddress addr && IPAddress.IsLoopback(addr))
                return null;

            if (settings.maxPlayers > 0 &&
                players.Count(p => !p.IsArbiter) >= settings.maxPlayers)
                return MpDisconnectReason.ServerFull;

            if (lastConnection.TryGetValue(id, out var last) && clock.ElapsedMilliseconds - last < ThrottleMillis)
                return MpDisconnectReason.Throttled;

            lastConnection[id] = clock.ElapsedMilliseconds;

            return null;
        }

        private int nextPlayerId;

        public ServerPlayer OnConnected(ConnectionBase conn)
        {
            if (conn.serverPlayer != null)
                ServerLog.Error($"Connection {conn} already has a server player");

            conn.serverPlayer = new ServerPlayer(nextPlayerId++, conn);
            players.Add(conn.serverPlayer);
            ServerLog.Log($"New connection: {conn}");

            return conn.serverPlayer;
        }

        public void OnDisconnected(ConnectionBase conn, MpDisconnectReason reason)
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

                SendNotification("MpPlayerDisconnected", conn.username);
                SendChat($"{conn.username} has left.");

                SendToAll(Packets.Server_PlayerList, new object[] { (byte)PlayerListAction.Remove, player.id });
            }

            conn.State = ConnectionStateEnum.Disconnected;

            ServerLog.Log($"Disconnected ({reason}): {conn}");
        }

        public void SendToAll(Packets id)
        {
            SendToAll(id, new byte[0]);
        }

        public void SendToAll(Packets id, object[] data)
        {
            SendToAll(id, ByteWriter.GetBytes(data));
        }

        public void SendToAll(Packets id, byte[] data, bool reliable = true, ServerPlayer excluding = null)
        {
            foreach (ServerPlayer player in PlayingPlayers)
                if (player != excluding)
                    player.conn.Send(id, data, reliable);
        }

        public ServerPlayer GetPlayer(string username)
        {
            return players.Find(player => player.Username == username);
        }

        public IdBlock NextIdBlock(int blockSize = 30000)
        {
            int blockStart = nextUniqueId;
            nextUniqueId = nextUniqueId + blockSize;
            ServerLog.Log($"New id block {blockStart} of size {blockSize}");

            return new IdBlock(blockStart, blockSize);
        }

        public void SendCommand(CommandType cmd, int factionId, int mapId, byte[] data, ServerPlayer sourcePlayer = null)
        {
            if (sourcePlayer != null)
            {
                bool debugCmd =
                    cmd == CommandType.DebugTools ||
                    cmd == CommandType.Sync && debugOnlySyncCmds.Contains(BitConverter.ToInt32(data, 0));

                if (!settings.debugMode && debugCmd)
                    return;

                bool hostOnly = cmd == CommandType.Sync && hostOnlySyncCmds.Contains(BitConverter.ToInt32(data, 0));
                if (!sourcePlayer.IsHost && hostOnly)
                    return;
            }

            byte[] toSave = new ScheduledCommand(cmd, gameTimer, factionId, mapId, data).Serialize();

            // todo cull target players if not global
            mapCmds.GetOrAddNew(mapId).Add(toSave);
            tmpMapCmds?.GetOrAddNew(mapId).Add(toSave);

            byte[] toSend = toSave.Append(new byte[] { 0 });
            byte[] toSendSource = toSave.Append(new byte[] { 1 });

            foreach (var player in PlayingPlayers)
            {
                player.conn.Send(
                    Packets.Server_Command,
                    sourcePlayer == player ? toSendSource : toSend
                );
            }

            nextCmdId++;
        }

        public void SendChat(string msg)
        {
            SendToAll(Packets.Server_Chat, new[] { msg });
        }

        public void SendNotification(string key, params string[] args)
        {
            SendToAll(Packets.Server_Notification, new object[] { key, args });
        }

        public void RegisterChatCmd(string cmdName, ChatCmdHandler handler)
        {
            chatCmds[cmdName] = handler;
        }

        public ChatCmdHandler GetCmdHandler(string cmdName)
        {
            chatCmds.TryGetValue(cmdName, out ChatCmdHandler handler);
            return handler;
        }
    }
}
