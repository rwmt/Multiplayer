using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Client;

[PacketHandlerClass(inheritHandlers: true)]
public class ClientBootstrapState(ConnectionBase connection) : ClientBaseState(connection)
{
	[TypedPacketHandler]
	public void HandleBootstrap(ServerBootstrapPacket packet)
	{
		OnMainThread.Enqueue(() => BootstrapConfiguratorWindow.Instance?.ApplyBootstrapState(BootstrapServerState.FromPacket(packet)));
	}
}
