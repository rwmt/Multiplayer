namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Client_Autosaving)]
public record struct ClientAutosavingPacket(JoinPointRequestReason reason) : IPacket
{
    public JoinPointRequestReason reason = reason;

    public void Bind(PacketBuffer buf)
    {
        buf.BindEnum(ref reason);
    }
}