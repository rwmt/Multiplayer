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

        // The join flow sends commands and mutates players, which must happen on the server thread
        Assert.That(server.OffThreadCalls, Is.Zero);
    }

    [Test]
    public async Task JoinPointAbortUnblocksWaiters()
    {
        var server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings
        {
            gameName = "Test",
            direct = false,
            lan = false
        });
        AddHost(server);

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
        var server = MakeServer(out var port, s =>
        {
            AddHost(s);
            Assert.That(s.worldData.TryStartJoinPointCreation(true), Is.True);
        });

        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            // Join points end on the server thread (world upload handler)
            server.Enqueue(() => server.worldData.EndJoinPointCreation());
        });

        ConnectClient(port, typeof(TestLoadingKeepAliveState));

        // The client joins, waits for the join point, receives the world and leaves; only the host remains
        WaitUntil(() => server.playerManager.Players.Count == 2);
        WaitUntil(() => server.playerManager.Players.Count == 1);
    }

    private static void AddHost(MultiplayerServer server)
    {
        var conn = new RecordingConnection("host");
        conn.ChangeState(ConnectionStateEnum.ServerPlaying);
        var host = new ServerPlayer(100, conn) { hasJoined = true };
        conn.serverPlayer = host;
        server.playerManager.Players.Add(host);
        server.hostUsername = "host";
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var timeoutWatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeoutWatch.ElapsedMilliseconds > 2000)
                Assert.Fail("Timeout");

            Thread.Sleep(20);
        }
    }

    [Test]
    public void StandaloneJoinWithExistingPlayer_DoesNotStartJoinPoint()
    {
        var server = MakeServer(out var port, s =>
        {
            s.IsStandaloneServer = true;
            // Everything a hosted server would need to start a join point on join
            s.settings.autoJoinPoint = AutoJoinPointFlags.Join;

            var existingConn = new RecordingConnection("existing");
            existingConn.ChangeState(ConnectionStateEnum.ServerPlaying);
            var existingPlayer = new ServerPlayer(100, existingConn) { hasJoined = true, status = PlayerStatus.Playing };
            existingConn.serverPlayer = existingPlayer;
            s.playerManager.Players.Add(existingPlayer);
        });

        ConnectClient(port, typeof(TestJoiningState));

        // The client leaving would abort a started join point, but its command stays in the log
        bool JoinPointStarted() => OnServerThread(server, () => server.worldData.cmds
            .Any(raw => ScheduledCommand.Deserialize(new ByteReader(raw)).type == CommandType.CreateJoinPoint));

        // The existing player alone makes the count 1: wait for the client to come and go (with a join point, it
        // would wait for it instead)
        WaitUntil(() => server.playerManager.Players.Count == 2);
        WaitUntil(() => server.playerManager.Players.Count == 1 || JoinPointStarted());

        Assert.That(JoinPointStarted(), Is.False);
    }

    private static T OnServerThread<T>(MultiplayerServer server, Func<T> func)
    {
        var result = new TaskCompletionSource<T>();
        server.Enqueue(() => result.SetResult(func()));
        Assert.That(result.Task.Wait(2000), Is.True, "The server thread didn't run the action");
        return result.Task.Result;
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

    /// beforeStart sets up server state before the server thread runs, which owns it from then on
    private MultiplayerServer MakeServer(out int port, Action<MultiplayerServer>? beforeStart = null)
    {
        var server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings
        {
            gameName = "Test",
            direct = true,
            directAddress = "127.0.0.1:0", // 0 makes the OS choose any free port
            lan = false,
            autoJoinPoint = 0 // Disable: test server has no game simulation to complete join points
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

        beforeStart?.Invoke(server);

        var serverThread = new Thread(server.Run) { IsBackground = true };
        serverThread.Start();

        teardownActions.Add(server.TryStop);

        return server;
    }
}
