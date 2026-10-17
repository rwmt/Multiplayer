using System.Collections.Generic;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

public class RecordingConnection : ConnectionBase
{
    public List<Packets> SentPackets { get; } = new();
    // Payloads of non-fragmented packets
    public List<(Packets id, byte[] data)> SentMessages { get; } = new();
    public List<byte[]> SentRaw { get; } = new();

    public RecordingConnection(string username)
    {
        this.username = username;
    }

    public override int Latency { get => 0; set { } }

    protected override void SendRaw(byte[] raw, bool reliable)
    {
        if (raw.Length == 0)
            return;

        SentPackets.Add((Packets)(raw[0] & 0x3F));
        if ((raw[0] & 0xC0) == 0)
            SentMessages.Add(((Packets)(raw[0] & 0x3F), raw[1..]));
        SentRaw.Add(raw);
    }

    protected override void OnClose(ServerDisconnectPacket? goodbye) { }
}