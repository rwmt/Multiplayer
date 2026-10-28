using System.Threading;
using Multiplayer.Common;

namespace Tests;

[TestFixture]
public class FreezeManagerTest
{
    private MultiplayerServer server = null!;
    private int nextPlayerId;

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
    }

    [TearDown]
    public void TearDown()
    {
        MultiplayerServer.instance = null;
    }

    private ServerPlayer AddPlayer(string username, bool isHost = false)
    {
        var conn = new DummyConnection(username);
        var player = new ServerPlayer(nextPlayerId++, conn);
        conn.serverPlayer = player;
        conn.ChangeState(ConnectionStateEnum.ServerPlaying);

        if (isHost)
            server.hostUsername = username;

        server.playerManager.Players.Add(player);
        return player;
    }

    private void RemovePlayer(ServerPlayer player)
    {
        server.playerManager.Players.Remove(player);
    }

    [Test]
    public void HostPresent_HostFrozen_ServerFreezes()
    {
        var host = AddPlayer("host", isHost: true);
        host.frozen = true;

        server.freezeManager.Tick();

        Assert.That(server.freezeManager.Frozen, Is.True);
    }

    [Test]
    public void HostPresent_HostNotFrozen_ServerNotFrozen()
    {
        var host = AddPlayer("host", isHost: true);
        host.frozen = false;

        server.freezeManager.Tick();

        Assert.That(server.freezeManager.Frozen, Is.False);
    }

    [Test]
    public void HostAbsent_PlayersPresent_NotFrozen()
    {
        // Host was never added — only a non-host player is present
        AddPlayer("player1");

        // Force frozen state to true to verify it gets unfrozen
        // Use Tick with a temporary host to set Frozen = true
        var tempHost = AddPlayer("host", isHost: true);
        tempHost.frozen = true;
        server.freezeManager.Tick();
        Assert.That(server.freezeManager.Frozen, Is.True);

        // Now remove host — simulating disconnect
        RemovePlayer(tempHost);
        server.hostUsername = "host"; // host username stays but player is gone

        server.freezeManager.Tick();

        Assert.That(server.freezeManager.Frozen, Is.False);
    }

    [Test]
    public void HostAbsent_NoPlayers_StaysFrozen()
    {
        // Set Frozen = true by having a host freeze, then removing everyone
        var host = AddPlayer("host", isHost: true);
        host.frozen = true;
        server.freezeManager.Tick();
        Assert.That(server.freezeManager.Frozen, Is.True);

        RemovePlayer(host);

        server.freezeManager.Tick();

        Assert.That(server.freezeManager.Frozen, Is.True);
    }

    [Test]
    public void HostAbsent_NoPlayers_NotFrozenStaysNotFrozen()
    {
        // No players at all, not frozen — should remain not frozen
        // (no one to send the freeze packet to anyway)
        server.freezeManager.Tick();

        Assert.That(server.freezeManager.Frozen, Is.False);
    }

    [Test]
    public void TryStop_JoinsServerLoopThread()
    {
        // The #991 shutdown race is closed by having TryStop wait for the server
        // loop to finish before nulling the static instance. A fake loop verifies
        // the wait really happens: TryStop must not return before the loop did.
        var loopFinished = false;
        var fakeLoop = new Thread(() =>
        {
            Thread.Sleep(100);
            loopFinished = true;
        }) { IsBackground = true };
        server.serverThread = fakeLoop;
        fakeLoop.Start();

        server.TryStop();

        Assert.That(loopFinished, Is.True);
        Assert.That(fakeLoop.IsAlive, Is.False);
    }

    [Test]
    public void TryStop_FromLoopThreadItself_DoesNotDeadlock()
    {
        // Run() calls TryStop at its own exit; self-joining would hang forever.
        var completed = false;
        var loopThread = new Thread(() =>
        {
            server.serverThread = Thread.CurrentThread;
            server.TryStop(); // same thread as the loop we must not join
            completed = true;
        }) { IsBackground = true };
        loopThread.Start();

        // If self-join deadlocked, the loop thread would never reach completed=true
        // and the join with timeout below would observe an alive thread.
        Assert.That(loopThread.Join(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(completed, Is.True);
    }

    [Test]
    public void HostReconnects_ResumesNormalBehavior()
    {
        // Start with host, freeze
        var host = AddPlayer("host", isHost: true);
        host.frozen = true;
        server.freezeManager.Tick();
        Assert.That(server.freezeManager.Frozen, Is.True);

        // Host disconnects, player present → unfreezes
        RemovePlayer(host);
        var player = AddPlayer("player1");
        server.freezeManager.Tick();
        Assert.That(server.freezeManager.Frozen, Is.False);

        // Host reconnects and is not frozen → stays unfrozen
        var hostAgain = AddPlayer("host", isHost: true);
        hostAgain.frozen = false;
        server.freezeManager.Tick();
        Assert.That(server.freezeManager.Frozen, Is.False);

        // Host freezes again → server freezes
        hostAgain.frozen = true;
        server.freezeManager.Tick();
        Assert.That(server.freezeManager.Frozen, Is.True);
    }
}
