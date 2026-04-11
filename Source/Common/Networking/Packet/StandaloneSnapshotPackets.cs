namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Client_StandaloneWorldSnapshotUpload, allowFragmented: true)]
public record struct ClientStandaloneWorldSnapshotPacket : IPacket
{
    public int tick;
    public int leaseVersion;
    public byte[] worldData;
    public byte[] sessionData;
    public byte[] sha256Hash;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref tick);
        buf.Bind(ref leaseVersion);
        buf.BindBytes(ref worldData, maxLength: -1);
        buf.BindBytes(ref sessionData, maxLength: -1);
        buf.BindBytes(ref sha256Hash, maxLength: 32);
    }
}

[PacketDefinition(Packets.Client_StandaloneMapSnapshotUpload, allowFragmented: true)]
public record struct ClientStandaloneMapSnapshotPacket : IPacket
{
    public int mapId;
    public int tick;
    public int leaseVersion;
    public byte[] mapData;
    public byte[] sha256Hash;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref mapId);
        buf.Bind(ref tick);
        buf.Bind(ref leaseVersion);
        buf.BindBytes(ref mapData, maxLength: -1);
        buf.BindBytes(ref sha256Hash, maxLength: 32);
    }
}