namespace Multiplayer.Common.Networking.Packet;

/// tick: the game timer of the saved state. With in-game day autosaves every client saves at the same tick and asks
/// for a join point once its save is written, the server starts one per tick.
[PacketDefinition(Packets.Client_Autosaving)]
public record struct ClientAutosavingPacket(JoinPointRequestReason reason, int tick) : IPacket
{
    public JoinPointRequestReason reason = reason;
    public int tick = tick;

    public void Bind(PacketBuffer buf)
    {
        buf.BindEnum(ref reason);
        buf.Bind(ref tick);
    }
}
