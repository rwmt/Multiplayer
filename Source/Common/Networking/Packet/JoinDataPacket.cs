using System.Collections.Generic;

namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_JoinData, allowFragmented: true)]
public record struct ServerJoinDataPacket : IPacket
{
    public const int MaxMissingDefs = 512;

    public string gameName;
    public int playerId;
    public string rwVersion;
    public string mpVersion;
    // Aligned with the client's def infos from ClientJoinDataPacket
    public DefCheckStatus[] defStatus;
    // Def types the server knows but the client didn't send
    public string[] missingDefs;
    public bool configsIncluded;
    public byte[] rawServerInitData;

    public List<ClientInitDataPacket.ModData> ServerInitData
    {
        get => ClientInitDataPacket.ModData.ListBinder.Deserialize(rawServerInitData);
        set => rawServerInitData = ClientInitDataPacket.ModData.ListBinder.Serialize(value);
    }

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref gameName);
        buf.Bind(ref playerId);
        buf.Bind(ref rwVersion);
        buf.Bind(ref mpVersion);
        buf.Bind(ref defStatus, BinderOf.Enum<DefCheckStatus>());
        buf.Bind(ref missingDefs, MissingDefBinder, maxLength: MaxMissingDefs);
        buf.Bind(ref configsIncluded);
        // The server forwards the first joiner's (or host's) ClientInitDataPacket.rawMods, so the limits must match.
        // As an example, one game with ~100 mods used ~35KiB, synced configs can add a lot more.
        buf.BindRemaining(ref rawServerInitData, maxLength: ClientInitDataPacket.MaxRawModsLength);
    }

    private static readonly Binder<string> MissingDefBinder = (PacketBuffer buf, ref string name) =>
        buf.Bind(ref name, maxLength: KeyedDefInfo.MaxNameLength);
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
    public const int MaxNameLength = 128;

    public string name;
    public int count;
    public int hash;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref name, maxLength: MaxNameLength);
        buf.Bind(ref count);
        buf.Bind(ref hash);
    }
}
