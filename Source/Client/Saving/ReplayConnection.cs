using System;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Client;

public class ReplayConnection : ConnectionBase
{
    public static Action<ScheduledCommand> replayCmdEvent;

    protected override void Send(Packets id, byte[] message, bool reliable = true)
    {
        if (id == Packets.Client_Command)
        {
            var packet = new ClientCommandPacket();
            packet.Bind(new PacketReader(new ByteReader(message)));
            replayCmdEvent?.Invoke(new ScheduledCommand(packet.type, 0, 0, packet.mapId, 0, packet.data));
        }
    }

    protected override void SendRaw(byte[] raw, bool reliable)
    {
    }

    public override void HandleReceiveRaw(ByteReader data, bool reliable)
    {
    }

    protected override void OnClose()
    {
    }
}
