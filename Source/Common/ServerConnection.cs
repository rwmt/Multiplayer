using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Multiplayer.Common
{
    public class ServerJoiningState : MpConnectionState
    {
        public static Regex UsernamePattern = new Regex(@"^[a-zA-Z0-9_]+$");

        public ServerJoiningState(IConnection conn) : base(conn)
        {
        }

        [PacketHandler(Packets.Client_Defs)]
        public void HandleDefs(ByteReader data)
        {
            int clientProtocol = data.ReadInt32();
            if (clientProtocol != MpVersion.Protocol)
            {
                Player.Disconnect("MpWrongProtocol");
                return;
            }

            connection.Send(Packets.Server_DefsOK, Server.settings.gameName);
        }

        [PacketHandler(Packets.Client_Username)]
        public void HandleClientUsername(ByteReader data)
        {
            if (connection.username != null && connection.username.Length != 0)
                return;

            string username = data.ReadString();

            if (username.Length < 3 || username.Length > 15)
            {
                Player.Disconnect("MpInvalidUsernameLength");
                return;
            }

            if (!Player.IsArbiter && !UsernamePattern.IsMatch(username))
            {
                Player.Disconnect("MpInvalidUsernameChars");
                return;
            }

            if (Server.GetPlayer(username) != null)
            {
                Player.Disconnect("MpInvalidUsernameAlreadyPlaying");
                return;
            }

            connection.username = username;

            Server.SendNotification("MpPlayerConnected", Player.Username);
            Server.SendChat($"{Player.Username} has joined.");

            var writer = new ByteWriter();
            writer.WriteByte((byte)PlayerListAction.Add);
            writer.WriteRaw(Player.SerializePlayerInfo());

            Server.SendToAll(Packets.Server_PlayerList, writer.GetArray());
        }

        [PacketHandler(Packets.Client_RequestWorld)]
        public void HandleWorldRequest(ByteReader data)
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

            connection.State = ConnectionStateEnum.ServerPlaying;

            byte[] packetData = writer.GetArray();
            connection.SendFragmented(Packets.Server_WorldData, packetData);

            Player.SendPlayerList();

            MpLog.Log("World response sent: " + packetData.Length);
        }
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
            }

            Server.SendToAll(Packets.Server_Cursor, writer.GetArray(), reliable: false);
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
            // Latency already handled by LiteNetLib
            if (connection is MpNetConnection) return;

            int id = data.ReadInt32();
            if (MultiplayerServer.instance.keepAliveId == id)
                connection.Latency = (int)MultiplayerServer.instance.lastKeepAlive.ElapsedMilliseconds / 2;
            else
                connection.Latency = 2000;
        }

        [PacketHandler(Packets.Client_SyncInfo)]
        public void HandleDesyncCheck(ByteReader data)
        {
            var arbiter = Server.ArbiterPlaying;
            if (arbiter && !Player.IsArbiter) return;
            if (!arbiter && Player.Username != Server.hostUsername) return;

            var raw = data.ReadRaw(data.Left);
            foreach (var p in Server.PlayingPlayers.Where(p => !p.IsArbiter))
                p.SendPacket(Packets.Server_SyncInfo, raw);
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

            Server.PlayingPlayers.FirstOrDefault(p => p.IsArbiter)?.SendPacket(Packets.Server_Debug, data.ReadRaw(data.Left));
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
