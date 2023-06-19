using Multiplayer.Common;

namespace Multiplayer.Client;

public abstract class ClientBaseState : MpConnectionState
{
    protected MultiplayerSession Session => Multiplayer.session;

    public ClientBaseState(ConnectionBase connection) : base(connection)
    {
    }
}
