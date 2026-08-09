using Multiplayer.Common;

namespace Tests;

[TestFixture]
public class PauseGraceTest
{
    private MultiplayerServer server = null!;
    private int nextPlayerId;
    private readonly List<ServerPlayer> playersBehind = new();

    [SetUp]
    public void SetUp()
    {
        server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings
        {
            gameName = "Test",
            direct = false,
            lan = false
        });
        nextPlayerId = 1;
        playersBehind.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        MultiplayerServer.instance = null;
    }

    private ServerPlayer AddPlayingPlayer(string username)
    {
        var conn = new DummyConnection(username);
        var player = new ServerPlayer(nextPlayerId++, conn);
        conn.serverPlayer = player;
        conn.ChangeState(ConnectionStateEnum.ServerPlaying);
        server.playerManager.Players.Add(player);
        player.UpdateStatus(PlayerStatus.Playing);
        return player;
    }

    private void AdvanceNetTicks(int ticks)
    {
        for (int i = 0; i < ticks; i++)
            server.TickNet();
    }

    private void Evaluate() => server.EvaluatePlayersBehind(playersBehind);

    // ExtrapolatedTicksBehind = ticksBehind + (gameTimer - ticksBehindReceivedAt);
    // gameTimer never advances in these tests, so ticksBehind is the whole value
    private static void SetBehind(ServerPlayer player, int ticks)
    {
        player.ticksBehind = ticks;
        player.ticksBehindReceivedAt = 0;
    }

    [Test]
    public void Behind_WithinGrace_NoPause()
    {
        var player = AddPlayingPlayer("player");
        SetBehind(player, 100);

        Evaluate();
        Assert.That(player.behindSinceNetTimer, Is.GreaterThanOrEqualTo(0), "timer should arm");
        Assert.That(playersBehind, Is.Empty);

        // Grace uses a strict > comparison: exactly PauseGraceNetTicks elapsed is still within grace
        AdvanceNetTicks(MultiplayerServer.PauseGraceNetTicks);
        Evaluate();
        Assert.That(playersBehind, Is.Empty);
    }

    [Test]
    public void Behind_PastGrace_PauseFires()
    {
        var player = AddPlayingPlayer("player");
        SetBehind(player, 100);

        Evaluate();
        AdvanceNetTicks(MultiplayerServer.PauseGraceNetTicks + 1);
        Evaluate();

        Assert.That(playersBehind, Is.EqualTo(new[] { player }));
    }

    [Test]
    public void Recovery_ResetsTimer_GraceRestartsOnRelapse()
    {
        var player = AddPlayingPlayer("player");
        SetBehind(player, 100);
        Evaluate();
        AdvanceNetTicks(600);

        SetBehind(player, 0);
        Evaluate();
        Assert.That(player.behindSinceNetTimer, Is.EqualTo(-1), "recovery should reset the timer");

        // Falling behind again must arm a fresh grace, not resume the old one
        SetBehind(player, 100);
        Evaluate();
        AdvanceNetTicks(MultiplayerServer.PauseGraceNetTicks);
        Evaluate();
        Assert.That(playersBehind, Is.Empty);

        AdvanceNetTicks(1);
        Evaluate();
        Assert.That(playersBehind, Is.EqualTo(new[] { player }));
    }

    [Test]
    public void RejoinWhileArmed_GraceRestarts()
    {
        var player = AddPlayingPlayer("player");
        SetBehind(player, 100);
        Evaluate();
        Assert.That(player.behindSinceNetTimer, Is.GreaterThanOrEqualTo(0));

        // Rejoin changes only the connection state - status stays Playing.
        // Start a join point first (as PlayerManager does on rejoin) so the
        // loading state genuinely pends instead of completing synchronously.
        server.worldData.TryStartJoinPointCreation(force: true);
        player.conn.ChangeState(ConnectionStateEnum.ServerLoading);
        AdvanceNetTicks(MultiplayerServer.PauseGraceNetTicks + 100);
        Evaluate();
        Assert.That(player.behindSinceNetTimer, Is.EqualTo(-1), "timer should reset while rejoining");
        Assert.That(playersBehind, Is.Empty);

        // Back in play, still behind: fresh grace, no instant pause
        player.conn.ChangeState(ConnectionStateEnum.ServerPlaying);
        Evaluate();
        Assert.That(playersBehind, Is.Empty, "returning player must get a fresh grace");

        AdvanceNetTicks(MultiplayerServer.PauseGraceNetTicks + 1);
        Evaluate();
        Assert.That(playersBehind, Is.EqualTo(new[] { player }));
    }

    [Test]
    public void DesyncWhileArmed_GraceRestarts()
    {
        var player = AddPlayingPlayer("player");
        SetBehind(player, 100);
        Evaluate();
        Assert.That(player.behindSinceNetTimer, Is.GreaterThanOrEqualTo(0));

        // Desync changes the status but not the connection state
        player.UpdateStatus(PlayerStatus.Desynced);
        AdvanceNetTicks(MultiplayerServer.PauseGraceNetTicks + 100);
        Evaluate();
        Assert.That(player.behindSinceNetTimer, Is.EqualTo(-1), "timer should reset while desynced");

        player.UpdateStatus(PlayerStatus.Playing);
        Evaluate();
        Assert.That(playersBehind, Is.Empty, "recovered player must get a fresh grace");

        AdvanceNetTicks(MultiplayerServer.PauseGraceNetTicks + 1);
        Evaluate();
        Assert.That(playersBehind, Is.EqualTo(new[] { player }));
    }

    [Test]
    public void TimersArePerPlayer()
    {
        var laggard = AddPlayingPlayer("laggard");
        var healthy = AddPlayingPlayer("healthy");
        SetBehind(laggard, 100);
        SetBehind(healthy, 0);

        Evaluate();
        AdvanceNetTicks(MultiplayerServer.PauseGraceNetTicks + 1);
        Evaluate();
        Assert.That(playersBehind, Is.EqualTo(new[] { laggard }));
        Assert.That(healthy.behindSinceNetTimer, Is.EqualTo(-1));

        // The second player arms its own timer at the current NetTimer, not the first player's
        SetBehind(healthy, 100);
        Evaluate();
        Assert.That(playersBehind, Is.EqualTo(new[] { laggard }), "healthy player is within its own grace");

        AdvanceNetTicks(MultiplayerServer.PauseGraceNetTicks + 1);
        Evaluate();
        Assert.That(playersBehind, Is.EquivalentTo(new[] { laggard, healthy }));
    }
}
