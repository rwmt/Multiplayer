namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Client_BootstrapSettings)]
public record struct ClientBootstrapSettingsPacket(ServerSettings settings) : IPacket
{
    public ServerSettings settings = settings;

    public void Bind(PacketBuffer buf)
    {
        ServerSettings.Binder(buf, ref settings);
    }
}

[PacketDefinition(Packets.Client_BootstrapSave, allowFragmented: true)]
public record struct ClientBootstrapSaveDataPacket(byte[] saveData, byte[] sha256Hash) : IPacket
{
    public byte[] saveData = saveData;
    public byte[] sha256Hash = sha256Hash;

    public void Bind(PacketBuffer buf)
    {
        buf.BindBytes(ref saveData, maxLength: -1);
        buf.BindBytes(ref sha256Hash, maxLength: 32);
    }
}
