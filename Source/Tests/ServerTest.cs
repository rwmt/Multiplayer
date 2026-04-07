using System.Diagnostics;
using LiteNetLib;
using Multiplayer.Common;

namespace Tests;

[NonParallelizable]
public class ServerTest
{
    private List<Action> teardownActions = new();

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        ServerLog.detailEnabled = true;
        ServerLog.verboseEnabled = true;
        ServerLog.error = Assert.Fail;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var action in teardownActions)
            action();
        teardownActions.Clear();
    }

    [Test]
    public void Test()
    {
        var server = MakeServer(out var port);
        ConnectClient(port, typeof(TestJoiningState));

        var timeoutWatch = Stopwatch.StartNew();
        while (true)
        {
            if (server.InitDataState == InitDataState.Complete && server.playerManager.Players.Count == 0)
                break; // Success

            if (timeoutWatch.ElapsedMilliseconds > 2000)
                Assert.Fail("Timeout");

            Thread.Sleep(50);
        }
    }

    [Test]
    public async Task JoinPointAbortUnblocksWaiters()
    {
        var server = new MultiplayerServer(new ServerSettings
        {
            gameName = "Test",
            direct = false,
            lan = false
        });

        Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.True);
        Assert.That(server.worldData.CreatingJoinPoint, Is.True);

        var waitTask = server.worldData.WaitJoinPoint();
        Assert.That(waitTask.IsCompleted, Is.False);

        server.worldData.AbortJoinPointCreation();

        var completed = await waitTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(completed, Is.SameAs(server.worldData));
        Assert.That(server.worldData.CreatingJoinPoint, Is.False);
        Assert.That(server.worldData.WaitJoinPoint().IsCompleted, Is.True);
    }

    [Test]
    public void LoadingStateHandlesKeepAliveWhileWaitingForJoinPoint()
    {
        var server = MakeServer(out var port);
        Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.True);

        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            server.worldData.EndJoinPointCreation();
        });

        ConnectClient(port, typeof(TestLoadingKeepAliveState));

        var timeoutWatch = Stopwatch.StartNew();
        while (true)
        {
            if (server.playerManager.Players.Count == 0)
                break;

            if (timeoutWatch.ElapsedMilliseconds > 2000)
                Assert.Fail("Timeout");

            Thread.Sleep(50);
        }
    }

    private void ConnectClient(int port, Type joiningStateType)
    {
        var clientListener = new TestNetListener(joiningStateType);
        var client = new NetManager(clientListener);
        client.Start();
        var peer = client.Connect("127.0.0.1", port, "");

        teardownActions.Add(() => client.Stop());

        Console.WriteLine($"Connected to {peer}");

        new Thread(() =>
        {
            while (client.IsRunning)
            {
                client.PollEvents();
                Thread.Sleep(50);
            }
        }) { IsBackground = true }.Start();
    }

    private MultiplayerServer MakeServer(out int port)
    {
        var server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings
        {
            gameName = "Test",
            direct = true,
            directAddress = "127.0.0.1:0", // 0 makes the OS choose any free port
            lan = false
        })
        {
            running = true
        };

        server.worldData.savedGame = Array.Empty<byte>();

        var badEndpoint = server.settings.TryParseEndpoints(out var endpoints);
        Assert.That(badEndpoint, Is.Null);
        var success = LiteNetManager.Create(server, endpoints, out var liteNet);
        Assert.That(success, Is.True);
        server.netManagers.Add(liteNet);

        port = liteNet.netManagers[0].manager.LocalPort;

        var serverThread = new Thread(server.Run) { IsBackground = true };
        serverThread.Start();

        teardownActions.Add(() =>
        {
            server.running = false;
            serverThread.Join(timeout: TimeSpan.FromSeconds(2));
        });

        return server;
    }
}
