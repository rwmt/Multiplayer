using Multiplayer.Common;

namespace Tests;

[TestFixture]
public class StandaloneMapStreamingTest
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
        })
        {
            IsStandaloneServer = true,
        };
        nextPlayerId = 1;
    }

    [TearDown]
    public void TearDown()
    {
        MultiplayerServer.instance = null;
    }

    private (ServerPlayer player, RecordingConnection conn) AddPlayer(string username, int currentMapId, bool hasReportedCurrentMap = true)
    {
        var conn = new RecordingConnection(username);
        var player = new ServerPlayer(nextPlayerId++, conn)
        {
            currentMapId = currentMapId,
            hasReportedCurrentMap = hasReportedCurrentMap,
        };
        conn.serverPlayer = player;
        conn.ChangeState(ConnectionStateEnum.ServerPlaying);
        server.playerManager.Players.Add(player);
        return (player, conn);
    }

    [Test]
    public void StandaloneFiltering_SendsMapCommandsOnlyToPlayersOnThatMap()
    {
        server.worldData.mapData[1] = [1, 2, 3];
        var (playerOnMap, connOnMap) = AddPlayer("map1", 1);
        var (_, connOtherMap) = AddPlayer("map2", 2);

        server.commands.Send(CommandType.Designator, 0, 1, [], sourcePlayer: playerOnMap);

        Assert.That(connOnMap.SentPackets, Does.Contain(Packets.Server_Command));
        Assert.That(connOtherMap.SentPackets, Does.Not.Contain(Packets.Server_Command));
    }

    [Test]
    public void NonStandaloneServer_KeepsBroadcastBehavior()
    {
        server.IsStandaloneServer = false;
        server.worldData.mapData[1] = [1, 2, 3];
        var (playerOnMap, connOnMap) = AddPlayer("map1", 1);
        var (_, connOtherMap) = AddPlayer("map2", 2);

        server.commands.Send(CommandType.Designator, 0, 1, [], sourcePlayer: playerOnMap);

        Assert.That(connOnMap.SentPackets, Does.Contain(Packets.Server_Command));
        Assert.That(connOtherMap.SentPackets, Does.Contain(Packets.Server_Command));
    }

    [Test]
    public void PlayerCountMapSwitch_SendsMapResponseForStandaloneSnapshotMaps()
    {
        server.worldData.mapData[5] = [9, 9, 9];
        server.worldData.mapCmds[5] = [ScheduledCommand.Serialize(new ScheduledCommand(CommandType.Designator, 10, 0, 5, 1, []))];
        var (player, conn) = AddPlayer("player", -1, hasReportedCurrentMap: false);

        var state = player.conn.GetState<ServerPlayingState>()!;
        state.HandleClientCommand(new Multiplayer.Common.Networking.Packet.ClientCommandPacket(
            CommandType.PlayerCount,
            ScheduledCommand.Global,
            ByteWriter.GetBytes(-1, 5)
        ));

        Assert.That(player.currentMapId, Is.EqualTo(5));
        Assert.That(player.hasReportedCurrentMap, Is.True);
        Assert.That(conn.SentPackets, Does.Contain(Packets.Server_MapResponse));
    }
}