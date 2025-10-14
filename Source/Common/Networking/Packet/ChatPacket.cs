namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_Chat)]
public record struct ServerChatPacket : IPacket
{
    public string msg;

    public static ServerChatPacket Create(string msg) => new() { msg = msg.Trim() };

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref msg);
    }
}

[PacketDefinition(Packets.Client_Chat)]
public record struct ClientChatPacket : IPacket
{
    public string msg;

    public static ClientChatPacket Create(string msg) => new() { msg = msg.Trim() };

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref msg);
    }
}
