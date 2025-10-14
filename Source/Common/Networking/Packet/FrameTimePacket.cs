namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Client_FrameTime)]
public record struct ClientFrameTimePacket(float frameTime) : IPacket
{
    public float frameTime = frameTime;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref frameTime);
    }
}
