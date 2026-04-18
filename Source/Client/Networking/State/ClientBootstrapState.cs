using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using Verse;

namespace Multiplayer.Client;

[PacketHandlerClass(inheritHandlers: true)]
public class ClientBootstrapState(ConnectionBase connection) : ClientBaseState(connection)
{
	[TypedPacketHandler]
	public void HandleBootstrap(ServerBootstrapPacket packet)
	{
		OnMainThread.Enqueue(() => BootstrapConfiguratorWindow.Instance?.ApplyBootstrapState(BootstrapServerState.FromPacket(packet)));
	}

	[TypedPacketHandler]
	public override void HandleDisconnected(ServerDisconnectPacket packet)
	{
		if (packet.reason == MpDisconnectReason.BootstrapCompleted)
		{
			OnMainThread.Enqueue(() => Messages.Message(
				"Bootstrap configuration completed. The server will now shut down; please restart it manually to start normally.",
				MessageTypeDefOf.PositiveEvent, false));
		}

		OnMainThread.Enqueue(() =>
		{
			var window = Find.WindowStack.WindowOfType<BootstrapConfiguratorWindow>();
			if (window != null)
			{
				window.ResetTransientUiState(resetServerDrivenState: true);
				Find.WindowStack.TryRemove(window);
			}
		});

		base.HandleDisconnected(packet);
	}
}
