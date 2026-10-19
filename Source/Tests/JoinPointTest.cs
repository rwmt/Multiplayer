using System.Collections.Generic;
using System.IO;
using System.Linq;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

[TestFixture]
public class JoinPointTest
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
            autosaveInterval = 0
        });
        server.worldData.savedGame = [1];
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

    private static void Upload(ServerPlayer player, int tick, byte[] game, int mapId = 0, byte[]? map = null)
    {
        var writer = new ByteWriter();
        writer.WriteInt32(tick);
        writer.WriteInt32(1);
        writer.WriteInt32(mapId);
        writer.WritePrefixedBytes(map ?? [7]);
        writer.WritePrefixedBytes(game);
        writer.WritePrefixedBytes([5]);
        player.conn.GetState<ServerPlayingState>()!.HandleWorldDataUpload(new ByteReader(writer.ToArray()));
    }

    private void SendCmd(byte marker) => server.commands.Send(CommandType.Sync, 0, 1, [marker]);

    private List<ScheduledCommand> Cmds => server.worldData.cmds
        .Select(raw => ScheduledCommand.Deserialize(new ByteReader(raw))).ToList();

    [Test]
    public void StandaloneUploaderIsLongestConnectedPlayerInSync()
    {
        server.IsStandaloneServer = true;
        AddPlayer("desynced", PlayerStatus.Desynced);
        AddPlayer("simulating", PlayerStatus.Simulating);
        var reference = AddPlayer("reference");
        AddPlayer("other");

        Assert.That(server.playerManager.SyncReference, Is.SameAs(reference));
        Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.True);
        Assert.That(server.worldData.JoinPointUploaderId, Is.EqualTo(reference.id));

        // The uploader is announced in the command itself, every client reloads
        var cjp = Cmds.Single(c => c.type == CommandType.CreateJoinPoint);
        Assert.That(new ByteReader(cjp.data).ReadInt32(), Is.EqualTo(reference.id));
    }

    [Test]
    public void HostedUploaderIsArbiterWhenPlaying()
    {
        var host = AddPlayer("host");
        server.hostUsername = "host";
        var arbiter = AddPlayer("arbiter", type: PlayerType.Arbiter);

        Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.True);
        Assert.That(server.worldData.JoinPointUploaderId, Is.EqualTo(arbiter.id));

        Upload(host, 0, [2]);
        Assert.That(server.worldData.CreatingJoinPoint, Is.True);

        Upload(arbiter, 0, [3]);
        Assert.That(server.worldData.CreatingJoinPoint, Is.False);
        Assert.That(server.worldData.savedGame, Is.EqualTo(new byte[] { 3 }));
    }

    [Test]
    public void NoJoinPointWithoutAnUploader()
    {
        server.IsStandaloneServer = true;
        AddPlayer("simulating", PlayerStatus.Simulating);

        Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.False);
        Assert.That(server.worldData.CreatingJoinPoint, Is.False);
        Assert.That(server.worldData.cmds, Is.Empty);
    }

    [Test]
    public void OnlyTheExpectedUploadAtTheJoinPointTickCompletesIt()
    {
        server.IsStandaloneServer = true;
        var uploader = AddPlayer("a");
        var other = AddPlayer("b");
        server.gameTimer = 100;

        SendCmd(1);
        Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.True);
        server.gameTimer = 105;
        SendCmd(2);

        Upload(other, 100, [2]);
        Upload(uploader, 99, [2]);
        Assert.That(server.worldData.CreatingJoinPoint, Is.True);
        Assert.That(server.worldData.savedGame, Is.EqualTo(new byte[] { 1 }));

        Upload(uploader, 100, [2], mapId: 3, map: [4]);

        // World, maps and log are swapped together
        Assert.That(server.worldData.CreatingJoinPoint, Is.False);
        Assert.That(server.worldData.savedGame, Is.EqualTo(new byte[] { 2 }));
        Assert.That(server.worldData.mapData.Keys, Is.EqualTo(new[] { 3 }));
        Assert.That(Cmds.Select(c => c.data[0]), Is.EqualTo(new byte[] { 2 }));
        Assert.That(Cmds.All(c => c.ticks >= 100), Is.True);
        Assert.That(server.worldData.standaloneWorldSnapshot.tick, Is.EqualTo(100));
        Assert.That(server.worldData.standaloneWorldSnapshot.producerPlayerId, Is.EqualTo(uploader.id));
    }

    [Test]
    public void UploadOutsideOfJoinPointIsRejected([Values] bool standalone)
    {
        server.IsStandaloneServer = standalone;
        var host = AddPlayer("host");
        server.hostUsername = standalone ? null : "host";

        Upload(host, 0, [2]);

        Assert.That(server.worldData.savedGame, Is.EqualTo(new byte[] { 1 }));
        Assert.That(server.worldData.mapData, Is.Empty);
    }

    [Test]
    public void InitialHostUploadIsAccepted()
    {
        server.worldData.savedGame = null;
        var host = AddPlayer("host");
        server.hostUsername = "host";

        Upload(host, 0, [2]);

        Assert.That(server.worldData.savedGame, Is.EqualTo(new byte[] { 2 }));
    }

    [Test]
    public void JoinPointAbortsWhenUploaderDisconnects()
    {
        server.IsStandaloneServer = true;
        var uploader = AddPlayer("a");
        AddPlayer("b");

        Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.True);
        var waiter = server.worldData.WaitJoinPoint();

        uploader.Disconnect(MpDisconnectReason.Generic);

        Assert.That(server.worldData.CreatingJoinPoint, Is.False);
        Assert.That(waiter.IsCompleted, Is.True);
        // Joiners get the previous world with the full log, including the aborted CreateJoinPoint
        Assert.That(Cmds.Any(c => c.type == CommandType.CreateJoinPoint), Is.True);
        Assert.That(server.worldData.savedGame, Is.EqualTo(new byte[] { 1 }));
    }

    [Test]
    public void JoinPointAbortsWhenUploaderDesyncs()
    {
        server.IsStandaloneServer = true;
        var uploader = AddPlayer("a");
        AddPlayer("b");

        Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.True);
        server.playerManager.OnDesync(uploader, 0, 0);

        Assert.That(server.worldData.CreatingJoinPoint, Is.False);
        Assert.That(uploader.status, Is.EqualTo(PlayerStatus.Desynced));
    }

    [Test]
    public void JoinPointAbortsWhenUploaderRejoins()
    {
        server.IsStandaloneServer = true;
        var uploader = AddPlayer("a");
        AddPlayer("b");

        Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.True);
        uploader.conn.GetState<ServerPlayingState>()!.HandleRejoin(new ByteReader([]));

        Assert.That(server.worldData.CreatingJoinPoint, Is.False);
        Assert.That(uploader.status, Is.EqualTo(PlayerStatus.Simulating));
    }

    [Test]
    public void JoinPointTimesOutWithoutUpload()
    {
        var host = AddPlayer("host");
        server.hostUsername = "host";

        Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.True);

        // The uploader keeps answering keep alives (it isn't busy saving) but never uploads
        host.keepAliveAt = WorldData.JoinPointTimeout;
        server.worldData.CheckJoinPointTimeout(WorldData.JoinPointTimeout);
        Assert.That(server.worldData.CreatingJoinPoint, Is.True);

        host.keepAliveAt = WorldData.JoinPointTimeout + 1;
        server.worldData.CheckJoinPointTimeout(WorldData.JoinPointTimeout + 1);
        Assert.That(server.worldData.CreatingJoinPoint, Is.False);
    }

    [Test]
    public void JoinPointWaitsWhileTheUploaderIsBusySaving()
    {
        server.IsStandaloneServer = true;
        var uploader = AddPlayer("a");
        AddPlayer("b");

        Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.True);

        // Saving and reloading a big game blocks the uploader's main thread: no keep alives, no fragments
        var reloaded = WorldData.JoinPointTimeout * 5;
        server.worldData.CheckJoinPointTimeout(reloaded);
        Assert.That(server.worldData.CreatingJoinPoint, Is.True);

        // Once it answers keep alives again, the upload has to start within the timeout
        uploader.keepAliveAt = reloaded + WorldData.JoinPointTimeout;
        server.worldData.CheckJoinPointTimeout(reloaded + WorldData.JoinPointTimeout);
        Assert.That(server.worldData.CreatingJoinPoint, Is.True);

        uploader.keepAliveAt = reloaded + WorldData.JoinPointTimeout + 1;
        server.worldData.CheckJoinPointTimeout(reloaded + WorldData.JoinPointTimeout + 1);
        Assert.That(server.worldData.CreatingJoinPoint, Is.False);
    }

    [Test]
    public void ForcedJoinPointsAreRateLimited()
    {
        AddPlayer("host");
        server.hostUsername = "host";

        Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.True);
        server.worldData.EndJoinPointCreation();

        Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.False);
    }

    [Test]
    public void RepeatedDesyncReportsAreIgnored()
    {
        server.settings.autoJoinPoint = AutoJoinPointFlags.Desync;
        var host = AddPlayer("host");
        server.hostUsername = "host";
        var player = AddPlayer("player");

        server.playerManager.OnDesync(player, 0, 0);
        var cmdCount = server.worldData.cmds.Count;
        server.playerManager.OnDesync(player, 0, 0);

        Assert.That(server.worldData.cmds, Has.Count.EqualTo(cmdCount));
        Assert.That(((RecordingConnection)host.conn).SentPackets.Count(p => p == Packets.Server_Traces), Is.EqualTo(1));
    }

    [Test]
    public void StandaloneDesyncRequestsTracesFromSyncReference()
    {
        server.IsStandaloneServer = true;
        var reference = AddPlayer("reference");
        var player = AddPlayer("player");

        Assert.DoesNotThrow(() => server.playerManager.OnDesync(player, 0, 0));
        Assert.That(((RecordingConnection)reference.conn).SentPackets, Does.Contain(Packets.Server_Traces));
    }

    [Test]
    public void StandaloneAutosaveIsIssuedByServer()
    {
        server.IsStandaloneServer = true;
        server.settings.autosaveUnit = AutosaveUnit.Minutes;
        server.settings.autosaveInterval = 1;
        var uploader = AddPlayer("a");

        server.worldData.Tick();
        server.gameTimer = 60 * 60 - 1;
        server.worldData.Tick();
        Assert.That(server.worldData.CreatingJoinPoint, Is.False);

        server.gameTimer = 60 * 60;
        server.worldData.Tick();
        Assert.That(server.worldData.CreatingJoinPoint, Is.True);
        Assert.That(server.worldData.JoinPointUploaderId, Is.EqualTo(uploader.id));
        Assert.That(server.worldData.JoinPointTick, Is.EqualTo(60 * 60));
    }

    [Test]
    public void WorldTravelRequestsDoNotCreateJoinPoints()
    {
        server.IsStandaloneServer = true;
        var player = AddPlayer("a");

        player.conn.GetState<ServerPlayingState>()!.HandleAutosaving(new ClientAutosavingPacket(JoinPointRequestReason.WorldTravel, 0));
        Assert.That(server.worldData.CreatingJoinPoint, Is.False);

        player.conn.GetState<ServerPlayingState>()!.HandleAutosaving(new ClientAutosavingPacket(JoinPointRequestReason.Save, 0));
        Assert.That(server.worldData.CreatingJoinPoint, Is.True);
    }

    private void AdvanceNetTimer(int ticks) =>
        typeof(MultiplayerServer).GetProperty(nameof(MultiplayerServer.NetTimer))!
            .SetValue(server, server.NetTimer + ticks);

    private static void RequestSave(ServerPlayer player, int tick) =>
        player.conn.GetState<ServerPlayingState>()!.HandleAutosaving(
            new ClientAutosavingPacket(JoinPointRequestReason.Save, tick));

    [Test]
    public void DaysAutosaveRequestsForOneTickCreateOneJoinPoint()
    {
        server.IsStandaloneServer = true;
        var uploader = AddPlayer("a");
        var other = AddPlayer("b");
        var slow = AddPlayer("slow");

        // Every client autosaves at tick 90 and asks once its save is written
        server.gameTimer = 100;
        RequestSave(uploader, 90);
        Assert.That(server.worldData.JoinPointTick, Is.EqualTo(100));
        RequestSave(other, 90);
        Upload(uploader, 100, [2]);
        Assert.That(server.worldData.CompletedJoinPointTick, Is.EqualTo(100));

        // Saving took the slow client longer than the forced join point cooldown
        AdvanceNetTimer(WorldData.ForcedJoinPointCooldown + 1);
        server.gameTimer = 400;
        var cmdCount = server.worldData.cmds.Count;
        RequestSave(slow, 90);

        Assert.That(server.worldData.CreatingJoinPoint, Is.False);
        Assert.That(server.worldData.cmds, Has.Count.EqualTo(cmdCount));

        // A save of a newer state still gets one
        RequestSave(other, 300);
        Assert.That(server.worldData.CreatingJoinPoint, Is.True);
        Assert.That(server.worldData.JoinPointTick, Is.EqualTo(400));
    }

    [Test]
    public void SaveRequestAfterAnAbortedJoinPointCreatesOne()
    {
        server.IsStandaloneServer = true;
        var uploader = AddPlayer("a");
        var slow = AddPlayer("slow");

        server.gameTimer = 100;
        RequestSave(uploader, 90);
        server.worldData.AbortJoinPointCreation();

        // The server's world is still older than the save
        AdvanceNetTimer(WorldData.ForcedJoinPointCooldown + 1);
        RequestSave(slow, 90);
        Assert.That(server.worldData.CreatingJoinPoint, Is.True);
    }

    [Test]
    public void StandaloneSnapshotsDoNotReplaceTheWorld()
    {
        server.IsStandaloneServer = true;
        var player = AddPlayer("a");
        var state = player.conn.GetState<ServerPlayingState>()!;

        state.HandleStandaloneWorldSnapshot(new ClientStandaloneWorldSnapshotPacket
        {
            tick = 10, worldData = [2], sessionData = [3], sha256Hash = []
        });
        state.HandleStandaloneMapSnapshot(new ClientStandaloneMapSnapshotPacket
        {
            mapId = 0, tick = 10, mapData = [4], sha256Hash = []
        });

        Assert.That(server.worldData.savedGame, Is.EqualTo(new byte[] { 1 }));
        Assert.That(server.worldData.mapData, Is.Empty);
    }

    [Test]
    public void StandaloneJoinPointIsPersistedWithItsLog()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mp-joinpoint-{Guid.NewGuid():N}");
        try
        {
            server.IsStandaloneServer = true;
            server.persistence = new StandalonePersistence(dir);
            var uploader = AddPlayer("a");
            server.gameTimer = 50;

            SendCmd(1);
            Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.True);
            server.gameTimer = 60;
            SendCmd(2);
            Upload(uploader, 50, [2]);

            var reloaded = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings())
            {
                IsStandaloneServer = true,
                persistence = new StandalonePersistence(dir),
            };
            reloaded.persistence.LoadInto(reloaded);

            Assert.That(reloaded.worldData.savedGame, Is.EqualTo(new byte[] { 2 }));
            Assert.That(reloaded.worldData.cmds.Select(c => ScheduledCommand.Deserialize(new ByteReader(c)).data[0]),
                Is.EqualTo(new byte[] { 2 }));
            // Never behind the last logged command
            Assert.That(reloaded.gameTimer, Is.EqualTo(60));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
