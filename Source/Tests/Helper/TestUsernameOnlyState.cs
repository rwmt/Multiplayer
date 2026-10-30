using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

// Gets as far as claiming a username and then stays put, so tests have a connection for a later
// client to collide with.
public class TestUsernameOnlyState : AsyncConnectionState
{
    public TestUsernameOnlyState(ConnectionBase connection) : base(connection)
    {
    }

    // Deliberately registers no packet handlers: handlers accumulate globally per connection state, so
    // declaring one here would clash with the state a second client in the same test installs.
    protected override async Task RunState()
    {
        connection.Send(ClientProtocolPacket.Current());
        await TypedPacket<ServerProtocolOkPacket>();

        connection.Send(new ClientUsernamePacket(connection.username!));
        await TypedPacket<ServerInitDataRequestPacket>();

        // Left unanswered, which parks the connection here still holding the username.
        await TypedPacket<ServerJoinDataPacket>();
    }
}
