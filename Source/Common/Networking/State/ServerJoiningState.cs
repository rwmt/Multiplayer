using System.Collections.Generic;
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

        [PacketHandler(Packets.Client_Protocol)]
        public void HandleProtocol(ByteReader data)
        {
            int clientProtocol = data.ReadInt32();

            if (clientProtocol != MpVersion.Protocol)
                Player.Disconnect(MpDisconnectReason.Protocol, ByteWriter.GetBytes(MpVersion.Version, MpVersion.Protocol));
            else
                Player.SendPacket(Packets.Server_ProtocolOk, new object[] { Server.settings.hasPassword });
        }

        [PacketHandler(Packets.Client_Username)]
        public void HandleUsername(ByteReader data)
        {
            if (!string.IsNullOrEmpty(connection.username)) // Username already set
                return;

            if (Server.settings.hasPassword)
            {
                string password = data.ReadString();
                if (password != Server.settings.password)
                {
                    Player.Disconnect(MpDisconnectReason.BadGamePassword);
                    return;
                }
            }

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
            var defTypeCount = data.ReadInt32();
            if (defTypeCount > 512)
            {
                Player.Disconnect("Too many defs");
                return;
            }

            var defsResponse = new ByteWriter();

            for (int i = 0; i < defTypeCount; i++)
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

            if (!defsMismatched)
            {
                if (Server.settings.pauseOnJoin)
                    Server.commands.PauseAll();

                if (Server.settings.autoJoinPoint.HasFlag(AutoJoinPointFlags.Join))
                    Server.TryStartJoinPointCreation();

                Server.playerManager.OnJoin(Player);
            }
        }

        [PacketHandler(Packets.Client_WorldRequest)]
        public void HandleWorldRequest(ByteReader data)
        {
            if (Server.CreatingJoinPoint)
            {
                Server.playerManager.playersWaitingForWorldData.Add(Player.id);
                return;
            }

            SendWorldData();
        }

        public void SendWorldData()
        {
            connection.Send(Packets.Server_WorldDataStart);

            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(Player.FactionId);
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

            writer.WriteInt32(Server.commands.SentCmds);
            writer.WriteBool(Server.freezeManager.Frozen);

            writer.WriteInt32(Server.syncInfos.Count);
            foreach (var syncInfo in Server.syncInfos)
                writer.WritePrefixedBytes(syncInfo);

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
