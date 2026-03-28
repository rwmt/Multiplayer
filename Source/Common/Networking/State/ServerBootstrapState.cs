using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common;

[PacketHandlerClass]
public class ServerBootstrapState(ConnectionBase connection) : MpConnectionState(connection)
{
    public override void StartState()
    {
        if (!Server.BootstrapMode)
        {
            connection.ChangeState(ConnectionStateEnum.ServerJoining);
            return;
        }

        connection.Send(new ServerBootstrapPacket(true));
    }
}