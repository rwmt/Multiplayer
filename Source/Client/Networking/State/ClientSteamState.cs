using Multiplayer.Client.Networking;
using Multiplayer.Common;

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
    }

}
