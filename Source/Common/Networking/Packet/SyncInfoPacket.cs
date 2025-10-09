using System.Collections.Generic;

namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Client_SyncInfo, allowFragmented: true)]
public record struct ClientSyncInfoPacket : IPacket
{
    // 1MiB. For some big saves, the sync opinion can reach ~650KiB, so this includes some leeway.
    public const int MaxLength = 1 << 20;
    public byte[] rawSyncOpinion;

    public SyncOpinion SyncOpinion
    {
        get => SyncOpinionBinder.Deserialize(rawSyncOpinion);
        set => rawSyncOpinion = SyncOpinionBinder.Serialize(value);
    }

    public void Bind(PacketBuffer buf)
    {
        buf.BindRemaining(ref rawSyncOpinion, maxLength: MaxLength);
    }

    private static readonly Binder<SyncOpinion> SyncOpinionBinder = BinderOf.Identity<SyncOpinion>();
}

[PacketDefinition(Packets.Server_SyncInfo, allowFragmented: true)]
public record struct ServerSyncInfoPacket : IPacket
{
    public byte[] rawSyncOpinion;
    public SyncOpinion SyncOpinion
    {
        get => SyncOpinionBinder.Deserialize(rawSyncOpinion);
        set => rawSyncOpinion = SyncOpinionBinder.Serialize(value);
    }

    public void Bind(PacketBuffer buf)
    {
        buf.BindRemaining(ref rawSyncOpinion, maxLength: ClientSyncInfoPacket.MaxLength);
    }

    private static readonly Binder<SyncOpinion> SyncOpinionBinder = BinderOf.Identity<SyncOpinion>();
}

public record struct SyncOpinion : IPacketBufferable
{
    public int startTick;
    public List<uint> commandRandomStates;
    public List<uint> worldRandomStates;
    public List<MapRandomState> mapRandomStates;
    public List<int> traceHashes;
    public bool simulating;
    public RoundModeEnum roundMode;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref startTick);

        buf.Bind(ref commandRandomStates, BinderOf.UInt());
        buf.Bind(ref worldRandomStates, BinderOf.UInt());
        buf.Bind(ref mapRandomStates, BinderOf.Identity<MapRandomState>());
        // Max 262k (256Ki) trace hashes. For some big saves, the trace hashes can reach ~60k entries, so this includes
        // some leeway.
        buf.Bind(ref traceHashes, BinderOf.Int(), maxLength: 1<<18);

        buf.Bind(ref simulating);
        buf.BindEnum(ref roundMode);
    }
}

public record struct MapRandomState : IPacketBufferable
{
    public int mapId;
    public List<uint> randomStates;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref mapId);
        buf.Bind(ref randomStates, BinderOf.UInt());
    }
}
