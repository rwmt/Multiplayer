namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_Notification)]
public record struct ServerNotificationPacket(string key) : IPacket
{
    public string key = key;
    public string[] args = [];

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref key);
        buf.Bind(ref args, BinderOf.String());
    }
}

