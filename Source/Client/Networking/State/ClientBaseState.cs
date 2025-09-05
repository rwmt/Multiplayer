using Multiplayer.Common;

namespace Multiplayer.Client;

public abstract class ClientBaseState(ConnectionBase connection) : MpConnectionState(connection)
{
    protected MultiplayerSession Session => Multiplayer.session;

    protected void HandleKeepAlive(ByteReader data)
    {
        int id = data.ReadInt32();
        int ticksBehind = TickPatch.tickUntil - TickPatch.Timer;

        connection.Send(
            Packets.Client_KeepAlive,
            ByteWriter.GetBytes(id, ticksBehind, TickPatch.Simulating, TickPatch.workTicks),
            false
        );
    }

    protected void HandleTimeControl(ByteReader data)
    {
        int tickUntil = data.ReadInt32();
        int sentCmds = data.ReadInt32();
        float stpt = data.ReadFloat();

        if (Multiplayer.session.remoteTickUntil >= tickUntil) return;

        TickPatch.serverTimePerTick = stpt;
        Multiplayer.session.remoteTickUntil = tickUntil;
        Multiplayer.session.remoteSentCmds = sentCmds;
        Multiplayer.session.ProcessTimeControl();
    }
}
