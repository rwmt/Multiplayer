namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Client_Username)]
public record struct ClientUsernamePacket(string username, string? password = null) : IPacket
{
    public string username = username;
    public string? password = password;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref username);
        if (/* reading */ buf.DataRemaining || /* writing */ password != null)
            buf.Bind(ref password!);
    }
}
