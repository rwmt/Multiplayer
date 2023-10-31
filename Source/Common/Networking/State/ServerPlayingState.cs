using System;
using System.Collections.Generic;
using System.Linq;

namespace Multiplayer.Common
{
    public class ServerPlayingState : MpConnectionState
    {
        public ServerPlayingState(ConnectionBase conn) : base(conn)
        {
        }

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

        [PacketHandler(Packets.Client_Traces)]
        [IsFragmented]
        public void HandleTraces(ByteReader data)
        {
            var type = (TracesPacket)data.ReadInt32();

            if (type == TracesPacket.Response && Player.IsHost)
            {
                var playerId = data.ReadInt32();
                var traces = data.ReadPrefixedBytes();
                Server.GetPlayer(playerId)?.SendPacket(Packets.Server_Traces, new object[] { TracesPacket.Transfer, traces });
            }
        }

        [PacketHandler(Packets.Client_Command)]
        public void HandleClientCommand(ByteReader data)
        {
            CommandType cmd = (CommandType)data.ReadInt32();
            int mapId = data.ReadInt32();
            byte[]? extra = data.ReadPrefixedBytes(65535);
            if (extra == null) return;

            // todo check if map id is valid for the player

            Server.commands.Send(cmd, Player.FactionId, mapId, extra, Player);
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

        [PacketHandler(Packets.Client_WorldDataUpload)]
        [IsFragmented]
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
            Server.worldData.semiPersistent = data.ReadPrefixedBytes();

            if (Server.worldData.CreatingJoinPoint)
                Server.worldData.EndJoinPointCreation();
        }

        [PacketHandler(Packets.Client_Cursor)]
        public void HandleCursor(ByteReader data)
        {
            if (Player.lastCursorTick == Server.NetTimer) return; // policy

            var writer = new ByteWriter();

            byte seq = data.ReadByte();
            byte map = data.ReadByte();

            writer.WriteInt32(Player.id);
            writer.WriteByte(seq);
            writer.WriteByte(map);

            if (map < byte.MaxValue)
            {
                byte icon = data.ReadByte();
                short x = data.ReadShort();
                short z = data.ReadShort();

                writer.WriteByte(icon);
                writer.WriteShort(x);
                writer.WriteShort(z);

                short dragX = data.ReadShort();
                writer.WriteShort(dragX);

                if (dragX != -1)
                {
                    short dragZ = data.ReadShort();
                    writer.WriteShort(dragZ);
                }
            }

            Player.lastCursorTick = Server.NetTimer;

            Server.SendToPlaying(Packets.Server_Cursor, writer.ToArray(), reliable: false, excluding: Player);
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

        [PacketHandler(Packets.Client_PingLocation)]
        public void HandlePing(ByteReader data)
        {
            var writer = new ByteWriter();

            writer.WriteInt32(Player.id);

            writer.WriteInt32(data.ReadInt32()); // Map id
            writer.WriteInt32(data.ReadInt32()); // Planet tile
            writer.WriteFloat(data.ReadFloat()); // X
            writer.WriteFloat(data.ReadFloat()); // Y
            writer.WriteFloat(data.ReadFloat()); // Z

            Server.SendToPlaying(Packets.Server_PingLocation, writer.ToArray());
        }

        [PacketHandler(Packets.Client_KeepAlive)]
        public void HandleClientKeepAlive(ByteReader data)
        {
            int id = data.ReadInt32();
            int ticksBehind = data.ReadInt32();
            var simulating = data.ReadBool();
            var workTicks = data.ReadInt32();

            Player.ticksBehind = ticksBehind;
            Player.ticksBehindReceivedAt = Server.gameTimer;
            Player.simulating = simulating;
            Player.keepAliveAt = Server.NetTimer;

            if (Player.IsHost)
                Server.workTicks = workTicks;

            // Latency already handled by LiteNetLib
            if (connection is LiteNetConnection) return;

            if (Player.keepAliveId == id)
            {
                connection.Latency = (connection.Latency * 4 + (int)Player.keepAliveTimer.ElapsedMilliseconds / 2) / 5;

                Player.keepAliveId++;
                Player.keepAliveTimer.Reset();
            }
        }

        [PacketHandler(Packets.Client_SyncInfo)]
        [IsFragmented]
        public void HandleDesyncCheck(ByteReader data)
        {
            var arbiter = Server.ArbiterPlaying;
            if (arbiter ? !Player.IsArbiter : !Player.IsHost) return; // policy

            var raw = data.ReadRaw(data.Left);

            // Keep at most 10 sync infos
            Server.worldData.syncInfos.Add(raw);
            if (Server.worldData.syncInfos.Count > 10)
                Server.worldData.syncInfos.RemoveAt(0);

            foreach (var p in Server.PlayingPlayers.Where(p => !p.IsArbiter && (arbiter || !p.IsHost)))
                p.conn.SendFragmented(Packets.Server_SyncInfo, raw);
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

        [PacketHandler(Packets.Client_SetFaction)]
        public void HandleSetFaction(ByteReader data)
        {
            // todo restrict handling

            int playerId = data.ReadInt32();
            int factionId = data.ReadInt32();

            var player = Server.GetPlayer(playerId);
            if (player == null) return;

            player.FactionId = factionId;
            Server.SendToPlaying(Packets.Server_SetFaction, new object[] { playerId, factionId });
        }

        [PacketHandler(Packets.Client_FrameTime)]
        public void HandleFrameTime(ByteReader data)
        {
            Player.frameTime = data.ReadFloat();
        }
    }

    public enum TracesPacket
    {
        Request, Response, Transfer
    }
}
