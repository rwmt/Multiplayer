using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using Multiplayer.Common;

namespace Tests;

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
        MpConnectionState.SetImplementation(ConnectionStateEnum.ClientJoining, typeof(TestJoiningState));

        int port = GetFreePort();
        var server = MakeServer(port);
        ConnectClient(port);

        var timeoutWatch = Stopwatch.StartNew();
        while (true)
        {
            if (server.initDataState == InitDataState.Complete && server.playerManager.Players.Count == 0)
                break; // Success

            if (timeoutWatch.ElapsedMilliseconds > 2000)
                Assert.Fail("Timeout");

            Thread.Sleep(50);
        }
    }

    private void ConnectClient(int port)
    {
        var clientListener = new TestNetListener();
        var client = new NetManager(clientListener);
        client.Start();
        var peer = client.Connect($"127.0.0.1", port, "");

        teardownActions.Add(() =>
        {
            client.Stop();
        });

        Console.WriteLine($"Connected to {peer.EndPoint}");

        new Thread(() =>
        {
            while (true)
            {
                client.PollEvents();
                Thread.Sleep(50);
            }
        }) { IsBackground = true }.Start();
    }

    private MultiplayerServer MakeServer(int port)
    {
        var server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings()
        {
            gameName = "Test",
            direct = true,
            directAddress = $"127.0.0.1:{port}",
            lan = false
        })
        {
            running = true,
            initDataState = InitDataState.Waiting
        };

        server.worldData.savedGame = Array.Empty<byte>();

        server.liteNet.StartNet();
        new Thread(server.Run) { IsBackground = true }.Start();

        teardownActions.Add(() => { server.running = false; });

        return server;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
