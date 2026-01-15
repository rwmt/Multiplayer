using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Client;

/// <summary>
/// Client connection state used while configuring a bootstrap server.
/// The server is in ServerBootstrap and expects upload packets; the client must keep the connection alive
/// and handle bootstrap completion / disconnect packets.
/// </summary>
[PacketHandlerClass(inheritHandlers: true)]
public class ClientBootstrapState(ConnectionBase connection) : ClientBaseState(connection)
{
    [TypedPacketHandler]
    public void HandleDisconnected(ServerDisconnectPacket packet)
    {
        // If bootstrap completed successfully, show success message before closing the window
        if (packet.reason == MpDisconnectReason.BootstrapCompleted)
        {
            OnMainThread.Enqueue(() => Verse.Messages.Message(
                "Bootstrap configuration completed. The server will now shut down; please restart it manually to start normally.",
                RimWorld.MessageTypeDefOf.PositiveEvent, false));
        }

        // Close the bootstrap configurator window now that the process is complete
        OnMainThread.Enqueue(() =>
        {
            var window = Verse.Find.WindowStack.WindowOfType<BootstrapConfiguratorWindow>();
            if (window != null)
                Verse.Find.WindowStack.TryRemove(window);
        });

        // Let the base class handle the disconnect
        base.HandleDisconnected(packet);
    }
}
