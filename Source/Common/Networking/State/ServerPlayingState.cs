using System.Collections.Generic;
using System.Linq;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common
{
    public class ServerPlayingState(ConnectionBase conn) : MpConnectionState(conn)
    {
        [PacketHandler(Packets.Client_WorldReady)]
        public void HandleWorldReady(ByteReader data)
        {
            Player.UpdateStatus(PlayerStatus.Playing);
            Server.playerManager.UpdateSyncReference();
        }

        [PacketHandler(Packets.Client_RequestRejoin)]
        public void HandleRejoin(ByteReader data)
        {
            // Not caught up until Client_WorldReady again: can't be the sync reference or a join point uploader
            Player.UpdateStatus(PlayerStatus.Simulating);
            Server.worldData.OnPlayerUnavailable(Player, "is rejoining");
            Server.playerManager.UpdateSyncReference();

            connection.ChangeState(ConnectionStateEnum.ServerLoading);
            Player.ResetTimeVotes();
        }

        [TypedPacketHandler]
        public void HandleDesynced(ClientDesyncedPacket packet) =>
            Server.playerManager.OnDesync(Player, packet.tick, packet.diffAt);

        [TypedPacketHandler]
        public void HandleTraces(ClientTracesPacket packet)
        {
            // Requested in PlayerManager.OnDesync
            if (!Player.IsHost && Player != Server.playerManager.SyncReference) return;
            Server.GetPlayer(packet.playerId)?.SendPacket(ServerTracesPacket.Transfer(packet.rawTraces, packet.rawJittedMethods));
        }

        [TypedPacketHandler]
        public void HandleClientCommand(ClientCommandPacket packet)
        {
            int? mapToResync = null;

            if (packet.type == CommandType.PlayerCount)
            {
                ByteReader reader = new ByteReader(packet.data);
                var prevMapId = reader.ReadInt32();
                var newMapId = reader.ReadInt32();
                if (Player.currentMapId != prevMapId)
                    ServerLog.Error($"Inconsistent player {Player.Username} map. Last known map: {Player.currentMapId}, " +
                                    $"however received command with transition: {prevMapId} -> {newMapId}");
                Player.currentMapId = newMapId;
                Player.hasReportedCurrentMap = true;

                if (Server.CanUseStandaloneMapStreaming(newMapId))
                    mapToResync = newMapId;
            }

            // todo check if map id is valid for the player

            Server.commands.Send(packet.type, Player.FactionId, packet.mapId, packet.data, Player);

            if (mapToResync is int currentMapId)
                Server.SendMapResponse(Player, currentMapId);
        }

        public const int MaxChatMsgLength = 128;

        [TypedPacketHandler]
        public void HandleChat(ClientChatPacket packet)
        {
            Player.helpOnlyUsableCommands = packet.helpOnlyUsableCommands;

            string msg = packet.msg;
            msg = msg.Trim();

            if (msg.Length == 0) return;

            if (msg.Length > MaxChatMsgLength)
                msg = msg[..MaxChatMsgLength];

            if (msg[0] == '/')
            {
                var cmd = msg[1..];
                Server.HandleChatCommand(Player, cmd);
            }
            else
            {
                Server.SendChat($"{connection.username}: {msg}");
            }
        }

        [PacketHandler(Packets.Client_WorldDataUpload, allowFragmented: true)]
        public void HandleWorldDataUpload(ByteReader data)
        {
            ServerLog.Detail($"Got world upload {data.Left} from {Player.Username}");

            int tick = data.ReadInt32();

            var maps = new Dictionary<int, byte[]>();
            int mapCount = data.ReadInt32();
            for (int i = 0; i < mapCount; i++)
            {
                int mapId = data.ReadInt32();
                maps[mapId] = data.ReadPrefixedBytes()!;
            }

            var game = data.ReadPrefixedBytes()!;
            var session = data.ReadPrefixedBytes()!;

            // The host's initial upload when hosting, there is no command log yet
            if (Server.worldData.savedGame == null && !Server.IsStandaloneServer && Player.IsHost)
            {
                Server.worldData.mapData = maps;
                Server.worldData.savedGame = game;
                Server.worldData.sessionData = session;
                return;
            }

            // Anything else replacing the world without its command log would break every later joiner
            Server.worldData.TryCompleteJoinPoint(Player, tick, maps, game, session);
        }

        [FragmentedPacketHandler(Packets.Client_WorldDataUpload)]
        public void HandleWorldDataUploadFragment(FragmentedPacket packet) =>
            Server.worldData.OnUploadProgress(Player);

        // Standalone snapshots are taken from join point uploads (Client_WorldDataUpload). A snapshot on its own would
        // replace the world without trimming the command log.
        [TypedPacketHandler]
        public void HandleStandaloneWorldSnapshot(ClientStandaloneWorldSnapshotPacket packet) =>
            ServerLog.Detail($"Ignored standalone world snapshot tick={packet.tick} from {Player.Username}");

        [TypedPacketHandler]
        public void HandleStandaloneMapSnapshot(ClientStandaloneMapSnapshotPacket packet) =>
            ServerLog.Detail($"Ignored standalone map snapshot map={packet.mapId} tick={packet.tick} from {Player.Username}");

        [TypedPacketHandler]
        public void HandleCursor(ClientCursorPacket clientPacket)
        {
            if (Player.lastCursorTick == Server.NetTimer) return; // policy
            Player.lastCursorTick = Server.NetTimer;

            var serverPacket = new ServerCursorPacket(Player.id, clientPacket);
            Server.SendToIngame(serverPacket, reliable: false, excluding: Player);
        }

        [TypedPacketHandler]
        public void HandleSelected(ClientSelectedPacket packet) =>
            Server.SendToPlaying(new ServerSelectedPacket(Player.id, packet), excluding: Player);

        [TypedPacketHandler]
        public void HandlePing(ClientPingLocPacket packet) =>
            Server.SendToPlaying(new ServerPingLocPacket(Player.id, packet));

        [TypedPacketHandler]
        public void HandleClientKeepAlive(ClientKeepAlivePacket packet)
        {
            Player.ticksBehind = packet.ticksBehind;
            Player.ticksBehindReceivedAt = Server.gameTimer;
            Player.simulating = packet.simulating;
            Player.keepAliveAt = Server.NetTimer;

            if (Player.IsHost)
                Server.workTicks = packet.workTicks;

            var idMatched = Player.keepAliveId == packet.id;
            connection.OnKeepAliveArrived(idMatched);
            if (idMatched) Player.keepAliveId++;
        }

        [TypedPacketHandler]
        public void HandleDesyncCheck(ClientSyncInfoPacket packet)
        {
            // Only the reference's opinions are kept and forwarded (arbiter, host, or the standalone reference)
            var reference = Server.playerManager.SyncReference;
            if (Player != reference) return; // policy

            // Keep at most 10 sync infos
            Server.worldData.syncInfos.Add(packet.rawSyncOpinion);
            if (Server.worldData.syncInfos.Count > 10)
                Server.worldData.syncInfos.RemoveAt(0);

            foreach (var p in Server.PlayingPlayers.Where(p => p != reference && !p.IsArbiter))
                p.conn.SendFragmented(new ServerSyncInfoPacket { rawSyncOpinion = packet.rawSyncOpinion }.Serialize());
        }

        [TypedPacketHandler]
        public void HandleFreeze(ClientFreezePacket packet)
        {
            Player.frozen = packet.freeze;

            if (!packet.freeze)
                Player.unfrozenAt = Server.NetTimer;
        }

        [TypedPacketHandler]
        public void HandleAutosaving(ClientAutosavingPacket packet)
        {
            var forceJoinPoint = packet.reason == JoinPointRequestReason.Save;

            ServerLog.Detail(
                $"Received Client_Autosaving from {Player.Username}, standalone={Server.IsStandaloneServer}, " +
                $"isHost={Player.IsHost}, reason={packet.reason}, force={forceJoinPoint}");

            // Every client reloads at a join point (and the game pauses), so per-player world travel join points
            // would interrupt everyone each time someone opens the world map. Standalone autosaves are server driven.
            if (packet.reason == JoinPointRequestReason.WorldTravel)
                return;

            // On standalone, any playing client can request one (saves; forced ones are rate limited)
            // On hosted, only the host can trigger and only if the Autosave flag is set
            if (Server.IsStandaloneServer ||
                (Player.IsHost && Server.settings.autoJoinPoint.HasFlag(AutoJoinPointFlags.Autosave)))
                Server.worldData.TryStartJoinPointCreation(forceJoinPoint, sourcePlayer: Player);
        }

        [TypedPacketHandler]
        public void HandleDebug(ClientDebugPacket _)
        {
            if (!Server.commands.CanUseDevMode(Player))
                return;

            Server.worldData.cmds.Clear();
            Server.gameTimer = Server.startingTimer;

            Server.SendToPlaying(new ServerDebugPacket());
        }

        [TypedPacketHandler]
        public void HandleSetFaction(ClientSetFactionPacket packet)
        {
            // todo restrict handling

            int playerId = packet.playerId;
            int factionId = packet.factionId;

            var player = Server.GetPlayer(playerId);
            if (player == null) return;
            if (player.FactionId == factionId) return;

            player.FactionId = factionId;
            Server.SendToPlaying(new ServerSetFactionPacket(playerId, factionId));
        }

        [TypedPacketHandler]
        public void HandleFrameTime(ClientFrameTimePacket packet) => Player.frameTime = packet.frameTime;
    }
}
