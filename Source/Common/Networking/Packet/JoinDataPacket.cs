namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_JoinData, allowFragmented: true)]
public record struct ServerJoinDataPacket : IPacket
{
    public string gameName;
    public int playerId;
    public string rwVersion;
    public string mpVersion;
    public DefCheckStatus[] defStatus;
    public byte[] rawServerInitData;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref gameName);
        buf.Bind(ref playerId);
        buf.Bind(ref rwVersion);
        buf.Bind(ref mpVersion);
        buf.Bind(ref defStatus, BinderOf.Enum<DefCheckStatus>());
        buf.BindRemaining(ref rawServerInitData);
    }
}

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
