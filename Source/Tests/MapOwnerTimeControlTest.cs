using Multiplayer.Common;

namespace Tests;

[TestFixture]
public class MapOwnerTimeControlTest
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
            timeControl = TimeControl.MapOwners
        });
        nextPlayerId = 1;
    }

    [TearDown]
    public void TearDown()
    {
        MultiplayerServer.instance = null;
    }

    private ServerPlayer AddPlayer(string username, bool isHost = false)
    {
        var conn = new DummyConnection(username);
        var player = new ServerPlayer(nextPlayerId++, conn);
        conn.serverPlayer = player;
        conn.ChangeState(ConnectionStateEnum.ServerPlaying);

        if (isHost)
            server.hostUsername = username;

        server.playerManager.Players.Add(player);
        return player;
    }

    private static byte[] MapTime(bool claimedMap) => TimeControlPolicy.MapTimeSpeedData(Speed, claimedMap);

    // RimWorld's TimeSpeed is not referenced here, any speed byte is the first byte of the payload
    private const byte Speed = 2;

    [Test]
    public void MapOwners_OwnedMapTimeAccepted()
    {
        AddPlayer("host", isHost: true);
        var player = AddPlayer("player1");

        int cmdsBefore = server.commands.SentCmds;
        server.commands.Send(CommandType.MapTimeSpeed, 0, 0, MapTime(true), sourcePlayer: player);

        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsBefore + 1),
            "A player that claims the map should change its time");
    }

    [Test]
    public void MapOwners_OtherPlayersMapTimeRejected()
    {
        AddPlayer("host", isHost: true);
        var player = AddPlayer("player1");

        int cmdsBefore = server.commands.SentCmds;
        server.commands.Send(CommandType.MapTimeSpeed, 0, 0, MapTime(false), sourcePlayer: player);

        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsBefore),
            "A player without colonists on the map should not change its time");
    }

    [Test]
    public void MapOwners_HostChangesAnyMap()
    {
        var host = AddPlayer("host", isHost: true);
        AddPlayer("player1");

        int cmdsBefore = server.commands.SentCmds;
        server.commands.Send(CommandType.MapTimeSpeed, 0, 0, MapTime(false), sourcePlayer: host);

        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsBefore + 1),
            "The host controls the time of every map");
    }

    [Test]
    public void MapOwners_HostAbsent_EveryoneChangesMaps()
    {
        // The host was there, but its player is gone: nobody should be stuck with no time control
        server.hostUsername = "host";
        var player = AddPlayer("player1");

        int cmdsBefore = server.commands.SentCmds;
        server.commands.Send(CommandType.MapTimeSpeed, 0, 0, MapTime(false), sourcePlayer: player);

        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsBefore + 1),
            "Without a host in the session every player changes every map");
    }

    [Test]
    public void MapOwners_GlobalTimeSpeedIsHostOnly()
    {
        var host = AddPlayer("host", isHost: true);
        var player = AddPlayer("player1");

        int cmdsBefore = server.commands.SentCmds;
        server.commands.Send(CommandType.GlobalTimeSpeed, 0, 0, [Speed], sourcePlayer: player);
        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsBefore),
            "The time of the whole session is host only");

        server.commands.Send(CommandType.GlobalTimeSpeed, 0, 0, [Speed], sourcePlayer: host);
        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsBefore + 1),
            "The host changes the time of the whole session");
    }

    [Test]
    public void MapOwners_ClientWithoutClaimKeepsPreviousBehaviour()
    {
        AddPlayer("host", isHost: true);
        var player = AddPlayer("player1");

        int cmdsBefore = server.commands.SentCmds;
        server.commands.Send(CommandType.MapTimeSpeed, 0, 0, [Speed], sourcePlayer: player);

        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsBefore + 1),
            "A client that does not report ownership is not restricted, older sessions keep working");
    }

    [Test]
    public void MapOwners_ClaimDoesNotRelaxHostOnly()
    {
        server.settings.timeControl = TimeControl.HostOnly;
        AddPlayer("host", isHost: true);
        var player = AddPlayer("player1");

        int cmdsBefore = server.commands.SentCmds;
        server.commands.Send(CommandType.MapTimeSpeed, 0, 0, MapTime(true), sourcePlayer: player);

        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsBefore),
            "The claim only selects the maps of MapOwners, HostOnly stays host only");
    }

    [Test]
    public void EveryoneControls_ClaimIgnored()
    {
        server.settings.timeControl = TimeControl.EveryoneControls;
        AddPlayer("host", isHost: true);
        var player = AddPlayer("player1");

        int cmdsBefore = server.commands.SentCmds;
        server.commands.Send(CommandType.MapTimeSpeed, 0, 0, MapTime(false), sourcePlayer: player);

        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsBefore + 1),
            "EveryoneControls is unaffected by the claim");
    }
}
