using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        public const char EndpointSeparator = '&';

        public int defaultFactionId;
        public byte[] savedGame; // Compressed game save
        public byte[] semiPersistent; // Compressed semi persistent data
        public Dictionary<int, byte[]> mapData = new(); // Map id to compressed map data

        public Dictionary<int, List<byte[]>> mapCmds = new(); // Map id to serialized cmds list
        public Dictionary<int, List<byte[]>> tmpMapCmds;
        public int lastJoinPointAtWorkTicks = -1;
        public List<byte[]> syncInfos = new();

        public bool CreatingJoinPoint => tmpMapCmds != null;

        public FreezeManager freezeManager;
        public CommandHandler commands;
        public PlayerManager playerManager;
        public LiteNetManager liteNet;
        public IEnumerable<ServerPlayer> PlayingPlayers => playerManager.PlayingPlayers;

        public string hostUsername;
        public int gameTimer;
        public int workTicks;
        public ActionQueue queue = new();
        public ServerSettings settings;

        public volatile bool running;

        private Dictionary<string, ChatCmdHandler> chatCmds = new();

        public int nextUniqueId; // currently unused

        public string rwVersion;
        public string mpVersion;
        public Dictionary<string, DefInfo> defInfos;
        public byte[] serverData;

        public Thread serverThread;
        public event Action<MultiplayerServer> TickEvent;

        public bool ArbiterPlaying => PlayingPlayers.Any(p => p.IsArbiter && p.status == PlayerStatus.Playing);
        public ServerPlayer HostPlayer => PlayingPlayers.First(p => p.IsHost);

        public bool FullyStarted => running && savedGame != null;

        public float serverTimePerTick = 1000.0f / 60.0f;

        public int NetTimer { get; private set; }

        public MultiplayerServer(ServerSettings settings)
        {
            this.settings = settings;

            freezeManager = new FreezeManager(this);
            commands = new CommandHandler(this);
            playerManager = new PlayerManager(this);
            liteNet = new LiteNetManager(this);

            RegisterChatCmd("joinpoint", new ChatCmdJoinPoint());
            RegisterChatCmd("kick", new ChatCmdKick());
        }

        public void Run()
        {
            Stopwatch time = Stopwatch.StartNew();
            double realTime = 0;

            while (running)
            {
                try
                {
                    double elapsed = time.ElapsedMillisDouble();
                    time.Restart();

                    realTime += elapsed;

                    if (realTime > 0)
                    {
                        queue.RunQueue(ServerLog.Error);
                        TickEvent?.Invoke(this);
                        liteNet.Tick();
                        TickNet();
                        freezeManager.Tick();

                        if (!freezeManager.Frozen)
                            gameTimer++;

                        realTime -= serverTimePerTick * 1.05f;
                    }

                    Thread.Sleep(16);
                }
                catch (Exception e)
                {
                    ServerLog.Log($"Exception ticking the server: {e}");
                }
            }

            try
            {
                TryStop();
            }
            catch (Exception e)
            {
                ServerLog.Log($"Exception stopping the server: {e}");
            }
        }

        private void TickNet()
        {
            NetTimer++;

            if (NetTimer % 60 == 0)
                playerManager.SendLatencies();

            if (NetTimer % 30 == 0)
                foreach (var player in PlayingPlayers)
                    player.SendPacket(Packets.Server_KeepAlive, ByteWriter.GetBytes(player.keepAliveId), false);

            if (NetTimer % 2 == 0)
                SendToAll(Packets.Server_TimeControl, ByteWriter.GetBytes(gameTimer, commands.SentCmds, serverTimePerTick), false);

            serverTimePerTick = Math.Max(1000.0f / 60.0f, PlayingPlayers.Max(p => p.frameTime));
        }

        public void TryStop()
        {
            playerManager.OnServerStop();
            liteNet.OnServerStop();

            instance = null;
        }

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

        public ChatCmdHandler GetChatCmdHandler(string cmdName)
        {
            chatCmds.TryGetValue(cmdName, out ChatCmdHandler handler);
            return handler;
        }
    }
}
