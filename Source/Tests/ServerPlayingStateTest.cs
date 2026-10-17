using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

[NonParallelizable]
public class ServerPlayingStateTest
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

    private (ServerPlayer player, RecordingConnection conn) AddPlayer(string username)
    {
        var conn = new RecordingConnection(username);
        var player = new ServerPlayer(nextPlayerId++, conn);
        conn.serverPlayer = player;
        conn.ChangeState(ConnectionStateEnum.ServerPlaying);
        server.playerManager.Players.Add(player);
        return (player, conn);
    }

    [TestCase(CommandType.PauseAll, ScheduledCommand.Global)]
    [TestCase(CommandType.CreateJoinPoint, ScheduledCommand.Global)]
    [TestCase(CommandType.InitPlayerData, ScheduledCommand.Global)]
    [TestCase(CommandType.GlobalTimeSpeed, 0)]
    [TestCase(CommandType.TimeSpeedVote, 3)]
    [TestCase(CommandType.PlayerCount, 1)]
    [TestCase(CommandType.Designator, ScheduledCommand.Global)]
    [TestCase(CommandType.MapTimeSpeed, ScheduledCommand.Global)]
    [TestCase(CommandType.Sync, -2)]
    [TestCase(CommandType.DebugTools, int.MinValue)]
    [TestCase((CommandType)200, ScheduledCommand.Global)]
    public void InvalidClientCommandIsRejected(CommandType type, int mapId)
    {
        var (player, _) = AddPlayer("player");
        var state = player.conn.GetState<ServerPlayingState>()!;

        Assert.Throws<PacketReadException>(() =>
            state.HandleClientCommand(new ClientCommandPacket(type, mapId, ByteWriter.GetBytes(-1, 0))));
        Assert.That(server.commands.SentCmds, Is.Zero);
        Assert.That(server.worldData.mapCmds, Is.Empty);
    }

    [TestCase(CommandType.GlobalTimeSpeed, ScheduledCommand.Global)]
    [TestCase(CommandType.TimeSpeedVote, ScheduledCommand.Global)]
    [TestCase(CommandType.PlayerCount, ScheduledCommand.Global)]
    [TestCase(CommandType.Designator, 0)]
    [TestCase(CommandType.MapTimeSpeed, 5)]
    [TestCase(CommandType.DebugTools, ScheduledCommand.Global)]
    [TestCase(CommandType.DebugTools, 3)]
    public void ValidClientCommandIsScheduled(CommandType type, int mapId)
    {
        var (player, conn) = AddPlayer("player");
        var state = player.conn.GetState<ServerPlayingState>()!;
        server.settings.debugMode = true;
        server.settings.devModeScope = DevModeScope.Everyone;

        state.HandleClientCommand(new ClientCommandPacket(type, mapId, ByteWriter.GetBytes(-1, 0)));

        Assert.That(server.commands.SentCmds, Is.EqualTo(1));
        Assert.That(conn.SentPackets, Does.Contain(Packets.Server_Command));
    }

    [Test]
    public void ServerOnlyCommandDisconnectsSender()
    {
        var (player, conn) = AddPlayer("player");

        var cmd = new ClientCommandPacket(CommandType.InitPlayerData, ScheduledCommand.Global,
            ByteWriter.GetBytes(player.id, true)).Serialize();
        var raw = new byte[cmd.data.Length + 1];
        raw[0] = (byte)cmd.id;
        cmd.data.CopyTo(raw, 1);

        var prevError = ServerLog.error;
        ServerLog.error = _ => { };
        try
        {
            player.HandleReceive(new ByteReader(raw), true);
        }
        finally
        {
            ServerLog.error = prevError;
        }

        Assert.That(conn.State, Is.EqualTo(ConnectionStateEnum.Disconnected));
        Assert.That(server.commands.SentCmds, Is.Zero);
    }

    [Test]
    public void PlayerCanOnlySetOwnFaction()
    {
        var (alice, aliceConn) = AddPlayer("alice");
        var (bob, _) = AddPlayer("bob");
        alice.FactionId = 1;
        bob.FactionId = 1;

        var state = alice.conn.GetState<ServerPlayingState>()!;
        state.HandleSetFaction(new ClientSetFactionPacket(bob.id, 2));

        Assert.That(bob.FactionId, Is.EqualTo(1));
        Assert.That(aliceConn.SentPackets, Does.Not.Contain(Packets.Server_SetFaction));

        state.HandleSetFaction(new ClientSetFactionPacket(alice.id, 2));

        Assert.That(alice.FactionId, Is.EqualTo(2));
        Assert.That(aliceConn.SentPackets, Does.Contain(Packets.Server_SetFaction));
    }

    [Test]
    public void HostCanSetOtherPlayersFaction()
    {
        var (host, _) = AddPlayer("host");
        var (bob, _) = AddPlayer("bob");
        server.hostUsername = "host";
        bob.FactionId = 1;

        host.conn.GetState<ServerPlayingState>()!.HandleSetFaction(new ClientSetFactionPacket(bob.id, 2));

        Assert.That(bob.FactionId, Is.EqualTo(2));
    }

    [TestCase(-1000, 0)]
    [TestCase(int.MinValue, 0)]
    [TestCase(int.MaxValue, ServerPlayer.MaxTicksBehind)]
    [TestCase(50, 50)]
    public void KeepAliveTicksBehindIsClamped(int reported, int expected)
    {
        var (player, _) = AddPlayer("player");
        player.conn.GetState<ServerPlayingState>()!.HandleClientKeepAlive(new ClientKeepAlivePacket(0, reported, false, 0));
        Assert.That(player.ticksBehind, Is.EqualTo(expected));
    }

    [TestCase(float.NaN, 0f)]
    [TestCase(float.PositiveInfinity, ServerPlayingState.MaxFrameTime)]
    [TestCase(-5f, 0f)]
    [TestCase(20f, 20f)]
    public void FrameTimeIsSanitized(float reported, float expected)
    {
        var (player, _) = AddPlayer("player");
        player.conn.GetState<ServerPlayingState>()!.HandleFrameTime(new ClientFrameTimePacket(reported));
        Assert.That(player.frameTime, Is.EqualTo(expected));
    }

    [Test]
    public void StaleKeepAliveDisconnectsPlayingPlayer()
    {
        var (host, hostConn) = AddPlayer("host");
        server.hostUsername = "host";
        var (loading, loadingConn) = AddPlayer("loading");
        var (stale, staleConn) = AddPlayer("stale");
        var (fresh, freshConn) = AddPlayer("fresh");

        foreach (var p in new[] { host, stale, fresh })
            p.status = PlayerStatus.Playing;

        host.keepAliveAt = loading.keepAliveAt = stale.keepAliveAt = -PlayerManager.StaleKeepAliveNetTicks - 1;
        fresh.keepAliveAt = -PlayerManager.StaleKeepAliveNetTicks + 10;

        server.playerManager.CheckTimeouts();

        Assert.That(staleConn.State, Is.EqualTo(ConnectionStateEnum.Disconnected));
        Assert.That(hostConn.State, Is.EqualTo(ConnectionStateEnum.ServerPlaying)); // In-memory host connection
        Assert.That(loadingConn.State, Is.EqualTo(ConnectionStateEnum.ServerPlaying)); // Not in game yet
        Assert.That(freshConn.State, Is.EqualTo(ConnectionStateEnum.ServerPlaying));
    }
}
