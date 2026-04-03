using Multiplayer.Client.Networking;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Client;

public abstract class ClientBaseState(ConnectionBase connection) : MpConnectionState(connection)
{
    protected MultiplayerSession Session => Multiplayer.session;

    [TypedPacketHandler]
    public void HandleKeepAlive(ServerKeepAlivePacket packet)
    {
        int ticksBehind = TickPatch.tickUntil - TickPatch.Timer;

        connection.Send(new ClientKeepAlivePacket(packet.id, ticksBehind, TickPatch.Simulating, TickPatch.workTicks),
            false);
    }

    [TypedPacketHandler]
    public void HandleTimeControl(ServerTimeControlPacket packet)
    {
        if (Multiplayer.session.remoteTickUntil >= packet.tickUntil) return;

        TickPatch.serverTimePerTick = packet.serverTimePerTick;
        Multiplayer.session.remoteTickUntil = packet.tickUntil;
        Multiplayer.session.remoteSentCmds = packet.sentCmds;
        Multiplayer.session.ProcessTimeControl();
    }

    // Currently handles disconnection only for Steam connections. See comment in ConnectionBase.Close for more info.
    [TypedPacketHandler]
    public virtual void HandleDisconnected(ServerDisconnectPacket packet)
    {
        ConnectionStatusListeners.TryNotifyAll_Disconnected(SessionDisconnectInfo.From(packet));
        Multiplayer.StopMultiplayer();
    }
}
