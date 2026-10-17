namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_Command)]
public record struct ServerCommandPacket : IPacket
{
    // type + ticks + factionId + mapId + playerId
    private const int HeaderSize = 1 + 4 * 4;
    // Largest data that still fits the rebroadcast in a single packet
    public const int MaxDataLength = ConnectionBase.MaxSinglePacketSize - HeaderSize;

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

[PacketDefinition(Packets.Client_Command)]
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
