using System;
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
        }

        [PacketHandler(Packets.Client_RequestRejoin)]
        public void HandleRejoin(ByteReader data)
        {
            connection.ChangeState(ConnectionStateEnum.ServerLoading);
            Player.ResetTimeVotes();
        }

        [PacketHandler(Packets.Client_Desynced)]
        public void HandleDesynced(ByteReader data)
        {
            var tick = data.ReadInt32();
            var diffAt = data.ReadInt32();

            Server.playerManager.OnDesync(Player, tick, diffAt);
        }

        [PacketHandler(Packets.Client_Traces, allowFragmented: true)]
        public void HandleTraces(ByteReader data)
        {
            var type = data.ReadEnum<TracesPacket>();

            if (type == TracesPacket.Response && Player.IsHost)
            {
                var playerId = data.ReadInt32();
                var traces = data.ReadPrefixedBytes();
                Server.GetPlayer(playerId)?.SendPacket(Packets.Server_Traces, new object[] { TracesPacket.Transfer, traces });
            }
        }

        [TypedPacketHandler]
        public void HandleClientCommand(ClientCommandPacket packet)
        {
            if (packet.type == CommandType.PlayerCount)
            {
                ByteReader reader = new ByteReader(packet.data);
                var prevMapId = reader.ReadInt32();
                var newMapId = reader.ReadInt32();
                if (Player.currentMapId != prevMapId)
                    ServerLog.Error($"Inconsistent player {Player.Username} map. Last known map: {Player.currentMapId}, " +
                                    $"however received command with transition: {prevMapId} -> {newMapId}");
                Player.currentMapId = newMapId;
            }

            // todo check if map id is valid for the player

            Server.commands.Send(packet.type, Player.FactionId, packet.mapId, packet.data, Player);
        }

        public const int MaxChatMsgLength = 128;

        [PacketHandler(Packets.Client_Chat)]
        public void HandleChat(ByteReader data)
        {
            string msg = data.ReadString();
            msg = msg.Trim();

            // todo handle max length
            if (msg.Length == 0) return;

            if (msg[0] == '/')
            {
                var cmd = msg.Substring(1);
                Server.HandleChatCmd(Player, cmd);
            }
            else
            {
                Server.SendChat($"{connection.username}: {msg}");
            }
        }

        [PacketHandler(Packets.Client_WorldDataUpload, allowFragmented: true)]
        public void HandleWorldDataUpload(ByteReader data)
        {
            if (Server.ArbiterPlaying ? !Player.IsArbiter : !Player.IsHost) // policy
                return;

            ServerLog.Log($"Got world upload {data.Left}");

            Server.worldData.mapData = new Dictionary<int, byte[]>();

            int maps = data.ReadInt32();
            for (int i = 0; i < maps; i++)
            {
                int mapId = data.ReadInt32();
                Server.worldData.mapData[mapId] = data.ReadPrefixedBytes();
            }

            Server.worldData.savedGame = data.ReadPrefixedBytes();
            Server.worldData.sessionData = data.ReadPrefixedBytes();

            if (Server.worldData.CreatingJoinPoint)
                Server.worldData.EndJoinPointCreation();
        }

        [TypedPacketHandler]
        public void HandleCursor(ClientCursorPacket clientPacket)
        {
            if (Player.lastCursorTick == Server.NetTimer) return; // policy
            Player.lastCursorTick = Server.NetTimer;

            var serverPacket = new ServerCursorPacket(Player.id, clientPacket);
            Server.SendToIngame(serverPacket, reliable: false, excluding: Player);
        }

        [PacketHandler(Packets.Client_Selected)]
        public void HandleSelected(ByteReader data)
        {
            bool reset = data.ReadBool();

            var writer = new ByteWriter();

            writer.WriteInt32(Player.id);
            writer.WriteBool(reset);
            writer.WritePrefixedInts(data.ReadPrefixedInts(200));
            writer.WritePrefixedInts(data.ReadPrefixedInts(200));

            Server.SendToPlaying(Packets.Server_Selected, writer.ToArray(), excluding: Player);
        }

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
            var arbiter = Server.ArbiterPlaying;
            if (arbiter ? !Player.IsArbiter : !Player.IsHost) return; // policy

            // Keep at most 10 sync infos
            Server.worldData.syncInfos.Add(packet.rawSyncOpinion);
            if (Server.worldData.syncInfos.Count > 10)
                Server.worldData.syncInfos.RemoveAt(0);

            foreach (var p in Server.PlayingPlayers.Where(p => !p.IsArbiter && (arbiter || !p.IsHost)))
                p.conn.SendFragmented(new ServerSyncInfoPacket { rawSyncOpinion = packet.rawSyncOpinion }.Serialize());
        }

        [PacketHandler(Packets.Client_Freeze)]
        public void HandleFreeze(ByteReader data)
        {
            bool freeze = data.ReadBool();
            Player.frozen = freeze;

            if (!freeze)
                Player.unfrozenAt = Server.NetTimer;
        }

        [PacketHandler(Packets.Client_Autosaving)]
        public void HandleAutosaving(ByteReader data)
        {
            // Host policy
            if (Player.IsHost && Server.settings.autoJoinPoint.HasFlag(AutoJoinPointFlags.Autosave))
                Server.worldData.TryStartJoinPointCreation();
        }

        [PacketHandler(Packets.Client_Debug)]
        public void HandleDebug(ByteReader data)
        {
            // todo restrict handling

            Server.worldData.mapCmds.Clear();
            Server.gameTimer = Server.startingTimer;

            Server.SendToPlaying(Packets.Server_Debug, Array.Empty<object>());
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

        [PacketHandler(Packets.Client_FrameTime)]
        public void HandleFrameTime(ByteReader data)
        {
            Player.frameTime = data.ReadFloat();
        }
    }

    public enum TracesPacket : byte
    {
        Request, Response, Transfer
    }
}
