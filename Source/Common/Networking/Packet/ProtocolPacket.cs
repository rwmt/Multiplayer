namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_ProtocolOk)]
public record struct ServerProtocolOkPacket(bool hasPassword, bool isStandaloneServer = false) : IPacket
{
    public bool hasPassword = hasPassword;
    public bool isStandaloneServer = isStandaloneServer;
    public float autosaveInterval;
    public AutosaveUnit autosaveUnit;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref hasPassword);
        buf.Bind(ref isStandaloneServer);
        buf.Bind(ref autosaveInterval);
        buf.BindEnum(ref autosaveUnit);
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
