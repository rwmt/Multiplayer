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
    public void HandleBootstrapComplete(ServerBootstrapCompletePacket packet)
    {
        // The server will close shortly after sending this. Surface the message as an in-game notification.
        // (BootstrapConfiguratorWindow already tells the user what to do next.)
        if (!string.IsNullOrWhiteSpace(packet.message))
            OnMainThread.Enqueue(() => Verse.Messages.Message(packet.message, RimWorld.MessageTypeDefOf.PositiveEvent, false));

        // Close the bootstrap configurator window now that the process is complete
        OnMainThread.Enqueue(() =>
        {
            var window = Verse.Find.WindowStack.WindowOfType<BootstrapConfiguratorWindow>();
            if (window != null)
                Verse.Find.WindowStack.TryRemove(window);
        });
    }
}
