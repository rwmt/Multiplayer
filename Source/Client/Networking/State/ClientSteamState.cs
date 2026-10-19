using Multiplayer.Client.Networking;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Client
{
    public class ClientSteamState : MpConnectionState
    {
        private readonly string username;

        public ClientSteamState(SteamBaseConn conn, string username) : base(conn)
        {
            this.username = username;
            // The flag byte is: joinPacket | reliable | hasChannel
            conn.SendRawSteam(ByteWriter.GetBytes((byte)0b111, conn.recvChannel), true);
        }

        [PacketHandler(Packets.Server_SteamAccept)]
        public void HandleSteamAccept(ByteReader data)
        {
            connection.ChangeState(new ClientJoiningState(connection, username));
        }

        // The server turned the connection away (server full, starting, throttled)
        [TypedPacketHandler]
        public void HandleDisconnected(ServerDisconnectPacket packet)
        {
            ConnectionStatusListeners.TryNotifyAll_Disconnected(SessionDisconnectInfo.From(packet));
            Multiplayer.StopMultiplayer();
        }
    }

}
