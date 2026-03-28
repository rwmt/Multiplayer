namespace Multiplayer.Client;

[PacketHandlerClass(inheritHandlers: true)]
public class ClientBootstrapState(ConnectionBase connection) : ClientBaseState(connection)
{
}