namespace Multiplayer.Common.Networking.Packet;

// Commands which don't fit a single packet (designating or pasting big areas, reforming an ideo) are sent fragmented
[PacketDefinition(Packets.Server_Command, allowFragmented: true)]
public record struct ServerCommandPacket : IPacket
{
    // Commands go into the log joiners receive with the world data, which has to fit a fragmented packet
    // (ConnectionBase.MaxFragmentedMessageLength) together with the save, so keep them well below that
    public const int MaxDataLength = 1024 * 1024;

    public CommandType type;
    public int ticks;
    public int factionId;
    public int mapId;
    public int playerId;
    public byte[] data;

    public static ServerCommandPacket From(ScheduledCommand cmd) => new()
    {
        type = cmd.type,
        ticks = cmd.ticks,
        factionId = cmd.factionId,
        mapId = cmd.mapId,
        playerId = cmd.playerId,
        data = cmd.data
    };

    public ScheduledCommand ToCommand() => new(type: type, ticks: ticks, factionId: factionId, mapId: mapId,
        playerId: playerId, data: data);

    public void Bind(PacketBuffer buf)
    {
        buf.BindEnum(ref type);
        buf.Bind(ref ticks);
        buf.Bind(ref factionId);
        buf.Bind(ref mapId);
        buf.Bind(ref playerId);
        buf.BindRemaining(ref data, maxLength: MaxDataLength);
    }
}

[PacketDefinition(Packets.Client_Command, allowFragmented: true)]
public record struct ClientCommandPacket(CommandType type, int mapId, byte[] data) : IPacket
{
    public CommandType type = type;
    public int mapId = mapId;
    public byte[] data = data;

    public void Bind(PacketBuffer buf)
    {
        buf.BindEnum(ref type);
        buf.Bind(ref mapId);
        // Capped by what the server can rebroadcast, see ServerCommandPacket
        buf.BindRemaining(ref data, maxLength: ServerCommandPacket.MaxDataLength);
    }
}
