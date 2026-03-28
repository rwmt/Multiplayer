namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_Bootstrap)]
public record struct ServerBootstrapPacket(bool bootstrap, bool settingsMissing = false, bool saveMissing = false) : IPacket
{
    public bool bootstrap = bootstrap;
    public bool settingsMissing = settingsMissing;
    public bool saveMissing = saveMissing;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref bootstrap);
        buf.Bind(ref settingsMissing);
        buf.Bind(ref saveMissing);
    }
}