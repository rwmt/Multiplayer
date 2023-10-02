using System;
using Multiplayer.Common;

namespace Multiplayer.Client;

public class ReplayConnection : ConnectionBase
{
    public static Action<ScheduledCommand> replayCmdEvent;

    public override void Send(Packets id, byte[] message, bool reliable = true)
    {
        if (id == Packets.Client_Command)
            replayCmdEvent?.Invoke(DeserializeCmd(new ByteReader(message)));
    }

    private static ScheduledCommand DeserializeCmd(ByteReader data)
    {
        CommandType cmd = (CommandType)data.ReadInt32();
        int mapId = data.ReadInt32();
        byte[] extraBytes = data.ReadPrefixedBytes()!;

        return new ScheduledCommand(cmd, 0, 0, mapId, 0, extraBytes);
    }

    protected override void SendRaw(byte[] raw, bool reliable)
    {
    }

    public override void HandleReceiveRaw(ByteReader data, bool reliable)
    {
    }

    public override void Close(MpDisconnectReason reason, byte[] data)
    {
    }
}
