using System.Collections.Generic;
using System.Linq;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

/// The world data sent to joiners is one fragmented packet: the stored sync opinions must never push it over the limit
[TestFixture]
public class WorldDataSizeTest
{
    private const int MiB = 1024 * 1024;

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
        server.worldData.savedGame = [1];
        server.worldData.sessionData = [2];
        nextPlayerId = 0;
    }

    [TearDown]
    public void TearDown()
    {
        MultiplayerServer.instance = null;
    }

    private (ServerPlayer player, RecordingConnection conn) AddPlayer(string username)
    {
        var conn = new RecordingConnection(username);
        var player = new ServerPlayer(nextPlayerId++, conn) { hasJoined = true };
        conn.serverPlayer = player;
        conn.ChangeState(ConnectionStateEnum.ServerPlaying);
        server.playerManager.Players.Add(player);
        return (player, conn);
    }

    private static byte[] Opinion(byte marker, int length)
    {
        var opinion = new byte[length];
        opinion[0] = marker;
        return opinion;
    }

    // Reassembles the fragments the way ConnectionBase.HandleReceiveFragment does
    private static byte[] ReceivedFragmented(RecordingConnection conn, Packets id)
    {
        var data = new List<byte>();
        foreach (var raw in conn.SentRaw.Where(raw => (Packets)(raw[0] & 0x3F) == id && (raw[0] & 0xC0) != 0))
            data.AddRange(data.Count == 0 ? raw[8..] : raw[2..]);
        return data.ToArray();
    }

    private static List<byte> SentOpinionMarkers(byte[] worldData)
    {
        var reader = new ByteReader(worldData);
        reader.ReadInt32(); // faction
        reader.ReadInt32(); // timer
        reader.ReadInt32(); // sent cmds
        reader.ReadBool(); // frozen
        reader.ReadPrefixedBytes(); // game
        reader.ReadPrefixedBytes(); // session
        var cmds = reader.ReadInt32();
        for (int i = 0; i < cmds; i++) reader.ReadPrefixedBytes();
        var maps = reader.ReadInt32();
        for (int i = 0; i < maps; i++)
        {
            reader.ReadInt32();
            reader.ReadPrefixedBytes();
        }

        var opinions = reader.ReadInt32();
        var markers = Enumerable.Range(0, opinions).Select(_ => reader.ReadPrefixedBytes()![0]).ToList();
        Assert.That(reader.Left, Is.Zero);
        return markers;
    }

    [Test]
    public void StoredOpinionsAreBoundedDroppingTheOldest()
    {
        for (byte i = 0; i < WorldData.MaxSyncInfos + 2; i++)
            server.worldData.AddSyncInfo([i]);

        Assert.That(server.worldData.syncInfos.Select(o => o[0]),
            Is.EqualTo(Enumerable.Range(2, WorldData.MaxSyncInfos).Select(i => (byte)i)));

        // Opinions with desync traces of a big save
        for (byte i = 20; i < 24; i++)
            server.worldData.AddSyncInfo(Opinion(i, 3 * MiB));

        Assert.That(server.worldData.syncInfos.Select(o => o[0]), Is.EqualTo(new byte[] { 22, 23 }));
        Assert.That(server.worldData.syncInfos.Sum(o => o.Length), Is.LessThanOrEqualTo(WorldData.MaxSyncInfosLength));
    }

    [Test]
    public void TheNewestOpinionIsAlwaysKept()
    {
        server.worldData.AddSyncInfo(Opinion(1, 2 * MiB));
        server.worldData.AddSyncInfo(Opinion(2, WorldData.MaxSyncInfosLength));

        Assert.That(server.worldData.syncInfos.Select(o => o[0]), Is.EqualTo(new byte[] { 2 }));
    }

    [Test]
    public void WorldDataSendsOnlyTheNewestOpinionsThatFit()
    {
        for (byte i = 0; i < 8; i++)
            server.worldData.AddSyncInfo(Opinion(i, MiB));
        Assert.That(server.worldData.syncInfos, Has.Count.EqualTo(8));

        // A big save leaves room for two and a half of them
        server.worldData.savedGame = new byte[ConnectionBase.MaxFragmentedMessageLength - MiB * 5 / 2];

        var (_, conn) = AddPlayer("joiner");
        Assert.That(new ServerLoadingState(conn).SendWorldData(), Is.True);

        var worldData = ReceivedFragmented(conn, Packets.Server_WorldData);
        Assert.That(worldData.Length, Is.LessThanOrEqualTo(ConnectionBase.MaxFragmentedMessageLength));
        Assert.That(SentOpinionMarkers(worldData), Is.EqualTo(new byte[] { 6, 7 }));
        Assert.That(conn.SentPackets.First(), Is.EqualTo(Packets.Server_WorldDataStart));
        // The stored ones stay for joiners with smaller world data
        Assert.That(server.worldData.syncInfos, Has.Count.EqualTo(8));
    }

    [Test]
    public void SmallWorldDataSendsAllOpinions()
    {
        for (byte i = 0; i < 3; i++)
            server.worldData.AddSyncInfo([i]);

        var (_, conn) = AddPlayer("joiner");
        Assert.That(new ServerLoadingState(conn).SendWorldData(), Is.True);

        var worldData = conn.SentMessages.Single(m => m.id == Packets.Server_WorldData).data;
        Assert.That(SentOpinionMarkers(worldData), Is.EqualTo(new byte[] { 0, 1, 2 }));
    }

    [Test]
    public void OversizeWorldDataDisconnectsWithAReason()
    {
        // The command log since the last join point comes on top of a world that fit into the upload
        server.worldData.savedGame = new byte[ConnectionBase.MaxFragmentedMessageLength - 1000];
        var (_, liveConn) = AddPlayer("live");
        for (int i = 0; i < 10; i++)
            server.commands.Send(CommandType.Sync, 0, 1, new byte[200]);

        var (joiner, conn) = AddPlayer("joiner");
        var errors = new List<string>();
        var prevError = ServerLog.error;
        ServerLog.error = errors.Add;
        try
        {
            Assert.That(new ServerLoadingState(conn).SendWorldData(), Is.False);
        }
        finally
        {
            ServerLog.error = prevError;
        }

        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("joiner").And.Contain("too big"));

        Assert.That(conn.SentPackets, Does.Not.Contain(Packets.Server_WorldData));
        Assert.That(conn.SentPackets, Does.Not.Contain(Packets.Server_WorldDataStart));
        Assert.That(conn.State, Is.EqualTo(ConnectionStateEnum.Disconnected));
        Assert.That(conn.Goodbye?.reason, Is.EqualTo(MpDisconnectReason.GenericKeyed));
        Assert.That(new ByteReader(conn.Goodbye!.Value.data).ReadString(),
            Does.Contain("too big").And.Contain("10 commands"));
        Assert.That(server.playerManager.Players, Does.Not.Contain(joiner));
        Assert.That(liveConn.SentPackets, Does.Contain(Packets.Server_Chat));
        Assert.That(liveConn.State, Is.EqualTo(ConnectionStateEnum.ServerPlaying));
    }
}
