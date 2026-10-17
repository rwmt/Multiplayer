using System.Linq;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

/// Desync detection compares every client against one reference client's sync opinions
[TestFixture]
public class SyncReferenceTest
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
            lan = false,
            pauseOnDesync = false,
            autoJoinPoint = 0
        });
        nextPlayerId = 0;
    }

    [TearDown]
    public void TearDown()
    {
        MultiplayerServer.instance = null;
    }

    private ServerPlayer AddPlayer(string username, PlayerStatus status = PlayerStatus.Playing,
        PlayerType type = PlayerType.Normal)
    {
        var conn = new RecordingConnection(username);
        var player = new ServerPlayer(nextPlayerId++, conn) { hasJoined = true, status = status, type = type };
        conn.serverPlayer = player;
        conn.ChangeState(ConnectionStateEnum.ServerPlaying);
        server.playerManager.Players.Add(player);
        return player;
    }

    private static RecordingConnection Conn(ServerPlayer p) => (RecordingConnection)p.conn;

    private static void SendOpinion(ServerPlayer from) =>
        from.conn.GetState<ServerPlayingState>()!.HandleDesyncCheck(new ClientSyncInfoPacket { rawSyncOpinion = [1, 2, 3] });

    private static bool? LastReferenceFlag(ServerPlayer p)
    {
        var msg = Conn(p).SentMessages.LastOrDefault(m => m.id == Packets.Server_SyncReference);
        return msg.data == null ? null : msg.data[0] == 1;
    }

    [Test]
    public void StandaloneForwardsOnlyReferenceOpinions()
    {
        server.IsStandaloneServer = true;
        var reference = AddPlayer("reference");
        var other = AddPlayer("other");
        var joining = AddPlayer("joining", PlayerStatus.Simulating);

        SendOpinion(other);
        Assert.That(server.worldData.syncInfos, Is.Empty);
        Assert.That(Conn(reference).SentPackets, Does.Not.Contain(Packets.Server_SyncInfo));

        SendOpinion(reference);
        Assert.That(server.worldData.syncInfos, Has.Count.EqualTo(1));
        Assert.That(Conn(other).SentPackets, Does.Contain(Packets.Server_SyncInfo));
        Assert.That(Conn(joining).SentPackets, Does.Contain(Packets.Server_SyncInfo));
        Assert.That(Conn(reference).SentPackets, Does.Not.Contain(Packets.Server_SyncInfo));
    }

    [Test]
    public void StandaloneReferenceChangeIsAnnounced()
    {
        server.IsStandaloneServer = true;
        var first = AddPlayer("first");
        var second = AddPlayer("second");

        server.playerManager.UpdateSyncReference();
        Assert.That(LastReferenceFlag(first), Is.True);
        Assert.That(LastReferenceFlag(second), Is.Null);

        SendOpinion(first);
        Assert.That(server.worldData.syncInfos, Has.Count.EqualTo(1));

        server.playerManager.OnDesync(first, 0, 0);

        Assert.That(server.playerManager.SyncReference, Is.SameAs(second));
        Assert.That(LastReferenceFlag(first), Is.False);
        Assert.That(LastReferenceFlag(second), Is.True);
        // Opinions of the previous reference aren't used for joiners
        Assert.That(server.worldData.syncInfos, Is.Empty);
    }

    [Test]
    public void StandaloneReferenceMovesWhenItRejoinsAndReturnsAfterWorldReady()
    {
        server.IsStandaloneServer = true;
        var first = AddPlayer("first");
        var second = AddPlayer("second");
        server.playerManager.UpdateSyncReference();

        var state = first.conn.GetState<ServerPlayingState>()!;
        state.HandleRejoin(new ByteReader([]));
        Assert.That(server.playerManager.SyncReference, Is.SameAs(second));
        Assert.That(LastReferenceFlag(first), Is.False);

        // Back in the playing state after loading, reference again once caught up
        first.conn.ChangeState(ConnectionStateEnum.ServerPlaying);
        first.conn.GetState<ServerPlayingState>()!.HandleWorldReady(new ByteReader([]));
        Assert.That(server.playerManager.SyncReference, Is.SameAs(first));
        Assert.That(LastReferenceFlag(first), Is.True);
        Assert.That(LastReferenceFlag(second), Is.False);
    }

    [Test]
    public void HostedForwardsHostOpinionsToOthers()
    {
        var host = AddPlayer("host");
        server.hostUsername = "host";
        var other = AddPlayer("other");

        SendOpinion(other);
        Assert.That(server.worldData.syncInfos, Is.Empty);

        SendOpinion(host);
        Assert.That(Conn(other).SentPackets, Does.Contain(Packets.Server_SyncInfo));
        Assert.That(Conn(host).SentPackets, Does.Not.Contain(Packets.Server_SyncInfo));

        // Hosted clients know their role, no announcements
        server.playerManager.UpdateSyncReference();
        Assert.That(Conn(host).SentPackets, Does.Not.Contain(Packets.Server_SyncReference));
    }

    [Test]
    public void HostedForwardsArbiterOpinionsToEveryoneButArbiter()
    {
        var host = AddPlayer("host");
        server.hostUsername = "host";
        var arbiter = AddPlayer("arbiter", type: PlayerType.Arbiter);

        SendOpinion(host);
        Assert.That(server.worldData.syncInfos, Is.Empty);

        SendOpinion(arbiter);
        Assert.That(Conn(host).SentPackets, Does.Contain(Packets.Server_SyncInfo));
        Assert.That(Conn(arbiter).SentPackets, Does.Not.Contain(Packets.Server_SyncInfo));
    }
}
