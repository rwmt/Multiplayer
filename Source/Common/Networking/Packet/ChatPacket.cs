namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Server_Chat)]
public record struct ServerChatPacket : IPacket
{
    public string msg;
    public bool rawMessage;

    public static ServerChatPacket Create(string msg) => new() { msg = msg.Trim() };
    public static ServerChatPacket CreateRaw(string msg) => new() { msg = msg.Trim(), rawMessage = true };

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref msg);
        if (buf.isWriting || buf.DataRemaining)
            buf.Bind(ref rawMessage);
    }
}

[PacketDefinition(Packets.Client_Chat)]
public record struct ClientChatPacket : IPacket
{
    public string msg;
    public bool helpOnlyUsableCommands;

    public static ClientChatPacket Create(string msg, bool helpOnlyUsableCommands = false) => new()
    {
        msg = msg.Trim(),
        helpOnlyUsableCommands = helpOnlyUsableCommands
    };

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref msg);
        if (buf.isWriting || buf.DataRemaining)
            buf.Bind(ref helpOnlyUsableCommands);
    }
}
