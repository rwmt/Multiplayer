namespace Multiplayer.Common.Networking.Packet;

/// <summary>
/// Sent by the server during the initial connection handshake.
/// When enabled, the server is running in "bootstrap" mode (no save loaded yet)
/// and the client should enter the configuration flow instead of normal join.
/// </summary>
[PacketDefinition(Packets.Server_Bootstrap)]
public record struct ServerBootstrapPacket(bool bootstrap) : IPacket
{
    public bool bootstrap = bootstrap;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref bootstrap);
    }
}
