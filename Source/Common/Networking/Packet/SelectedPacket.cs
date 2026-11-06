namespace Multiplayer.Common.Networking.Packet;


[PacketDefinition(Packets.Server_Selected)]
public record struct ServerSelectedPacket(int playerId, ClientSelectedPacket data) : IPacket
{
    public int playerId = playerId;
    public ClientSelectedPacket data = data;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref playerId);
        buf.Bind(ref data);
    }
}

[PacketDefinition(Packets.Client_Selected)]
public record struct ClientSelectedPacket : IPacket
{
    public bool reset;
    public int[] newlySelectedIds;
    public int[] unselectedIds;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref reset);
        // Vanilla limits the amount of selected items to 200, but mods can increase that
        buf.Bind(ref newlySelectedIds, BinderOf.Int(), maxLength: 8000);
        buf.Bind(ref unselectedIds, BinderOf.Int(), maxLength: 8000);
    }
}
