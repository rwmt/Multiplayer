namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_Cursor)]
public record struct ServerCursorPacket(int playerId, ClientCursorPacket data) : IPacket
{
    public int playerId = playerId;
    public ClientCursorPacket data = data;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref playerId);
        buf.Bind(ref data);
    }
}

[PacketDefinition(Packets.Client_Cursor)]
public record struct ClientCursorPacket(byte seq) : IPacket
{
    public byte seq = seq;
    /// byte.MaxValue signifies an absence of a specific map (e.g., when viewing the world).
    public byte map = byte.MaxValue;
    public byte icon;
    public float x, z;
    public float dragX, dragZ;

    public bool HasDrag => dragX != 0 && dragZ != 0;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref seq);
        buf.Bind(ref map);
        if (map < byte.MaxValue)
        {
            buf.Bind(ref icon);
            buf.BindWith(ref x, FloatAsShortBinder);
            buf.BindWith(ref z, FloatAsShortBinder);

            if (/* reading */ buf.DataRemaining || /* writing */ HasDrag)
            {
                buf.BindWith(ref dragX, FloatAsShortBinder);
                buf.BindWith(ref dragZ, FloatAsShortBinder);
            }
        }
    }

    private static readonly Binder<float> FloatAsShortBinder = (PacketBuffer buf, ref float f) =>
    {
        if (buf is PacketWriter { Writer: var writer }) writer.WriteShort((short)(f * 10f));
        else if (buf is PacketReader { Reader: var reader }) f = reader.ReadShort() / 10f;
    };
}
