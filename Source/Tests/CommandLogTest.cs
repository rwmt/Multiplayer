using System.Collections.Generic;
using System.Linq;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

/// Joiners must execute exactly the command sequence live clients executed (same order across tickables)
[TestFixture]
public class CommandLogTest
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
            lan = false
        });
        nextPlayerId = 0;
    }

    [TearDown]
    public void TearDown()
    {
        MultiplayerServer.instance = null;
    }

    private (ServerPlayer player, RecordingConnection conn) AddPlayer(string username, ConnectionStateEnum state)
    {
        var conn = new RecordingConnection(username);
        var player = new ServerPlayer(nextPlayerId++, conn) { hasJoined = true };
        conn.serverPlayer = player;
        conn.ChangeState(state);
        server.playerManager.Players.Add(player);
        return (player, conn);
    }

    private void SendCmd(int mapId, byte marker) =>
        server.commands.Send(CommandType.Sync, 0, mapId, [marker]);

    private static List<(int mapId, byte marker)> Decode(IEnumerable<byte[]> cmds) =>
        cmds.Select(raw => ScheduledCommand.Deserialize(new ByteReader(raw)))
            .Where(cmd => cmd.type == CommandType.Sync)
            .Select(cmd => (cmd.mapId, cmd.data[0]))
            .ToList();

    [Test]
    public void CommandsAreLoggedInSendOrderAcrossTickables()
    {
        var (_, conn) = AddPlayer("live", ConnectionStateEnum.ServerPlaying);

        SendCmd(5, 1);
        SendCmd(ScheduledCommand.Global, 2);
        SendCmd(3, 3);
        SendCmd(5, 4);

        var expected = new List<(int, byte)> { (5, 1), (ScheduledCommand.Global, 2), (3, 3), (5, 4) };
        Assert.That(Decode(server.worldData.cmds), Is.EqualTo(expected));

        // Live clients receive the same order
        var live = conn.SentMessages
            .Where(m => m.id == Packets.Server_Command)
            .Select(m => ScheduledCommand.Serialize(ToCmd(m.data)));
        Assert.That(Decode(live), Is.EqualTo(expected));
    }

    [Test]
    public void WorldDataContainsAllCommandsInLogOrder()
    {
        server.worldData.savedGame = [9];
        server.worldData.sessionData = [8];
        AddPlayer("live", ConnectionStateEnum.ServerPlaying);

        SendCmd(5, 1);
        SendCmd(ScheduledCommand.Global, 2);
        SendCmd(3, 3);

        var (joiner, joinerConn) = AddPlayer("joiner", ConnectionStateEnum.ServerPlaying);
        new ServerLoadingState(joinerConn).SendWorldData();

        var data = joinerConn.SentMessages.Single(m => m.id == Packets.Server_WorldData).data;
        var reader = new ByteReader(data);
        reader.ReadInt32(); // faction
        reader.ReadInt32(); // timer
        Assert.That(reader.ReadInt32(), Is.EqualTo(server.commands.SentCmds));
        reader.ReadBool(); // frozen
        Assert.That(reader.ReadPrefixedBytes(), Is.EqualTo(new byte[] { 9 }));
        Assert.That(reader.ReadPrefixedBytes(), Is.EqualTo(new byte[] { 8 }));

        int count = reader.ReadInt32();
        var cmds = Enumerable.Range(0, count).Select(_ => reader.ReadPrefixedBytes()!).ToList();
        Assert.That(Decode(cmds), Is.EqualTo(new List<(int, byte)> { (5, 1), (ScheduledCommand.Global, 2), (3, 3) }));
    }

    [Test]
    public void CompletedJoinPointKeepsOnlyCommandsAfterIt()
    {
        AddPlayer("live", ConnectionStateEnum.ServerPlaying);
        server.hostUsername = "live";

        SendCmd(5, 1);
        Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.True);
        SendCmd(ScheduledCommand.Global, 2);
        SendCmd(5, 3);
        server.worldData.EndJoinPointCreation();

        var cmds = server.worldData.cmds.Select(raw => ScheduledCommand.Deserialize(new ByteReader(raw))).ToList();
        Assert.That(cmds.Any(c => c.type == CommandType.CreateJoinPoint), Is.False);
        Assert.That(Decode(server.worldData.cmds), Is.EqualTo(new List<(int, byte)> { (ScheduledCommand.Global, 2), (5, 3) }));
    }

    [Test]
    public void AbortedJoinPointKeepsTheFullLog()
    {
        AddPlayer("live", ConnectionStateEnum.ServerPlaying);
        server.hostUsername = "live";

        SendCmd(5, 1);
        Assert.That(server.worldData.TryStartJoinPointCreation(true), Is.True);
        SendCmd(ScheduledCommand.Global, 2);
        server.worldData.AbortJoinPointCreation();

        var types = server.worldData.cmds.Select(raw => ScheduledCommand.Deserialize(new ByteReader(raw)).type);
        // The joiner replays the aborted CreateJoinPoint too, so it reloads at the same tick as live clients did
        Assert.That(types, Is.EqualTo(new[] { CommandType.Sync, CommandType.CreateJoinPoint, CommandType.Sync }));
    }

    [Test]
    public void LargestAllowedCommandIsLoggedAndBroadcast()
    {
        var (_, conn) = AddPlayer("live", ConnectionStateEnum.ServerPlaying);

        server.commands.Send(CommandType.Sync, 0, 1, new byte[ServerCommandPacket.MaxDataLength]);

        Assert.That(server.worldData.cmds, Has.Count.EqualTo(1));
        Assert.That(conn.SentPackets, Does.Contain(Packets.Server_Command));
    }

    [Test]
    public void TooLargeCommandIsNeitherLoggedNorBroadcast()
    {
        var (_, conn) = AddPlayer("live", ConnectionStateEnum.ServerPlaying);
        var errors = new List<string>();
        var prevError = ServerLog.error;
        ServerLog.error = errors.Add;

        try
        {
            server.commands.Send(CommandType.Sync, 0, 1, new byte[ServerCommandPacket.MaxDataLength + 1]);
        }
        finally
        {
            ServerLog.error = prevError;
        }

        Assert.That(server.worldData.cmds, Is.Empty);
        Assert.That(conn.SentPackets, Does.Not.Contain(Packets.Server_Command));
        Assert.That(server.commands.SentCmds, Is.EqualTo(0));
        Assert.That(errors, Has.Count.EqualTo(1));
    }

    [Test]
    public void ClientCommandDataIsCappedAtRebroadcastLimit()
    {
        var ok = new ClientCommandPacket(CommandType.Sync, 1, new byte[ServerCommandPacket.MaxDataLength]);
        Assert.DoesNotThrow(() => ok.Serialize());

        var tooBig = new ClientCommandPacket(CommandType.Sync, 1, new byte[ServerCommandPacket.MaxDataLength + 1]);
        Assert.Catch(() => tooBig.Serialize());
    }

    [Test]
    public void CurrentFormatLogsAreNotReordered()
    {
        var log = new List<ScheduledCommand>
        {
            new(CommandType.Sync, 10, 0, 5, 0, [1]),
            new(CommandType.Sync, 10, 0, ScheduledCommand.Global, 0, [2]),
            new(CommandType.Sync, 3, 0, 5, 0, [3]), // e.g. after a debug timer reset
        };

        Assert.That(ScheduledCommand.MergeLegacyCmds(log, [[], []]), Is.SameAs(log));
    }

    private static ScheduledCommand ToCmd(byte[] packetData)
    {
        var packet = default(ServerCommandPacket);
        packet.Bind(new PacketReader(new ByteReader(packetData)));
        return packet.ToCommand();
    }
}
