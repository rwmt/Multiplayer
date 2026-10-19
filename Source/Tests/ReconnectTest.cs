using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

[TestFixture]
public class ReconnectTest
{
    private long now;

    [SetUp]
    public void SetUp() => now = 0;

    [Test]
    public void PendingCloseIsDueAfterItsDelayOnce()
    {
        var closes = new PendingSessionCloses<int>(() => now);
        closes.Schedule(1, 3000);

        now = 2999;
        Assert.That(closes.TakeDue(), Is.Empty);

        now = 3000;
        Assert.That(closes.TakeDue(), Is.EqualTo(new[] { 1 }));
        Assert.That(closes.TakeDue(), Is.Empty);
        Assert.That(closes.Count, Is.Zero);
    }

    [Test]
    public void CancelledCloseNeverComesDue()
    {
        var closes = new PendingSessionCloses<int>(() => now);
        closes.Schedule(1, 3000);
        closes.Schedule(2, 3000);

        Assert.That(closes.Cancel(1), Is.True);
        Assert.That(closes.Cancel(1), Is.False);

        now = 5000;
        Assert.That(closes.TakeDue(), Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void RescheduledCloseWaitsForTheLaterGoodbye()
    {
        var closes = new PendingSessionCloses<int>(() => now);
        closes.Schedule(1, 3000);

        now = 2000;
        closes.Schedule(1, 3000);

        now = 4000;
        Assert.That(closes.TakeDue(), Is.Empty);

        now = 5000;
        Assert.That(closes.TakeDue(), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void ReplaceStaleDropsTheOldConnectionWithoutClosingIt()
    {
        var server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings
        {
            gameName = "Test", direct = false, lan = false
        });

        try
        {
            var other = AddPlayer(server, 0, "other");
            var stale = AddPlayer(server, 1, "stale");
            var replacement = new RecordingConnection("stale");

            server.playerManager.ReplaceStale(stale, replacement);

            Assert.That(server.playerManager.Players, Is.EqualTo(new[] { other }));
            Assert.That(stale.conn.State, Is.EqualTo(ConnectionStateEnum.Disconnected));
            // A goodbye would reach the reconnecting client on transports which share the session (Steam)
            Assert.That(((RecordingConnection)stale.conn).Goodbye, Is.Null);
            Assert.That(((RecordingConnection)stale.conn).SentPackets, Is.Empty);
            Assert.That(((RecordingConnection)other.conn).SentPackets, Does.Contain(Packets.Server_PlayerList));
        }
        finally
        {
            MultiplayerServer.instance = null;
        }
    }

    private static ServerPlayer AddPlayer(MultiplayerServer server, int id, string username)
    {
        var conn = new RecordingConnection(username);
        var player = new ServerPlayer(id, conn) { hasJoined = true, status = PlayerStatus.Playing };
        conn.serverPlayer = player;
        conn.ChangeState(ConnectionStateEnum.ServerPlaying);
        server.playerManager.Players.Add(player);
        return player;
    }
}
