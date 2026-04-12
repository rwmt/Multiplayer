namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_InitDataRequest)]
public record struct ServerInitDataRequestPacket(bool includeConfigs) : IPacket
{
    public bool includeConfigs = includeConfigs;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref includeConfigs);
    }
}

[PacketDefinition(Packets.Client_InitData, allowFragmented: true)]
public record struct ClientInitDataPacket : IPacket
{
    // 1 MB limit — large mod lists can exceed the default 32 KB BindRemaining limit,
    // but 32 MiB (MaxFragmentPacketTotalSize) is excessive.
    // Subtract 6 bytes for SendFragmented's first-fragment overhead.
    private const int MaxRawDataLength = (1 << 20) - 6;

    public string rwVersion;
    public int[] debugOnlySyncCmds;
    public int[] hostOnlySyncCmds;
    public RoundModeEnum modCtorRoundMode;
    public RoundModeEnum staticCtorRoundMode;
    public KeyedDefInfo[] defInfos;
    public byte[] rawData;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref rwVersion);
        buf.Bind(ref debugOnlySyncCmds, BinderOf.Int());
        buf.Bind(ref hostOnlySyncCmds, BinderOf.Int());
        buf.BindEnum(ref modCtorRoundMode);
        buf.BindEnum(ref staticCtorRoundMode);
        buf.Bind(ref defInfos, BinderOf.Identity<KeyedDefInfo>());
        buf.BindRemaining(ref rawData, maxLength: MaxRawDataLength);
    }
}
