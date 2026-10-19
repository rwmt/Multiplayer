using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

/// Commands bigger than a single packet (big designations, stockpile pastes) are sent fragmented both ways
[TestFixture]
[NonParallelizable]
public class LargeCommandTest
{
    // Stands in for the client's playing state
    public class CommandReceiverState(ConnectionBase conn) : MpConnectionState(conn)
    {
        public static readonly List<ServerCommandPacket> received = new();

        [TypedPacketHandler]
        public void HandleCommand(ServerCommandPacket packet) => received.Add(packet);
    }

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
        nextPlayerId = 0;
        MpConnectionState.SetImplementation(ConnectionStateEnum.ClientPlaying, typeof(CommandReceiverState));
        CommandReceiverState.received.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        MultiplayerServer.instance = null;
    }

    private RecordingConnection AddPlayer(string username)
    {
        var conn = new RecordingConnection(username);
        var player = new ServerPlayer(nextPlayerId++, conn) { hasJoined = true };
        conn.serverPlayer = player;
        conn.ChangeState(ConnectionStateEnum.ServerPlaying);
        server.playerManager.Players.Add(player);
        return conn;
    }

    private static void Receive(ConnectionBase conn, IEnumerable<byte[]> raws)
    {
        foreach (var raw in raws)
            conn.HandleReceiveRaw(new ByteReader(raw), true);
    }

    private static bool IsFragment(byte[] raw) => (raw[0] & 0xC0) != 0;

    [Test]
    public void BigCommandReachesLiveClientsWhole()
    {
        var senderConn = AddPlayer("sender");
        var liveConn = AddPlayer("live");
        var data = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray();

        var client = new RecordingConnection("client");
        client.SendFragmentedIfNeeded(new ClientCommandPacket(CommandType.Designator, 3, data).Serialize());
        Assert.That(client.SentRaw, Has.Count.GreaterThan(1).And.All.Matches<byte[]>(IsFragment));

        Receive(senderConn, client.SentRaw);

        var logged = ScheduledCommand.Deserialize(new ByteReader(server.worldData.cmds.Single()));
        Assert.That(logged.type, Is.EqualTo(CommandType.Designator));
        Assert.That(logged.mapId, Is.EqualTo(3));
        Assert.That(logged.data, Is.EqualTo(data));

        var liveClient = new RecordingConnection("live client");
        liveClient.ChangeState(ConnectionStateEnum.ClientPlaying);
        Receive(liveClient, liveConn.SentRaw.Where(raw => (Packets)(raw[0] & 0x3F) == Packets.Server_Command));

        var received = CommandReceiverState.received.Single();
        Assert.That(received.ToCommand().playerId, Is.EqualTo(0));
        Assert.That(received.data, Is.EqualTo(data));
    }

    [Test]
    public void SmallCommandIsASinglePacket()
    {
        var liveConn = AddPlayer("live");

        server.commands.Send(CommandType.Sync, 0, 1, new byte[ConnectionBase.MaxSinglePacketSize - 100]);

        var sent = liveConn.SentRaw.Where(raw => (Packets)(raw[0] & 0x3F) == Packets.Server_Command).ToList();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(IsFragment(sent[0]), Is.False);
    }

    [Test]
    public void CommandsKeepTheirOrderAroundABigOne()
    {
        var liveConn = AddPlayer("live");

        server.commands.Send(CommandType.Sync, 0, 1, [1]);
        server.commands.Send(CommandType.Sync, 0, 1, Enumerable.Repeat((byte)2, 200_000).ToArray());
        server.commands.Send(CommandType.Sync, 0, 1, [3]);

        var liveClient = new RecordingConnection("live client");
        liveClient.ChangeState(ConnectionStateEnum.ClientPlaying);
        Receive(liveClient, liveConn.SentRaw.Where(raw => (Packets)(raw[0] & 0x3F) == Packets.Server_Command));

        Assert.That(CommandReceiverState.received.Select(p => p.data[0]), Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(CommandReceiverState.received[1].data, Has.Length.EqualTo(200_000));
    }
}
