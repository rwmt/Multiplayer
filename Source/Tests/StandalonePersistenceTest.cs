using System;
using System.Collections.Generic;
using System.IO;
using Multiplayer.Common;

namespace Tests;

[TestFixture]
public class StandalonePersistenceTest
{
    private string tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"mp-standalone-persistence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
    }

    [TearDown]
    public void TearDown()
    {
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

        var worldCmd = ScheduledCommand.Serialize(new ScheduledCommand(CommandType.Sync, 1234, 1, ScheduledCommand.Global, 5, [10]));
        var mapCmd = ScheduledCommand.Serialize(new ScheduledCommand(CommandType.Designator, 1234, 1, 7, 5, [11]));
        server.worldData.mapCmds[ScheduledCommand.Global] = [worldCmd];
        server.worldData.mapCmds[7] = [mapCmd];

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
        Assert.That(reloadedServer.worldData.mapCmds[ScheduledCommand.Global], Has.Count.EqualTo(1));
        Assert.That(reloadedServer.worldData.mapCmds[7], Has.Count.EqualTo(1));

        var reloadedWorldCmd = ScheduledCommand.Deserialize(new ByteReader(reloadedServer.worldData.mapCmds[ScheduledCommand.Global][0]));
        var reloadedMapCmd = ScheduledCommand.Deserialize(new ByteReader(reloadedServer.worldData.mapCmds[7][0]));

        Assert.That(reloadedWorldCmd.ticks, Is.EqualTo(1234));
        Assert.That(reloadedMapCmd.ticks, Is.EqualTo(1234));
        Assert.That(reloadedServer.worldData.standaloneWorldSnapshot.tick, Is.EqualTo(1234));
        Assert.That(reloadedServer.worldData.standaloneMapSnapshots[7].tick, Is.EqualTo(1234));
    }

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
    }
}