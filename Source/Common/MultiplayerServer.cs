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
        public const int MaxUsernameLength = 15;
        public const int MinUsernameLength = 3;

        public int coopFactionId;
        public byte[] savedGame; // Compressed game save
        public byte[] semiPersistent; // Compressed semi persistent data
        public Dictionary<int, byte[]> mapData = new(); // Map id to compressed map data

        public Dictionary<int, List<byte[]>> mapCmds = new(); // Map id to serialized cmds list
        public Dictionary<int, List<byte[]>> tmpMapCmds;
        public int lastJoinPointAtWorkTicks = -1;

        // todo remove entries
        public Dictionary<string, int> playerFactions = new(); // Username to faction id

        public PauseManager pauseManager;
        public CommandHandler commands;
        public PlayerManager playerManager;
        public IEnumerable<ServerPlayer> PlayingPlayers => playerManager.PlayingPlayers;

        public string hostUsername;
        public int gameTimer;
        public int workTicks;
        public ActionQueue queue = new();
        public ServerSettings settings;

        public volatile bool running = true;

        private Dictionary<string, ChatCmdHandler> chatCmds = new();

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
        public ServerPlayer HostPlayer => PlayingPlayers.First(p => p.IsHost);

        public event Action<MultiplayerServer> NetTick;

        public MultiplayerServer(ServerSettings settings)
        {
            this.settings = settings;

            pauseManager = new PauseManager(this);
            commands = new CommandHandler(this);
            playerManager = new PlayerManager(this);

            RegisterChatCmd("autosave", new ChatCmdAutosave());
            RegisterChatCmd("joinpoint", new ChatCmdJoinPoint());
            RegisterChatCmd("kick", new ChatCmdKick());

            if (settings.bindAddress != null)
                netManager = CreateNetManager();

            if (settings.lanAddress != null)
                lanManager = CreateNetManager();
        }

        private NetManager CreateNetManager()
        {
            return new NetManager(new MpServerNetListener(this, false))
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
            arbiter = new NetManager(new MpServerNetListener(this, true)) { IPv6Enabled = IPv6Mode.Disabled };
            arbiter.Start(IPAddress.Loopback, IPAddress.IPv6Any, 0);
        }

        public void Run()
        {
            Stopwatch time = Stopwatch.StartNew();
            double lag = 0;
            const double timePerTick = 1000.0 / 60.0;

            while (running)
            {
                try
                {
                    double elapsed = time.ElapsedMillisDouble();
                    time.Restart();

                    lag += elapsed;
                    lag = Math.Min(lag, 1000);

                    while (lag >= timePerTick)
                    {
                        TickNet();
                        if (!pauseManager.Paused && PlayingPlayers.Any(p => p.KeepsServerAwake))
                            Tick();
                        lag -= timePerTick;
                    }

                    Thread.Sleep(16);
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
            playerManager.OnServerStop();

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

            if (netTimer % 60 == 0)
                playerManager.SendLatencies();

            if (netTimer % 30 == 0)
                foreach (var player in PlayingPlayers)
                    player.SendPacket(Packets.Server_KeepAlive, ByteWriter.GetBytes(player.keepAliveId), false);

            pauseManager.Tick();

            if (netTimer % 2 == 0)
                SendToAll(Packets.Server_TimeControl, ByteWriter.GetBytes(gameTimer, commands.NextCmdId), false);
        }

        public void Tick()
        {
            gameTimer++;
        }

        public bool CreatingJoinPoint => tmpMapCmds != null;

        public bool TryStartJoinPointCreation(bool force = false)
        {
            if (!force && workTicks - lastJoinPointAtWorkTicks < 30)
                return false;

            if (CreatingJoinPoint)
                return false;

            SendChat("Creating a join point...");

            commands.Send(CommandType.CreateJoinPoint, ScheduledCommand.NoFaction, ScheduledCommand.Global, new byte[0]);
            tmpMapCmds = new Dictionary<int, List<byte[]>>();

            return true;
        }

        public void EndJoinPointCreation()
        {
            mapCmds = tmpMapCmds;
            tmpMapCmds = null;
            lastJoinPointAtWorkTicks = workTicks;

            foreach (var playerId in playerManager.playersWaitingForWorldData)
                if (GetPlayer(playerId)?.conn.StateObj is ServerJoiningState state)
                    state.SendWorldData();

            playerManager.playersWaitingForWorldData.Clear();
        }

        public void Enqueue(Action action)
        {
            queue.Enqueue(action);
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
            return playerManager.GetPlayer(username);
        }

        public ServerPlayer GetPlayer(int id)
        {
            return playerManager.GetPlayer(id);
        }

        public IdBlock NextIdBlock(int blockSize = 30000)
        {
            int blockStart = nextUniqueId;
            nextUniqueId += blockSize;
            ServerLog.Log($"New id block {blockStart} of size {blockSize}");

            return new IdBlock(blockStart, blockSize);
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
