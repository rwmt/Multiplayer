using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common
{
    public class PlayerManager
    {
        private MultiplayerServer server;
        const long ThrottleMillis = 1000;
        private Dictionary<object, long> lastConnection = new();
        private Stopwatch clock = Stopwatch.StartNew();

        public List<ServerPlayer> Players { get; } = new();

        public IEnumerable<ServerPlayer> JoinedPlayers => Players.Where(p => p.HasJoined);
        public IEnumerable<ServerPlayer> PlayingPlayers => Players.Where(p => p.IsPlaying);

        public PlayerManager(MultiplayerServer server)
        {
            this.server = server;
        }

        public void SendLatencies() =>
            server.SendToPlaying(
                ServerPlayerListPacket.Latencies(JoinedPlayers.Select(p => p.LatencyPacket())));

        // id can be an IPAddress or CSteamID
        public MpDisconnectReason? OnPreConnect(object id)
        {
            if (server.FullyStarted is false && !server.BootstrapMode)
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

        public void SetDisconnected(ConnectionBase conn, MpDisconnectReason reason)
        {
            if (conn.State == ConnectionStateEnum.Disconnected) return;

            conn.StateObj?.OnDisconnect();

            ServerPlayer player = conn.serverPlayer;
            Players.Remove(player);

            server.worldData.OnPlayerUnavailable(player, "disconnected");
            UpdateSyncReference();
            if (server.worldData.CreatingJoinPoint && (player.IsHost || !Players.Any(p => p.hasJoined)))
                server.worldData.AbortJoinPointCreation("The host left or no players remain.");

            if (player.hasJoined)
            {
                // Send PlayerCount command to remove the player from their last known map
                if (player.currentMapId != -1)
                {
                    byte[] playerCountData = ByteWriter.GetBytes(player.currentMapId, -1); // previousMap: player's map, newMap: -1 (disconnected)
                    server.commands.Send(CommandType.PlayerCount, ScheduledCommand.NoFaction, ScheduledCommand.Global, playerCountData);
                }
                // todo check player.IsPlaying?
                // todo FactionId might throw when called for not fully initialized players
                // if (Players.All(p => p.FactionId != player.FactionId))
                // {
                //     byte[] data = ByteWriter.GetBytes(player.FactionId);
                //     server.commands.Send(CommandType.FactionOffline, ScheduledCommand.NoFaction, ScheduledCommand.Global, data);
                // }

                server.SendNotification("MpPlayerDisconnected", conn.username);
                server.SendChat($"{conn.username} has left.");

                server.SendToPlaying(ServerPlayerListPacket.Remove(player.id));

                player.ResetTimeVotes();
            }

            conn.ChangeState(ConnectionStateEnum.Disconnected);

            ServerLog.Log($"Disconnected ({reason}): {conn}");
        }

        public void OnDesync(ServerPlayer player, int tick, int diffAt)
        {
            // A desynced client reports once and then rejoins; ignore repeats (each one pauses and forces a join point)
            if (player.status == PlayerStatus.Desynced) return;

            player.UpdateStatus(PlayerStatus.Desynced);
            server.worldData.OnPlayerUnavailable(player, "desynced");
            UpdateSyncReference();

            // Traces come from the client whose opinions the desynced player was compared against (no host on standalone)
            var traceSource = PlayingPlayers.FirstOrDefault(p => p.IsHost && p != player) ?? SyncReference;
            if (traceSource != null && traceSource != player)
                traceSource.SendPacket(ServerTracesPacket.Request(tick, diffAt, player.id));

            player.ResetTimeVotes();

            if (server.settings.pauseOnDesync)
                server.commands.PauseAll();

            if (server.settings.autoJoinPoint.HasFlag(AutoJoinPointFlags.Desync))
                server.worldData.TryStartJoinPointCreation(true);
        }

        /// <summary>
        /// The client treated as the truth: its sync opinions are forwarded to everyone else and it uploads join
        /// points. The arbiter if playing, else the host; standalone has no host, so the longest connected player
        /// which is caught up and in sync.
        /// </summary>
        public ServerPlayer? SyncReference
        {
            get
            {
                var arbiter = PlayingPlayers.FirstOrDefault(p => p.IsArbiter && p.status == PlayerStatus.Playing);
                if (arbiter != null)
                    return arbiter;

                if (!server.IsStandaloneServer)
                    return PlayingPlayers.FirstOrDefault(p => p.IsHost);

                return PlayingPlayers
                    .Where(p => !p.IsArbiter && p.status == PlayerStatus.Playing)
                    .OrderBy(p => p.id)
                    .FirstOrDefault();
            }
        }

        private int syncReferenceId = ScheduledCommand.NoPlayer;

        /// Call after player status changes and disconnects
        public void UpdateSyncReference()
        {
            var reference = SyncReference;
            var referenceId = reference?.id ?? ScheduledCommand.NoPlayer;
            if (referenceId == syncReferenceId) return;

            var previous = GetPlayer(syncReferenceId);
            syncReferenceId = referenceId;
            ServerLog.Detail($"Sync reference changed to {reference?.Username ?? "none"}");

            // Joiners compare their catch-up against these, keep only the current reference's opinions
            server.worldData.syncInfos.Clear();

            // Hosted clients know their role (host/arbiter)
            if (!server.IsStandaloneServer) return;

            previous?.SendPacket(new ServerSyncReferencePacket(false));
            reference?.SendPacket(new ServerSyncReferencePacket(true));
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
            var standalonePrimaryPlayer = server.IsStandaloneServer &&
                !server.JoinedPlayers.Any(p => p != player && !p.IsArbiter);

            player.FactionId = standalonePrimaryPlayer || player.id == 0 || !server.settings.multifaction ?
                server.worldData.hostFactionId :
                server.worldData.spectatorFactionId;

            server.SendNotification("MpPlayerConnected", player.Username);
            server.SendChat($"{player.Username} has joined.");

            if (!player.IsArbiter)
            {
                if (!givenColors.TryGetValue(player.Username, out ColorRGB color))
                    givenColors[player.Username] = color = PlayerColors[givenColors.Count % PlayerColors.Length];
                player.color = color;
            }

            server.SendToPlaying(ServerPlayerListPacket.Add(player.PlayerInfoPacket()));
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

        public void MakeHost(ServerPlayer host)
        {
            OnJoin(host);

            host.conn.ChangeState(ConnectionStateEnum.ServerPlaying);
            host.SendPlayerList();
            SendInitDataCommand(host);
            host.UpdateStatus(PlayerStatus.Playing);
            UpdateSyncReference();
        }

        public ServerPlayer? GetPlayer(string username)
        {
            return Players.Find(player => player.Username == username);
        }

        public ServerPlayer? GetPlayer(int id)
        {
            return Players.Find(player => player.id == id);
        }
    }
}
