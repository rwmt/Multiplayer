using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

[NonParallelizable]
public class JoiningStateTest
{
    private MultiplayerServer server = null!;
    private Action<string>? prevErrorLog;
    private readonly List<string> errors = new();

    [SetUp]
    public void SetUp()
    {
        server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings
        {
            gameName = "Test",
            direct = false,
            lan = false,
            autoJoinPoint = 0
        });
        server.worldData.savedGame = [];

        prevErrorLog = ServerLog.error;
        errors.Clear();
        ServerLog.error = errors.Add;
    }

    [TearDown]
    public void TearDown()
    {
        ServerLog.error = prevErrorLog;
        ServerJoiningState.ReplyTimeout = TimeSpan.FromMinutes(2);
        MultiplayerServer.instance = null;
    }

    private (ServerPlayer player, RecordingConnection conn) Connect()
    {
        // Username gets set by the joining state
        var conn = new RecordingConnection(null!);
        conn.ChangeState(ConnectionStateEnum.ServerJoining);
        var player = server.playerManager.OnConnected(conn);
        return (player, conn);
    }

    private static void Receive<T>(ConnectionBase conn, T packet) where T : struct, IPacket
    {
        var serialized = packet.Serialize();
        var raw = new byte[serialized.data.Length + 1];
        raw[0] = (byte)serialized.id;
        serialized.data.CopyTo(raw, 1);
        conn.HandleReceiveRaw(new ByteReader(raw), true);
    }

    private static void SendHandshake(ConnectionBase conn, string username)
    {
        Receive(conn, ClientProtocolPacket.Current());
        Receive(conn, new ClientUsernamePacket(username));
    }

    private static ClientInitDataPacket InitData(params KeyedDefInfo[] defInfos) => new()
    {
        rwVersion = "1.0.0",
        debugOnlySyncCmds = [],
        hostOnlySyncCmds = [],
        modCtorRoundMode = RoundModeEnum.ToNearest,
        staticCtorRoundMode = RoundModeEnum.ToNearest,
        defInfos = defInfos,
        Mods = []
    };

    [Test]
    public void InitDataRequesterDisconnecting_NextJoinerIsAsked()
    {
        var (_, first) = Connect();
        SendHandshake(first, "first");

        Assert.That(first.SentPackets, Does.Contain(Packets.Server_InitDataRequest));
        Assert.That(server.InitDataState, Is.EqualTo(InitDataState.Requested));

        server.playerManager.SetDisconnected(first, MpDisconnectReason.ClientLeft);

        // The dying connection must not request the init data again
        Assert.That(server.InitDataState, Is.EqualTo(InitDataState.Waiting));
        Assert.That(first.SentPackets.Count(p => p == Packets.Server_InitDataRequest), Is.EqualTo(1));

        var (_, second) = Connect();
        SendHandshake(second, "second");
        Assert.That(second.SentPackets, Does.Contain(Packets.Server_InitDataRequest));

        Receive(second, InitData());
        Assert.That(server.InitDataState, Is.EqualTo(InitDataState.Complete));
        Assert.That(second.SentPackets, Does.Contain(Packets.Server_UsernameOk));
    }

    [Test]
    public void WaitingJoinerTakesOverInitDataRequest()
    {
        var (_, first) = Connect();
        SendHandshake(first, "first");
        var (_, second) = Connect();
        SendHandshake(second, "second");

        Assert.That(second.SentPackets, Does.Not.Contain(Packets.Server_InitDataRequest));

        server.playerManager.SetDisconnected(first, MpDisconnectReason.ClientLeft);

        Assert.That(second.SentPackets, Does.Contain(Packets.Server_InitDataRequest));
        Assert.That(server.InitDataState, Is.EqualTo(InitDataState.Requested));
    }

    [Test]
    public void InvalidInitData_DisconnectsAndResetsInitDataState()
    {
        var (_, first) = Connect();
        SendHandshake(first, "first");

        // Duplicate def type names make ServerInitData.FromNet throw
        Receive(first, InitData(
            new KeyedDefInfo { name = "ThingDef", count = 1, hash = 1 },
            new KeyedDefInfo { name = "ThingDef", count = 2, hash = 2 }));

        Assert.That(first.State, Is.EqualTo(ConnectionStateEnum.Disconnected));
        Assert.That(server.InitDataState, Is.EqualTo(InitDataState.Waiting));
        Assert.That(errors, Is.Not.Empty);
    }

    [Test]
    public void InitDataRequestTimesOut()
    {
        var (_, first) = Connect();
        ServerJoiningState.ReplyTimeout = TimeSpan.Zero;
        SendHandshake(first, "first");
        Assert.That(server.InitDataState, Is.EqualTo(InitDataState.Requested));

        Thread.Sleep(10);
        server.playerManager.CheckTimeouts();

        Assert.That(first.State, Is.EqualTo(ConnectionStateEnum.Disconnected));
        Assert.That(server.playerManager.Players, Is.Empty);
        Assert.That(server.InitDataState, Is.EqualTo(InitDataState.Waiting));
    }

    [Test]
    public void IdleHandshakeTimesOut()
    {
        ServerJoiningState.ReplyTimeout = TimeSpan.Zero;
        var (_, conn) = Connect();

        Thread.Sleep(10);
        server.playerManager.CheckTimeouts();

        Assert.That(conn.State, Is.EqualTo(ConnectionStateEnum.Disconnected));
        Assert.That(server.playerManager.Players, Is.Empty);
    }
}
