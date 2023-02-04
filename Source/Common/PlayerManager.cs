using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;

namespace Multiplayer.Common
{
    public class PlayerManager
    {
        private MultiplayerServer server;
        const long ThrottleMillis = 1000;
        private Dictionary<object, long> lastConnection = new();
        private Stopwatch clock = Stopwatch.StartNew();
        public HashSet<int> playersWaitingForWorldData = new();

        public List<ServerPlayer> Players { get; } = new();

        public IEnumerable<ServerPlayer> PlayingPlayers => Players.Where(p => p.IsPlaying);

        public PlayerManager(MultiplayerServer server)
        {
            this.server = server;
        }

        public void SendLatencies()
        {
            var writer = new ByteWriter();
            writer.WriteByte((byte)PlayerListAction.Latencies);

            writer.WriteInt32(PlayingPlayers.Count());
            foreach (var player in PlayingPlayers)
                player.WriteLatencyUpdate(writer);

            server.SendToAll(Packets.Server_PlayerList, writer.ToArray());
        }

        // id can be an IPAddress or CSteamID
        public MpDisconnectReason? OnPreConnect(object id)
        {
            if (server.FullyStarted is false)
                return MpDisconnectReason.ServerStarting;

            if (id is IPAddress addr && IPAddress.IsLoopback(addr))
                return null;

            if (server.settings.maxPlayers > 0 &&
                Players.Count(p => !p.IsArbiter) >= server.settings.maxPlayers)
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
            Players.Add(conn.serverPlayer);
            ServerLog.Log($"New connection: {conn}");

            return conn.serverPlayer;
        }

        public void OnDisconnected(ConnectionBase conn, MpDisconnectReason reason)
        {
            if (conn.State == ConnectionStateEnum.Disconnected) return;

            ServerPlayer player = conn.serverPlayer;
            Players.Remove(player);

            if (player.hasJoined)
            {
                // todo check player.IsPlaying?
                // todo FactionId might throw when called for not fully initialized players
                // if (Players.All(p => p.FactionId != player.FactionId))
                // {
                //     byte[] data = ByteWriter.GetBytes(player.FactionId);
                //     server.commands.Send(CommandType.FactionOffline, ScheduledCommand.NoFaction, ScheduledCommand.Global, data);
                // }

                server.SendNotification("MpPlayerDisconnected", conn.username);
                server.SendChat($"{conn.username} has left.");

                server.SendToAll(Packets.Server_PlayerList, new object[] { (byte)PlayerListAction.Remove, player.id });

                player.ResetTimeVotes();
            }

            conn.State = ConnectionStateEnum.Disconnected;

            ServerLog.Log($"Disconnected ({reason}): {conn}");
        }

        public void OnDesync(ServerPlayer player, int tick, int diffAt)
        {
            player.UpdateStatus(PlayerStatus.Desynced);
            server.HostPlayer.SendPacket(Packets.Server_Traces, new object[] { TracesPacket.Request, tick, diffAt, player.id });

            player.ResetTimeVotes();

            if (server.settings.pauseOnDesync)
                server.commands.PauseAll();

            if (server.settings.autoJoinPoint.HasFlag(AutoJoinPointFlags.Desync))
                server.TryStartJoinPointCreation(true);
        }

        public static ColorRGB[] PlayerColors =
        {
            new(0,125,255),
            new(255,0,0),
            new(0,255,45),
            new(255,0,150),
            new(80,250,250),
            new(200,255,75),
            new(100,0,75)
        };

        public static Dictionary<string, ColorRGB> givenColors = new();

        public void OnJoin(ServerPlayer player)
        {
            player.hasJoined = true;
            player.FactionId = server.defaultFactionId;

            SendInitDataCommand(player);

            server.SendNotification("MpPlayerConnected", player.Username);
            server.SendChat($"{player.Username} has joined.");

            if (!player.IsArbiter)
            {
                if (!givenColors.TryGetValue(player.Username, out ColorRGB color))
                    givenColors[player.Username] = color = PlayerColors[givenColors.Count % PlayerColors.Length];
                player.color = color;
            }

            var writer = new ByteWriter();
            writer.WriteByte((byte)PlayerListAction.Add);
            writer.WriteRaw(player.SerializePlayerInfo());

            server.SendToAll(Packets.Server_PlayerList, writer.ToArray());
        }

        public void SendInitDataCommand(ServerPlayer player)
        {
            server.commands.Send(
                CommandType.InitPlayerData,
                ScheduledCommand.NoFaction, ScheduledCommand.Global,
                ByteWriter.GetBytes(player.id, server.commands.CanUseDevMode(player))
            );
        }

        public void OnServerStop()
        {
            foreach (var player in Players)
                player.conn.Close(MpDisconnectReason.ServerClosed);

            Players.Clear();
        }

        public ServerPlayer GetPlayer(string username)
        {
            return Players.Find(player => player.Username == username);
        }

        public ServerPlayer GetPlayer(int id)
        {
            return Players.Find(player => player.id == id);
        }
    }
}
