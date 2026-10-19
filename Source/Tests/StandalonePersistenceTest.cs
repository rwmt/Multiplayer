using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Multiplayer.Common;

namespace Tests;

[TestFixture]
public class StandalonePersistenceTest
{
    private string tempDir = null!;
    private readonly List<string> errors = new();
    private Action<string>? prevError;

    private string SavedDir => Path.Combine(tempDir, "Saved");

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"mp-standalone-persistence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        errors.Clear();
        prevError = ServerLog.error;
        ServerLog.error = errors.Add;
    }

    [TearDown]
    public void TearDown()
    {
        ServerLog.error = prevError;
        MultiplayerServer.instance = null;

        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    [Test]
    public void WriteJoinPoint_PersistsCommandsAndTickForReload()
    {
        var server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings())
        {
            IsStandaloneServer = true,
            persistence = new StandalonePersistence(tempDir),
        };

        server.worldData.savedGame = [1, 2, 3];
        server.worldData.sessionData = [4, 5, 6];
        server.worldData.mapData[7] = [7, 8, 9];

        // Map command first: the persisted log must keep the send order across tickables
        var mapCmd = ScheduledCommand.Serialize(new ScheduledCommand(CommandType.Designator, 1234, 1, 7, 5, [11]));
        var worldCmd = ScheduledCommand.Serialize(new ScheduledCommand(CommandType.Sync, 1234, 1, ScheduledCommand.Global, 5, [10]));
        server.worldData.cmds = [mapCmd, worldCmd];

        server.persistence.WriteJoinPoint(server.worldData, 1234);

        var reloadedServer = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings())
        {
            IsStandaloneServer = true,
            persistence = new StandalonePersistence(tempDir),
        };

        var info = reloadedServer.persistence.LoadInto(reloadedServer);

        Assert.That(info, Is.Null);
        Assert.That(reloadedServer.gameTimer, Is.EqualTo(1234));
        Assert.That(reloadedServer.startingTimer, Is.EqualTo(1234));
        Assert.That(reloadedServer.worldData.savedGame, Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(reloadedServer.worldData.sessionData, Is.EqualTo(new byte[] { 4, 5, 6 }));
        Assert.That(reloadedServer.worldData.mapData[7], Is.EqualTo(new byte[] { 7, 8, 9 }));
        Assert.That(reloadedServer.worldData.cmds, Is.EqualTo(new[] { mapCmd, worldCmd }));
        Assert.That(reloadedServer.worldData.standaloneWorldSnapshot.tick, Is.EqualTo(1234));
        Assert.That(reloadedServer.worldData.standaloneMapSnapshots[7].tick, Is.EqualTo(1234));
    }

    [Test]
    public void LoadInto_DiscardsLegacyCommandFiles()
    {
        var persistence = new StandalonePersistence(tempDir);
        persistence.EnsureDirectories();

        var savedDir = Path.Combine(tempDir, "Saved");
        File.WriteAllBytes(Path.Combine(savedDir, "world.dat"), [1]);
        File.WriteAllBytes(Path.Combine(savedDir, "maps", "7.dat"), [2]);
        File.WriteAllBytes(Path.Combine(savedDir, "world_cmds.dat"), ScheduledCommand.SerializeCmds([
            Cmd(10, ScheduledCommand.Global, 1),
            Cmd(20, ScheduledCommand.Global, 2),
        ]));
        File.WriteAllBytes(Path.Combine(savedDir, "maps", "7_cmds.dat"), ScheduledCommand.SerializeCmds([
            Cmd(5, 7, 3),
            Cmd(10, 7, 4),
        ]));

        var server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings())
        {
            IsStandaloneServer = true,
            persistence = persistence,
        };

        persistence.LoadInto(server);

        // Only versions before cmds.dat (an older protocol) wrote them
        Assert.That(server.worldData.cmds, Is.Empty);
        Assert.That(server.worldData.savedGame, Is.EqualTo(new byte[] { 1 }));
        Assert.That(server.worldData.mapData[7], Is.EqualTo(new byte[] { 2 }));
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("Discarding the saved command log (4 commands)"));
    }

    [Test]
    public void WriteJoinPoint_ReplacesLegacyCommandFiles()
    {
        var persistence = new StandalonePersistence(tempDir);
        persistence.EnsureDirectories();

        var savedDir = Path.Combine(tempDir, "Saved");
        File.WriteAllBytes(Path.Combine(savedDir, "world_cmds.dat"), ScheduledCommand.SerializeCmds([Cmd(1, ScheduledCommand.Global, 9)]));
        File.WriteAllBytes(Path.Combine(savedDir, "maps", "7_cmds.dat"), ScheduledCommand.SerializeCmds([Cmd(1, 7, 9)]));

        var server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings())
        {
            IsStandaloneServer = true,
            persistence = persistence,
        };
        server.worldData.savedGame = [1];
        server.worldData.mapData[7] = [2];
        server.worldData.cmds = [ScheduledCommand.Serialize(Cmd(50, 7, 1)), ScheduledCommand.Serialize(Cmd(50, ScheduledCommand.Global, 2))];

        persistence.WriteJoinPoint(server.worldData, 50);

        Assert.That(File.Exists(Path.Combine(savedDir, "world_cmds.dat")), Is.False);
        Assert.That(File.Exists(Path.Combine(savedDir, "maps", "7_cmds.dat")), Is.False);

        var reloaded = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings())
        {
            IsStandaloneServer = true,
            persistence = new StandalonePersistence(tempDir),
        };
        reloaded.persistence.LoadInto(reloaded);

        Assert.That(DataBytes(reloaded.worldData.cmds), Is.EqualTo(new byte[] { 1, 2 }));
    }

    private MultiplayerServer Reload()
    {
        var server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings())
        {
            IsStandaloneServer = true,
            persistence = new StandalonePersistence(tempDir),
        };
        server.persistence.LoadInto(server);
        return server;
    }

    private void WriteJoinPoint(byte world, params byte[] cmdMarkers)
    {
        var server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings())
        {
            IsStandaloneServer = true,
            persistence = new StandalonePersistence(tempDir),
        };
        server.worldData.savedGame = [world];
        server.worldData.mapData[7] = [world];
        server.worldData.cmds = cmdMarkers.Select(m => ScheduledCommand.Serialize(Cmd(50, ScheduledCommand.Global, m))).ToList();
        server.persistence.WriteJoinPoint(server.worldData, 60);
    }

    [Test]
    public void LoadInto_KeepsCommandsOfTheCurrentProtocol()
    {
        WriteJoinPoint(1, 5, 6);

        Assert.That(File.ReadAllText(Path.Combine(SavedDir, "cmds_protocol.txt")), Is.EqualTo(MpVersion.Protocol.ToString()));
        Assert.That(DataBytes(Reload().worldData.cmds), Is.EqualTo(new byte[] { 5, 6 }));
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void LoadInto_DiscardsCommandsOfAnotherProtocol([Values(true, false)] bool protocolKnown)
    {
        WriteJoinPoint(1, 5, 6);

        var protocolPath = Path.Combine(SavedDir, "cmds_protocol.txt");
        if (protocolKnown)
            File.WriteAllText(protocolPath, "56");
        else
            File.Delete(protocolPath);

        var server = Reload();

        Assert.That(server.worldData.cmds, Is.Empty);
        Assert.That(server.worldData.savedGame, Is.EqualTo(new byte[] { 1 }));
        Assert.That(server.worldData.mapData[7], Is.EqualTo(new byte[] { 1 }));
        Assert.That(server.gameTimer, Is.EqualTo(60));
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("Discarding the saved command log (2 commands)")
            .And.Contain(protocolKnown ? "protocol 56" : "an older version"));
    }

    [Test]
    public void InterruptedJoinPointWriteDiscardsTheLog()
    {
        WriteJoinPoint(1, 5);

        // The next join point fails after writing the world
        Directory.CreateDirectory(Path.Combine(SavedDir, "cmds.dat.tmp"));
        Assert.That(() => WriteJoinPoint(2, 6), NUnit.Framework.Throws.Exception);
        Directory.Delete(Path.Combine(SavedDir, "cmds.dat.tmp"));

        var server = Reload();

        // The old log doesn't belong to the new world
        Assert.That(server.worldData.savedGame, Is.EqualTo(new byte[] { 2 }));
        Assert.That(server.worldData.cmds, Is.Empty);
    }

    [Test]
    public void SeedFromSaveZip_KeepsCommandsOnlyFromTheCurrentProtocol([Values(true, false)] bool currentProtocol)
    {
        var zipPath = Path.Combine(tempDir, "save.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            void Entry(string name, byte[] data)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(data);
            }

            Entry("world/000_save", [1]);
            Entry("world/000_cmds", ScheduledCommand.SerializeCmds([Cmd(10, ScheduledCommand.Global, 5)]));
            Entry("info", ReplayInfo.Write(new ReplayInfo
            {
                protocol = currentProtocol ? MpVersion.Protocol : MpVersion.Protocol - 1,
                sections = [new ReplaySection(10, 20)]
            }));
        }

        new StandalonePersistence(tempDir).SeedFromSaveZip(zipPath);
        var server = Reload();

        Assert.That(server.worldData.savedGame, Is.Not.Null);
        Assert.That(server.gameTimer, Is.EqualTo(20));
        Assert.That(DataBytes(server.worldData.cmds), Is.EqualTo(currentProtocol ? new byte[] { 5 } : []));
        Assert.That(errors, Has.Count.EqualTo(currentProtocol ? 0 : 1));
    }

    private static ScheduledCommand Cmd(int ticks, int mapId, byte marker) =>
        new(CommandType.Sync, ticks, 0, mapId, 0, [marker]);

    private static byte[] DataBytes(List<byte[]> cmds) =>
        cmds.Select(cmd => ScheduledCommand.Deserialize(new ByteReader(cmd)).data[0]).ToArray();

    [Test]
    public void LoadInto_FallsBackToLatestReplaySectionTick()
    {
        var persistence = new StandalonePersistence(tempDir);
        persistence.EnsureDirectories();

        File.WriteAllBytes(Path.Combine(tempDir, "Saved", "world.dat"), [1]);
        File.WriteAllBytes(Path.Combine(tempDir, "Saved", "session.dat"), []);
        File.WriteAllBytes(Path.Combine(tempDir, "Saved", "world_cmds.dat"), ScheduledCommand.SerializeCmds(new List<ScheduledCommand>()));
        File.WriteAllBytes(Path.Combine(tempDir, "Saved", "info.xml"), ReplayInfo.Write(new ReplayInfo
        {
            sections = new List<ReplaySection>
            {
                new(100, 100),
                new(999, 999),
            }
        }));

        var server = MultiplayerServer.instance = new MultiplayerServer(new ServerSettings())
        {
            IsStandaloneServer = true,
            persistence = persistence,
        };

        persistence.LoadInto(server);

        Assert.That(server.gameTimer, Is.EqualTo(999));
        Assert.That(server.startingTimer, Is.EqualTo(999));
        // An empty legacy log loses nothing
        Assert.That(errors, Is.Empty);
    }
}