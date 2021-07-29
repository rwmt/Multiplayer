using Ionic.Zlib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RestSharp;
using Verse;

namespace Multiplayer.Common
{
    public class ServerJoiningState : MpConnectionState
    {
        public static Regex UsernamePattern = new Regex(@"^[a-zA-Z0-9_]+$");
        
        private bool defsMismatched;

        public ServerJoiningState(IConnection conn) : base(conn)
        {
        }

        [PacketHandler(Packets.Client_JoinData)]
        [IsFragmented]
        public void HandleJoinData(ByteReader data)
        {
            int clientProtocol = data.ReadInt32();
            if (clientProtocol != MpVersion.Protocol)
            {
                Player.Disconnect(MpDisconnectReason.Protocol, ByteWriter.GetBytes(MpVersion.Version, MpVersion.Protocol));
                return;
            }

            var count = data.ReadInt32();
            if (count > 512)
            {
                Player.Disconnect(MpDisconnectReason.Internal);
                return;
            }

            var defsResponse = new ByteWriter();

            for (int i = 0; i < count; i++)
            {
                var defType = data.ReadString(128);
                var defCount = data.ReadInt32();
                var defHash = data.ReadInt32();

                var status = DefCheckStatus.OK;

                if (!Server.defInfos.TryGetValue(defType, out DefInfo info))
                    status = DefCheckStatus.Not_Found;
                else if (info.count != defCount)
                    status = DefCheckStatus.Count_Diff;
                else if (info.hash != defHash)
                    status = DefCheckStatus.Hash_Diff;

                if (status != DefCheckStatus.OK)
                    defsMismatched = true;

                defsResponse.WriteByte((byte)status);
            }

            connection.SendFragmented(
                Packets.Server_JoinData,
                Server.settings.gameName,
                Player.id,
                Server.rwVersion,
                defsResponse.ToArray(),
                Server.serverData
            );
        }

        private static ColorRGB[] PlayerColors = new ColorRGB[]
        {
            new ColorRGB(0,125,255),
            new ColorRGB(255,0,0),
            new ColorRGB(0,255,45),
            new ColorRGB(255,0,150),
            new ColorRGB(80,250,250),
            new ColorRGB(200,255,75),
            new ColorRGB(100,0,75)
        };

        private static Dictionary<string, ColorRGB> givenColors = new Dictionary<string, ColorRGB>();

        [PacketHandler(Packets.Client_Username)]
        public void HandleClientUsername(ByteReader data)
        {
            if (connection.username != null && connection.username.Length != 0)
                return;

            string username = data.ReadString();

            if (username.Length < 3 || username.Length > 15)
            {
                Player.Disconnect(MpDisconnectReason.UsernameLength);
                return;
            }

            if (!Player.IsArbiter && !UsernamePattern.IsMatch(username))
            {
                Player.Disconnect(MpDisconnectReason.UsernameChars);
                return;
            }

            if (Server.GetPlayer(username) != null)
            {
                Player.Disconnect(MpDisconnectReason.UsernameAlreadyOnline);
                return;
            }

            connection.username = username;

            Server.SendNotification("MpPlayerConnected", Player.Username);
            Server.SendChat($"{Player.Username} has joined.");

            if (!Player.IsArbiter)
            {
                if (!givenColors.TryGetValue(username, out ColorRGB color))
                    givenColors[username] = color = PlayerColors[givenColors.Count % PlayerColors.Length];
                Player.color = color;
            }

            var writer = new ByteWriter();
            writer.WriteByte((byte)PlayerListAction.Add);
            writer.WriteRaw(Player.SerializePlayerInfo());

            Server.SendToAll(Packets.Server_PlayerList, writer.ToArray());

            SendWorldData();
        }

        private void SendWorldData()
        {
            int factionId = MultiplayerServer.instance.coopFactionId;
            MultiplayerServer.instance.playerFactions[connection.username] = factionId;

            /*if (!MultiplayerServer.instance.playerFactions.TryGetValue(connection.Username, out int factionId))
            {
                factionId = MultiplayerServer.instance.nextUniqueId++;
                MultiplayerServer.instance.playerFactions[connection.Username] = factionId;

                byte[] extra = ByteWriter.GetBytes(factionId);
                MultiplayerServer.instance.SendCommand(CommandType.SETUP_FACTION, ScheduledCommand.NoFaction, ScheduledCommand.Global, extra);
            }*/

            if (Server.PlayingPlayers.Count(p => p.FactionId == factionId) == 1)
            {
                byte[] extra = ByteWriter.GetBytes(factionId);
                MultiplayerServer.instance.SendCommand(CommandType.FactionOnline, ScheduledCommand.NoFaction, ScheduledCommand.Global, extra);
            }

            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(factionId);
            writer.WriteInt32(MultiplayerServer.instance.gameTimer);
            writer.WritePrefixedBytes(MultiplayerServer.instance.savedGame);

            writer.WriteInt32(MultiplayerServer.instance.mapCmds.Count);

            foreach (var kv in MultiplayerServer.instance.mapCmds)
            {
                int mapId = kv.Key;

                //MultiplayerServer.instance.SendCommand(CommandType.CreateMapFactionData, ScheduledCommand.NoFaction, mapId, ByteWriter.GetBytes(factionId));

                List<byte[]> mapCmds = kv.Value;

                writer.WriteInt32(mapId);

                writer.WriteInt32(mapCmds.Count);
                foreach (var arr in mapCmds)
                    writer.WritePrefixedBytes(arr);
            }

            writer.WriteInt32(MultiplayerServer.instance.mapData.Count);

            foreach (var kv in MultiplayerServer.instance.mapData)
            {
                int mapId = kv.Key;
                byte[] mapData = kv.Value;

                writer.WriteInt32(mapId);
                writer.WritePrefixedBytes(mapData);
            }

            writer.WriteInt32(Server.cmdId);

            connection.State = ConnectionStateEnum.ServerPlaying;

            byte[] packetData = writer.ToArray();
            connection.SendFragmented(Packets.Server_WorldData, packetData);

            Player.SendPlayerList();

            MpLog.Log("World response sent: " + packetData.Length);
        }
    }

    public enum DefCheckStatus : byte
    {
        OK,
        Not_Found,
        Count_Diff,
        Hash_Diff,
    }

    public class ServerPlayingState : MpConnectionState
    {
        public ServerPlayingState(IConnection conn) : base(conn)
        {
        }

        [PacketHandler(Packets.Client_WorldReady)]
        public void HandleWorldReady(ByteReader data)
        {
            Player.UpdateStatus(PlayerStatus.Playing);
        }

        [PacketHandler(Packets.Client_Desynced)]
        public void HandleDesynced(ByteReader data)
        {
            Player.UpdateStatus(PlayerStatus.Desynced);

            // todo
            //if (MultiplayerMod.settings.autosaveOnDesync)
            //    Server.DoAutosave(forcePause: true);
        }

        [PacketHandler(Packets.Client_Command)]
        public void HandleClientCommand(ByteReader data)
        {
            CommandType cmd = (CommandType)data.ReadInt32();
            int mapId = data.ReadInt32();
            byte[] extra = data.ReadPrefixedBytes(32767);

            // todo check if map id is valid for the player

            int factionId = MultiplayerServer.instance.playerFactions[connection.username];
            MultiplayerServer.instance.SendCommand(cmd, factionId, mapId, extra, Player);
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
                var parts = cmd.Split(' ');
                var handler = Server.GetCmdHandler(parts[0]);

                if (handler != null)
                {
                    if (handler.requiresHost && Player.Username != Server.hostUsername)
                        Player.SendChat("No permission");
                    else
                        handler.Handle(Player, parts.SubArray(1));
                }
                else
                {
                    Player.SendChat("Invalid command");
                }
            }
            else
            {
                Server.SendChat($"{connection.username}: {msg}");
            }
        }

        [PacketHandler(Packets.Client_AutosavedData)]
        [IsFragmented]
        public void HandleAutosavedData(ByteReader data)
        {
            var arbiter = Server.ArbiterPlaying;
            if (arbiter && !Player.IsArbiter) return;
            if (!arbiter && Player.Username != Server.hostUsername) return;

            int maps = data.ReadInt32();
            for (int i = 0; i < maps; i++)
            {
                int mapId = data.ReadInt32();
                Server.mapData[mapId] = data.ReadPrefixedBytes();
            }

            Server.savedGame = data.ReadPrefixedBytes();

            if (Server.tmpMapCmds != null)
            {
                Server.mapCmds = Server.tmpMapCmds;
                Server.tmpMapCmds = null;
            }
        }

        [PacketHandler(Packets.Client_Cursor)]
        public void HandleCursor(ByteReader data)
        {
            if (Player.lastCursorTick == Server.netTimer) return;

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

            Player.lastCursorTick = Server.netTimer;

            Server.SendToAll(Packets.Server_Cursor, writer.ToArray(), reliable: false, excluding: Player);
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

            Server.SendToAll(Packets.Server_Selected, writer.ToArray(), excluding: Player);
        }

        [PacketHandler(Packets.Client_IdBlockRequest)]
        public void HandleIdBlockRequest(ByteReader data)
        {
            int mapId = data.ReadInt32();

            if (mapId == ScheduledCommand.Global)
            {
                //IdBlock nextBlock = MultiplayerServer.instance.NextIdBlock();
                //MultiplayerServer.instance.SendCommand(CommandType.GlobalIdBlock, ScheduledCommand.NoFaction, ScheduledCommand.Global, nextBlock.Serialize());
            }
            else
            {
                // todo
            }
        }

        [PacketHandler(Packets.Client_KeepAlive)]
        public void HandleClientKeepAlive(ByteReader data)
        {
            int id = data.ReadInt32();
            int ticksBehind = data.ReadInt32();

            Player.ticksBehind = ticksBehind;

            // Latency already handled by LiteNetLib
            if (connection is MpNetConnection) return;

            if (MultiplayerServer.instance.keepAliveId == id)
                connection.Latency = (int)MultiplayerServer.instance.lastKeepAlive.ElapsedMilliseconds / 2;
            else
                connection.Latency = 2000;
        }

        [PacketHandler(Packets.Client_SyncInfo)]
        [IsFragmented]
        public void HandleDesyncCheck(ByteReader data)
        {
            var arbiter = Server.ArbiterPlaying;
            if (arbiter ? !Player.IsArbiter : Player.Username != Server.hostUsername) return;

            var raw = data.ReadRaw(data.Left);
            foreach (var p in Server.PlayingPlayers.Where(p => !p.IsArbiter && (arbiter || !p.IsHost)))
                p.conn.SendFragmented(Packets.Server_SyncInfo, raw);
        }

        [PacketHandler(Packets.Client_Pause)]
        public void HandlePause(ByteReader data)
        {
            bool pause = data.ReadBool();
            if (pause && Player.Username != Server.hostUsername) return;
            if (Server.paused == pause) return;

            Server.paused = pause;
            Server.SendToAll(Packets.Server_Pause, new object[] { pause });
        }

        [PacketHandler(Packets.Client_Debug)]
        public void HandleDebug(ByteReader data)
        {
            if (!MpVersion.IsDebug) return;

            Server.PlayingPlayers.FirstOrDefault(p => p.IsArbiter || p.IsHost)?.SendPacket(Packets.Server_Debug, data.ReadRaw(data.Left));
        }
    }

    public enum PlayerListAction : byte
    {
        List,
        Add,
        Remove,
        Latencies,
        Status
    }

    // Unused
    public class ServerSteamState : MpConnectionState
    {
        public ServerSteamState(IConnection conn) : base(conn)
        {
        }

        [PacketHandler(Packets.Client_SteamRequest)]
        public void HandleSteamRequest(ByteReader data)
        {
            connection.State = ConnectionStateEnum.ServerJoining;
            connection.Send(Packets.Server_SteamAccept);
        }
    }
}
