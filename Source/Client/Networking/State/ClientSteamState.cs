using Multiplayer.Client.Networking;
using Multiplayer.Common;

namespace Multiplayer.Client
{
    public class ClientSteamState : MpConnectionState
    {
        public ClientSteamState(ConnectionBase connection) : base(connection)
        {
            var steamConn = connection as SteamBaseConn;

            // The flag byte is: joinPacket | reliable | hasChannel
            steamConn.SendRawSteam(ByteWriter.GetBytes((byte)0b111, steamConn.recvChannel), true);
        }

        [PacketHandler(Packets.Server_SteamAccept)]
        public void HandleSteamAccept(ByteReader data)
        {
            connection.State = ConnectionStateEnum.ClientJoining;
            connection.StateObj.StartState();
        }
    }

}
