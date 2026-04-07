using Multiplayer.Common;

namespace Tests;

[TestFixture]
public class TimeControlFallbackTest
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
            timeControl = TimeControl.HostOnly
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

    private void RemovePlayer(ServerPlayer player)
    {
        server.playerManager.Players.Remove(player);
    }

    [Test]
    public void HostOnly_HostPresent_NonHostTimeCommandRejected()
    {
        AddPlayer("host", isHost: true);
        var player = AddPlayer("player1");

        int cmdsBefore = server.commands.SentCmds;
        server.commands.Send(CommandType.MapTimeSpeed, 0, 0, Array.Empty<byte>(), sourcePlayer: player);

        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsBefore),
            "Non-host time command should be rejected when host is present");
    }

    [Test]
    public void HostOnly_HostPresent_HostTimeCommandAccepted()
    {
        var host = AddPlayer("host", isHost: true);

        int cmdsBefore = server.commands.SentCmds;
        server.commands.Send(CommandType.MapTimeSpeed, 0, 0, Array.Empty<byte>(), sourcePlayer: host);

        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsBefore + 1),
            "Host time command should be accepted");
    }

    [Test]
    public void HostOnly_HostAbsent_NonHostTimeCommandAccepted()
    {
        // Host was present but disconnected
        server.hostUsername = "host";
        var player = AddPlayer("player1");

        int cmdsBefore = server.commands.SentCmds;
        server.commands.Send(CommandType.MapTimeSpeed, 0, 0, Array.Empty<byte>(), sourcePlayer: player);

        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsBefore + 1),
            "Non-host time command should be accepted when host is absent (fallback)");
    }

    [Test]
    public void HostOnly_HostAbsent_GlobalTimeSpeedAlsoAccepted()
    {
        server.hostUsername = "host";
        var player = AddPlayer("player1");

        int cmdsBefore = server.commands.SentCmds;
        server.commands.Send(CommandType.GlobalTimeSpeed, 0, 0, Array.Empty<byte>(), sourcePlayer: player);

        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsBefore + 1),
            "Non-host GlobalTimeSpeed should be accepted when host is absent");
    }

    [Test]
    public void EveryoneControls_TimeCommandAlwaysAccepted()
    {
        server.settings.timeControl = TimeControl.EveryoneControls;
        AddPlayer("host", isHost: true);
        var player = AddPlayer("player1");

        int cmdsBefore = server.commands.SentCmds;
        server.commands.Send(CommandType.MapTimeSpeed, 0, 0, Array.Empty<byte>(), sourcePlayer: player);

        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsBefore + 1),
            "EveryoneControls should always accept time commands");
    }

    [Test]
    public void HostOnly_HostReconnects_EnforcementResumes()
    {
        // Start with host absent → fallback active
        server.hostUsername = "host";
        var player = AddPlayer("player1");

        int cmdsBefore = server.commands.SentCmds;
        server.commands.Send(CommandType.MapTimeSpeed, 0, 0, Array.Empty<byte>(), sourcePlayer: player);
        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsBefore + 1),
            "Fallback should be active when host is absent");

        // Host reconnects → enforcement resumes
        AddPlayer("host", isHost: true);

        int cmdsAfterReconnect = server.commands.SentCmds;
        server.commands.Send(CommandType.MapTimeSpeed, 0, 0, Array.Empty<byte>(), sourcePlayer: player);
        Assert.That(server.commands.SentCmds, Is.EqualTo(cmdsAfterReconnect),
            "HostOnly enforcement should resume when host reconnects");
    }
}
