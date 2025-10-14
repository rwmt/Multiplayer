namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_ProtocolOk)]
public record struct ServerProtocolOkPacket(bool hasPassword) : IPacket
{
    public bool hasPassword = hasPassword;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref hasPassword);
    }
}


[PacketDefinition(Packets.Client_Protocol)]
public record struct ClientProtocolPacket(int protocolVersion) : IPacket
{
    public int protocolVersion = protocolVersion;

    public static ClientProtocolPacket Current() => new(MpVersion.Protocol);

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref protocolVersion);
    }
}
