using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

[NonParallelizable]
public class FragmentedPacketTest
{
    private MultiplayerServer server = null!;
    private Action<string>? prevErrorLog;
    private readonly List<string> errors = new();

    private class CapturingConnection : ConnectionBase
    {
        public readonly List<byte[]> sent = new();
        protected override void SendRaw(byte[] raw, bool reliable) => sent.Add(raw);
        protected override void OnClose(ServerDisconnectPacket? goodbye) { }
    }

    [SetUp]
    public void SetUp()
    {
        server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings
        {
            gameName = "Test",
            direct = false,
            lan = false
        });
        server.hostUsername = "host";

        prevErrorLog = ServerLog.error;
        errors.Clear();
        ServerLog.error = errors.Add;
    }

    [TearDown]
    public void TearDown()
    {
        ServerLog.error = prevErrorLog;
        MultiplayerServer.instance = null;
    }

    private RecordingConnection AddHost()
    {
        var conn = new RecordingConnection("host");
        var player = new ServerPlayer(0, conn);
        conn.serverPlayer = player;
        conn.ChangeState(ConnectionStateEnum.ServerPlaying);
        server.playerManager.Players.Add(player);
        return conn;
    }

    private static List<byte[]> Fragments(byte[] syncOpinion)
    {
        var sender = new CapturingConnection();
        sender.SendFragmented(new ClientSyncInfoPacket { rawSyncOpinion = syncOpinion }.Serialize());
        return sender.sent;
    }

    private static byte[] Payload(byte fill) => Enumerable.Repeat(fill, 5000).ToArray();

    private static void Receive(ConnectionBase conn, IEnumerable<byte[]> raws)
    {
        foreach (var raw in raws)
            conn.HandleReceiveRaw(new ByteReader(raw), true);
    }

    [Test]
    public void StateChangeDropsPartialPacket()
    {
        var conn = AddHost();
        var dropped = Fragments(Payload(1));
        Assert.That(dropped, Has.Count.GreaterThan(3));

        Receive(conn, dropped.Take(2));
        conn.ChangeState(ConnectionStateEnum.ServerLoading); // Goes straight back to playing
        Assert.That(conn.State, Is.EqualTo(ConnectionStateEnum.ServerPlaying));

        // The rest of the dropped packet is ignored, the next fragmented packet goes through
        Receive(conn, dropped.Skip(2));
        Receive(conn, Fragments(Payload(2)));

        Assert.That(server.worldData.syncInfos, Has.Count.EqualTo(1));
        Assert.That(server.worldData.syncInfos[0], Is.EqualTo(Payload(2)));
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void FragmentWithoutHandlerDropsWholePacket()
    {
        var conn = AddHost();
        conn.ChangeState(ConnectionStateEnum.ServerJoining); // No handler for sync info
        conn.Lenient = true;

        var dropped = Fragments(Payload(1));
        Receive(conn, dropped.Take(1));
        Assert.That(errors, Has.Count.EqualTo(1)); // "No handler" is logged once

        conn.Lenient = false;
        conn.ChangeState(ConnectionStateEnum.ServerPlaying);

        // The remaining fragments must not be mistaken for the start of a new packet
        Receive(conn, dropped.Skip(1));
        Receive(conn, Fragments(Payload(2)));

        Assert.That(server.worldData.syncInfos, Has.Count.EqualTo(1));
        Assert.That(server.worldData.syncInfos[0], Is.EqualTo(Payload(2)));
        Assert.That(errors, Has.Count.EqualTo(1));
    }
}
