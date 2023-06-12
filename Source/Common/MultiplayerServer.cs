using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Multiplayer.Common
{
    public class MultiplayerServer
    {
        static MultiplayerServer()
        {
            MpConnectionState.SetImplementation(ConnectionStateEnum.ServerSteam, typeof(ServerSteamState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ServerJoining, typeof(ServerJoiningState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ServerLoading, typeof(ServerLoadingState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ServerPlaying, typeof(ServerPlayingState));
        }

        public static MultiplayerServer? instance;

        public const int DefaultPort = 30502;
        public const int MaxUsernameLength = 15;
        public const int MinUsernameLength = 3;
        public const char EndpointSeparator = '&';

        public static readonly Regex UsernamePattern = new(@"^[a-zA-Z0-9_]+$");

        public WorldData worldData;
        public FreezeManager freezeManager;
        public CommandHandler commands;
        public PlayerManager playerManager;
        public LiteNetManager liteNet;
        public IEnumerable<ServerPlayer> JoinedPlayers => playerManager.JoinedPlayers;
        public IEnumerable<ServerPlayer> PlayingPlayers => playerManager.PlayingPlayers;
        public IEnumerable<ServerPlayer> PlayingIngamePlayers => playerManager.PlayingPlayers.Where(p => p.status == PlayerStatus.Playing);

        public string? hostUsername;
        public int gameTimer;
        public int workTicks;
        public ActionQueue queue = new();
        public ServerSettings settings;

        public ServerInitData? initData;
        public TaskCompletionSource<ServerInitData?> initDataSource = new();
        public InitDataState initDataState = InitDataState.Waiting;

        private Dictionary<string, ChatCmdHandler> chatCmdHandlers = new();

        public int nextUniqueId; // currently unused

        public volatile bool running;
        public event Action<MultiplayerServer>? TickEvent;

        public bool ArbiterPlaying => PlayingPlayers.Any(p => p.IsArbiter && p.status == PlayerStatus.Playing);
        public ServerPlayer HostPlayer => PlayingPlayers.First(p => p.IsHost);

        public bool FullyStarted => running && worldData.savedGame != null;

        public const float StandardTimePerTick = 1000.0f / 60.0f;

        public float serverTimePerTick = StandardTimePerTick;
        private int sentCmdsSnapshot;

        public int NetTimer { get; private set; }

        public MultiplayerServer(ServerSettings settings)
        {
            this.settings = settings;

            worldData = new WorldData(this);
            freezeManager = new FreezeManager(this);
            commands = new CommandHandler(this);
            playerManager = new PlayerManager(this);
            liteNet = new LiteNetManager(this);

            RegisterChatCmd("joinpoint", new ChatCmdJoinPoint());
            RegisterChatCmd("kick", new ChatCmdKick());
            RegisterChatCmd("stop", new ChatCmdStop());

            initDataSource.SetResult(null);
        }

        public void Run()
        {
            ServerLog.Detail("Server started");

            Stopwatch time = Stopwatch.StartNew();
            Stopwatch tickTime = Stopwatch.StartNew();
            double realTime = 0;

            while (running)
            {
                try
                {
                    double elapsed = time.ElapsedMillisDouble();
                    time.Restart();
                    realTime += elapsed;

                    tickTime.Restart();

                    freezeManager.Tick();
                    queue.RunQueue(ServerLog.Error);
                    TickEvent?.Invoke(this);
                    liteNet.Tick();
                    TickNet();

                    int ticked = 0;
                    while (realTime > 0 && ticked < 2)
                    {
                        if (!freezeManager.Frozen &&
                            PlayingPlayers.Any(p => p.ExtrapolatedTicksBehind < 40) &&
                            !PlayingIngamePlayers.Any(p => p.ExtrapolatedTicksBehind > 90))
                        {
                            gameTimer++;
                            sentCmdsSnapshot = commands.SentCmds;
                        }

                        // Run up to three times slower depending on max ticksBehind
                        var slowdown = Math.Min(
                            PlayingIngamePlayers.MaxOrZero(p => p.ticksBehind) / 60f,
                            2f
                        );
                        realTime -= serverTimePerTick * (1f + slowdown);

                        ticked++;
                    }

                    if (realTime > 0)
                        realTime = 0f;

#if DEBUG
                    if (tickTime.ElapsedMillisDouble() > 15f)
                        ServerLog.Log($"Server tick took {tickTime.ElapsedMillisDouble()}ms");
#endif

                    // On Windows, the clock ticks 64 times a second and sleep durations too close to a multiple of 15.625ms
                    // tend to be rounded up so we sleep for a bit less
                    int sleepFor = (int)Math.Floor((1000 / 30f - tickTime.ElapsedMillisDouble()) * 0.9f);
                    if (sleepFor > 0)
                        Thread.Sleep(sleepFor);
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

            if (NetTimer % 30 == 0)
                playerManager.SendLatencies();

            if (NetTimer % 6 == 0)
                foreach (var player in JoinedPlayers)
                    player.SendPacket(Packets.Server_KeepAlive, ByteWriter.GetBytes(player.keepAliveId), false);

            SendToPlaying(Packets.Server_TimeControl, ByteWriter.GetBytes(gameTimer, sentCmdsSnapshot, serverTimePerTick), false);

            serverTimePerTick = PlayingIngamePlayers.MaxOrZero(p => p.frameTime);

            if (serverTimePerTick < StandardTimePerTick)
                serverTimePerTick = StandardTimePerTick;

            if (serverTimePerTick > StandardTimePerTick * 4f)
                serverTimePerTick = StandardTimePerTick * 4f;
        }

        public void TryStop()
        {
            ServerLog.Detail("Server shutting down...");

            playerManager.OnServerStop();
            liteNet.OnServerStop();

            instance = null;
        }

        public void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }

        public void SendToPlaying(Packets id, object[] data)
        {
            SendToPlaying(id, ByteWriter.GetBytes(data));
        }

        public void SendToPlaying(Packets id, byte[] data, bool reliable = true, ServerPlayer? excluding = null)
        {
            foreach (ServerPlayer player in PlayingPlayers)
                if (player != excluding)
                    player.conn.Send(id, data, reliable);
        }

        public ServerPlayer? GetPlayer(string username)
        {
            return playerManager.GetPlayer(username);
        }

        public ServerPlayer? GetPlayer(int id)
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
            ServerLog.Detail($"[Chat] {msg}");
            SendToPlaying(Packets.Server_Chat, new object[] { msg });
        }

        public void SendNotification(string key, params string[] args)
        {
            SendToPlaying(Packets.Server_Notification, new object[] { key, args });
        }

        public void RegisterChatCmd(string cmdName, ChatCmdHandler handler)
        {
            chatCmdHandlers[cmdName] = handler;
        }

        public ChatCmdHandler? GetChatCmdHandler(string cmdName)
        {
            chatCmdHandlers.TryGetValue(cmdName, out ChatCmdHandler handler);
            return handler;
        }

        public void HandleChatCmd(IChatSource source, string cmd)
        {
            var parts = cmd.Split(' ');
            var handler = GetChatCmdHandler(parts[0]);

            if (handler != null)
            {
                if (handler.requiresHost && source is ServerPlayer { IsHost: false })
                    source.SendMsg("No permission");
                else
                    handler.Handle(source, parts.SubArray(1));
            }
            else
            {
                source.SendMsg("Invalid command");
            }
        }

        public Task<ServerInitData?> InitData()
        {
            return initDataSource.Task;
        }

        public void CompleteInitData(ServerInitData data)
        {
            initData = data;
            initDataState = InitDataState.Complete;
            initDataSource.SetResult(data);
        }
    }

    public enum InitDataState
    {
        Waiting,
        Requested,
        Complete
    }
}
