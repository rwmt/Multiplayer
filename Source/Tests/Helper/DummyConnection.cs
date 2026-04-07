using Multiplayer.Common;

namespace Tests;

public class DummyConnection : ConnectionBase
{
    public DummyConnection(string username)
    {
        this.username = username;
    }

    public override int Latency { get => 0; set { } }

    protected override void SendRaw(byte[] raw, bool reliable) { }
    protected override void OnClose(Multiplayer.Common.Networking.Packet.ServerDisconnectPacket? goodbye) { }
}
