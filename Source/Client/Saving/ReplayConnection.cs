using Multiplayer.Common;

namespace Multiplayer.Client;

public class ReplayConnection : ConnectionBase
{
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
