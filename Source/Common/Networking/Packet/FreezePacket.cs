namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_Freeze)]
public record struct ServerFreezePacket(bool frozen, int gameTimer) : IPacket
{
    public bool frozen = frozen;
    public int gameTimer = gameTimer;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref frozen);
        buf.Bind(ref gameTimer);
    }
}

[PacketDefinition(Packets.Client_Freeze)]
public record struct ClientFreezePacket(bool freeze) : IPacket
{
    public bool freeze = freeze;

    public static ClientFreezePacket Freeze() => new(true);
    public static ClientFreezePacket Unfreeze() => new(false);

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref freeze);
    }
}
