using Multiplayer.Common;

namespace Tests;

[TestFixture]
[NonParallelizable]
public class ServerStopTest
{
    private class StopRecorder : INetManager
    {
        public readonly List<int> stopThreads = new();
        public volatile bool ticked;

        public void Tick() => ticked = true;
        public void Stop() { lock (stopThreads) stopThreads.Add(Environment.CurrentManagedThreadId); }
        public string GetDiagnosticsName() => "test";
        public string? GetDiagnosticsInfo() => null;
    }

    private static MultiplayerServer MakeServer(StopRecorder recorder)
    {
        var server = new MultiplayerServer(new ServerSettings { gameName = "Test", direct = false, lan = false })
        {
            running = true
        };
        server.netManagers.Add(recorder);
        return server;
    }

    [TearDown]
    public void TearDown() => MultiplayerServer.instance = null;

    [Test]
    public void StopFromAnotherThreadIsDoneByTheServerThreadOnce()
    {
        var recorder = new StopRecorder();
        var server = MultiplayerServer.instance = MakeServer(recorder);
        var thread = new Thread(server.Run) { IsBackground = true };
        thread.Start();
        SpinWait.SpinUntil(() => recorder.ticked, 2000);

        server.TryStop();
        server.TryStop();

        Assert.That(thread.IsAlive, Is.False);
        Assert.That(recorder.stopThreads, Is.EqualTo(new[] { thread.ManagedThreadId }));
        Assert.That(MultiplayerServer.instance, Is.Null);
    }

    [Test]
    public void ServerStoppingLateKeepsTheNewerInstance()
    {
        var old = MakeServer(new StopRecorder());
        var newer = MultiplayerServer.instance = MakeServer(new StopRecorder());

        old.TryStop();

        Assert.That(MultiplayerServer.instance, Is.SameAs(newer));
    }

    [Test]
    public void ServerThreadExitStopsTheServer()
    {
        var recorder = new StopRecorder();
        var server = MultiplayerServer.instance = MakeServer(recorder);
        var thread = new Thread(server.Run) { IsBackground = true };
        thread.Start();
        SpinWait.SpinUntil(() => recorder.ticked, 2000);

        // What the bootstrap and the stop command do from the server thread
        server.running = false;

        Assert.That(thread.Join(2000), Is.True);
        Assert.That(recorder.stopThreads, Is.EqualTo(new[] { thread.ManagedThreadId }));
        Assert.That(MultiplayerServer.instance, Is.Null);
    }
}
