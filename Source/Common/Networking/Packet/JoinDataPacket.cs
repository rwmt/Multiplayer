namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Client_JoinData, allowFragmented: true)]
public record struct ClientJoinDataPacket : IPacket
{
    public RoundModeEnum modCtorRoundMode;
    public RoundModeEnum staticCtorRoundMode;
    public KeyedDefInfo[] defInfos;

    public void Bind(PacketBuffer buf)
    {
        buf.BindEnum(ref modCtorRoundMode);
        buf.BindEnum(ref staticCtorRoundMode);
        buf.Bind(ref defInfos, BinderOf.Identity<KeyedDefInfo>(), maxLength: 512);
    }
}

public record struct KeyedDefInfo : IPacketBufferable
{
    public string name;
    public int count;
    public int hash;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref name, maxLength: 128);
        buf.Bind(ref count);
        buf.Bind(ref hash);
    }
}
