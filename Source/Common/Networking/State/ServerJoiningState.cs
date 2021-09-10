using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Multiplayer.Common
{
    public class ServerJoiningState : MpConnectionState
    {
        public static Regex UsernamePattern = new(@"^[a-zA-Z0-9_]+$");

        private bool defsMismatched;

        public ServerJoiningState(ConnectionBase conn) : base(conn)
        {
        }

        [PacketHandler(Packets.Client_Username)]
        public void HandleUsername(ByteReader data)
        {
            int clientProtocol = data.ReadInt32();
            if (clientProtocol != MpVersion.Protocol)
            {
                Player.Disconnect(MpDisconnectReason.Protocol, ByteWriter.GetBytes(MpVersion.Version, MpVersion.Protocol));
                return;
            }

            if (!string.IsNullOrEmpty(connection.username)) // Username already set
                return;

            string username = data.ReadString();

            if (username.Length < MultiplayerServer.MinUsernameLength || username.Length > MultiplayerServer.MaxUsernameLength)
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
            connection.Send(Packets.Server_UsernameOk);
        }

        [PacketHandler(Packets.Client_JoinData)]
        [IsFragmented]
        public void HandleJoinData(ByteReader data)
        {
            var count = data.ReadInt32();
            if (count > 512)
            {
                Player.Disconnect("Too many defs");
                return;
            }

            var defsResponse = new ByteWriter();

            for (int i = 0; i < count; i++)
            {
                var defType = data.ReadString(128);
                var defCount = data.ReadInt32();
                var defHash = data.ReadInt32();

                var status = DefCheckStatus.Ok;

                if (!Server.defInfos.TryGetValue(defType, out DefInfo info))
                    status = DefCheckStatus.Not_Found;
                else if (info.count != defCount)
                    status = DefCheckStatus.Count_Diff;
                else if (info.hash != defHash)
                    status = DefCheckStatus.Hash_Diff;

                if (status != DefCheckStatus.Ok)
                    defsMismatched = true;

                defsResponse.WriteByte((byte)status);
            }

            connection.SendFragmented(
                Packets.Server_JoinData,
                Server.settings.gameName,
                Player.id,
                Server.rwVersion,
                Server.mpVersion,
                defsResponse.ToArray(),
                Server.serverData
            );
        }

        private static ColorRGB[] PlayerColors =
        {
            new(0,125,255),
            new(255,0,0),
            new(0,255,45),
            new(255,0,150),
            new(80,250,250),
            new(200,255,75),
            new(100,0,75)
        };

        private static Dictionary<string, ColorRGB> givenColors = new();

        [PacketHandler(Packets.Client_WorldRequest)]
        public void HandleClientUsername(ByteReader data)
        {
            Server.SendNotification("MpPlayerConnected", Player.Username);
            Server.SendChat($"{Player.Username} has joined.");

            if (!Player.IsArbiter)
            {
                if (!givenColors.TryGetValue(Player.Username, out ColorRGB color))
                    givenColors[Player.Username] = color = PlayerColors[givenColors.Count % PlayerColors.Length];
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
            writer.WritePrefixedBytes(MultiplayerServer.instance.semiPersistent);

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

            writer.WriteInt32(Server.nextCmdId);

            connection.State = ConnectionStateEnum.ServerPlaying;

            byte[] packetData = writer.ToArray();
            connection.SendFragmented(Packets.Server_WorldData, packetData);

            Player.SendPlayerList();

            ServerLog.Log("World response sent: " + packetData.Length);
        }
    }

    public enum DefCheckStatus : byte
    {
        Ok,
        Not_Found,
        Count_Diff,
        Hash_Diff,
    }
}
