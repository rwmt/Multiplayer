using Multiplayer.Common;
using Multiplayer.Common.ChatCommands;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

[TestFixture]
public class ChatCommandManagerTest
{
    [TearDown]
    public void TearDown()
    {
        MultiplayerServer.instance = null;
    }

    [Test]
    public void GeneratedRegistry_RegistersAliasesAsOneCommandInfo()
    {
        var server = MakeServer();

        Assert.That(server.chatCmdManager.TryGetCommandInfo("help", out var help), Is.True);
        Assert.That(server.chatCmdManager.TryGetCommandInfo("?", out var alias), Is.True);

        Assert.That(alias, Is.SameAs(help));
        Assert.That(help.Names, Is.EqualTo(["help", "?"]));
        Assert.That(help.Command, Is.SameAs(alias.Command));
    }

    [Test]
    public void GeneratedRegistry_StoresCommandMetadata()
    {
        var server = MakeServer();

        Assert.That(server.chatCmdManager.TryGetCommandInfo("kick", out var info), Is.True);
        Assert.That(info.Description, Is.EqualTo("Disconnect a player by username."));
        Assert.That(info.Usage, Is.EqualTo("kick <username>"));
        Assert.That(info.RequiresHost, Is.True);
    }

    [Test]
    public void ManualRegistration_GroupsAliasesForTheSameCommand()
    {
        var server = MakeServer();
        var command = new RecordingCommand();

        server.RegisterChatCommand("alias-one", command);
        server.RegisterChatCommand("alias-two", command);

        Assert.That(server.chatCmdManager.TryGetCommandInfo("alias-one", out var first), Is.True);
        Assert.That(server.chatCmdManager.TryGetCommandInfo("alias-two", out var second), Is.True);
        Assert.That(second, Is.SameAs(first));
        Assert.That(first.Names, Is.EqualTo(["alias-one", "alias-two"]));
        Assert.That(first.DisplayNames, Is.EqualTo("alias-one, alias-two"));
        Assert.That(server.chatCmdManager.GetCommandInfos().Count(info => info.Command == command), Is.EqualTo(1));
    }

    [Test]
    public void ManualRegistration_CanProvideMetadataForAliases()
    {
        var server = MakeServer();
        var command = new RecordingCommand();

        server.RegisterChatCommand(
            ["metadata", "meta"],
            command,
            "Manual description.",
            "metadata <value>",
            true
        );

        Assert.That(server.chatCmdManager.TryGetCommandInfo("metadata", out var primary), Is.True);
        Assert.That(server.chatCmdManager.TryGetCommandInfo("meta", out var alias), Is.True);
        Assert.That(alias, Is.SameAs(primary));
        Assert.That(primary.Names, Is.EqualTo(["metadata", "meta"]));
        Assert.That(primary.DisplayNames, Is.EqualTo("metadata, meta"));
        Assert.That(primary.Description, Is.EqualTo("Manual description."));
        Assert.That(primary.Usage, Is.EqualTo("metadata <value>"));
        Assert.That(primary.RequiresHost, Is.True);
    }

    [Test]
    public void Commands_DispatchCaseInsensitively()
    {
        var server = MakeServer();
        var command = new RecordingCommand();
        var source = new RecordingChatSource();

        server.RegisterChatCommand("mixed", command);
        server.HandleChatCommand(source, "MIXED");

        Assert.That(command.ExecutionCount, Is.EqualTo(1));
        Assert.That(source.Messages, Is.Empty);
    }

    [Test]
#pragma warning disable CS0618
    public void LegacyRegistration_UsesOldHandlerMetadataAndDispatch()
    {
        var server = MakeServer();
        var command = new LegacyRecordingCommand();
        var source = new RecordingChatSource();

        server.RegisterChatCmd("legacy", command);
        server.HandleChatCmd(source, "legacy hello");

        Assert.That(command.Args, Is.EqualTo(["hello"]));
        Assert.That(server.chatCmdManager.TryGetCommandInfo("legacy", out var info), Is.True);
        Assert.That(info.Description, Is.EqualTo("Legacy description."));
        Assert.That(info.Usage, Is.EqualTo("legacy <value>"));
        Assert.That(info.RequiresHost, Is.True);
    }
#pragma warning restore CS0618

    [Test]
    public void HelpCommand_PrintsMetadataForRequestedCommand()
    {
        var server = MakeServer();
        server.RegisterChatCommand(
            ["documented", "doc"],
            new RecordingCommand(),
            "Manual description.",
            "documented <value>",
            true
        );
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "help documented");

        Assert.That(
            source.Messages,
            Is.EqualTo([
                "Command: documented, doc",
                "Description: Manual description.",
                "Usage: documented <value>",
                "Requires host permissions."
            ])
        );
    }

    [Test]
    public void HostOnlyCommand_DoesNotExecuteForNonHostPlayer()
    {
        var server = MakeServer();
        var command = new RecordingCommand();
        server.RegisterChatCommand(["host-only"], command, requiresHost: true);

        var conn = new DummyConnection("guest");
        var player = new ServerPlayer(1, conn);
        conn.serverPlayer = player;
        server.hostUsername = "host";

        server.HandleChatCommand(player, "host-only");

        Assert.That(command.ExecutionCount, Is.Zero);
    }

    [Test]
    public void GeneratedTypedCommand_MissingRequiredArgumentRepliesWithUsage()
    {
        var server = MakeServer();
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "kick");

        Assert.That(source.Messages, Is.EqualTo(["Usage: kick <username>"]));
        Assert.That(source.RawMessages, Is.EqualTo(["Usage: kick <username>"]));
    }

    [Test]
    public void GeneratedTypedCommand_MissingWhoisArgumentRepliesWithUsernameUsage()
    {
        var server = MakeServer();
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "whois");

        Assert.That(source.Messages, Is.EqualTo(["Usage: whois <username>"]));
        Assert.That(source.RawMessages, Is.EqualTo(["Usage: whois <username>"]));
    }

    [Test]
    public void GeneratedTypedCommand_MissingResyncArgumentRepliesWithUsernameUsage()
    {
        var server = MakeServer();
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "resync");

        Assert.That(source.Messages, Is.EqualTo(["Usage: resync <username>"]));
        Assert.That(source.RawMessages, Is.EqualTo(["Usage: resync <username>"]));
    }

    [Test]
    public void GeneratedTypedCommand_MissingArgumentSentToPlayerKeepsRawUsageText()
    {
        var server = MakeServer();
        var player = AddPlayingPlayer(server, "host", isHost: true);

        server.HandleChatCommand(player, "resync");

        var conn = (RecordingConnection)player.conn;
        Assert.That(conn.ChatMessages, Is.EqualTo(["Usage: resync <username>"]));
        Assert.That(conn.RawChatMessages, Is.EqualTo(["Usage: resync <username>"]));
    }

    [Test]
    public void PlayersCommand_ListsConnectedPlayers()
    {
        var server = MakeServer();
        AddPlayingPlayer(server, "host", isHost: true, factionId: 4, currentMapId: 0);
        AddPlayingPlayer(server, "guest", factionId: 7, currentMapId: 2);
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "players");

        Assert.That(source.Messages[0], Is.EqualTo("Players (2):"));
        Assert.That(source.Messages[1], Does.Contain("host"));
        Assert.That(source.Messages[1], Does.Contain("[Playing]"));
        Assert.That(source.Messages[1], Does.Contain("faction=4"));
        Assert.That(source.Messages[1], Does.Contain("map=0"));
        Assert.That(source.Messages[2], Does.Contain("guest"));
        Assert.That(source.Messages[2], Does.Contain("player"));
        Assert.That(source.Messages[2], Does.Contain("faction=7"));
        Assert.That(source.Messages[2], Does.Contain("map=2"));
    }

    [Test]
    public void WhoisCommand_ShowsPlayerDetails()
    {
        var server = MakeServer();
        var player = AddPlayingPlayer(server, "guest", factionId: 7, currentMapId: 2);
        player.ticksBehind = 12;
        player.steamId = 12345;
        player.steamPersonaName = "Steam Guest";
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "whois guest");

        Assert.That(source.Messages, Does.Contain($"Player: guest (#{player.id})"));
        Assert.That(source.Messages, Does.Contain("Status: Playing"));
        Assert.That(source.Messages, Does.Contain("Role: player"));
        Assert.That(source.Messages, Does.Contain("Faction: 7"));
        Assert.That(source.Messages, Does.Contain("Map: 2"));
        Assert.That(source.Messages, Does.Contain("Ticks behind: 12"));
        Assert.That(source.Messages, Does.Contain("Steam: Steam Guest (12345)"));
    }

    [Test]
    public void PlayerArgument_ResolvesUniquePartialName()
    {
        var server = MakeServer();
        var player = AddPlayingPlayer(server, "NemuruYama", factionId: 7, currentMapId: 2);
        AddPlayingPlayer(server, "OtherPlayer");
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "whois Nemu");

        Assert.That(source.Messages, Does.Contain($"Player: NemuruYama (#{player.id})"));
    }

    [Test]
    public void PlayerArgument_RejectsAmbiguousPartialNameBeforeExecute()
    {
        var server = MakeServer();
        AddPlayingPlayer(server, "NemuruYama1");
        AddPlayingPlayer(server, "NemuruYama2");
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "whois Nemu");

        Assert.That(
            source.Messages,
            Is.EqualTo(["Player name 'Nemu' is ambiguous: NemuruYama1, NemuruYama2."])
        );
    }

    [Test]
    public void PlayerArgument_ExactNameWinsOverAmbiguousPartialName()
    {
        var server = MakeServer();
        var exact = AddPlayingPlayer(server, "Nemu");
        AddPlayingPlayer(server, "NemuruYama1");
        AddPlayingPlayer(server, "NemuruYama2");
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "whois Nemu");

        Assert.That(source.Messages, Does.Contain($"Player: Nemu (#{exact.id})"));
    }

    [Test]
    public void PlayerArgument_ResolvesQuotedPlayerName()
    {
        var server = MakeServer();
        var player = AddPlayingPlayer(server, "Player Name", factionId: 7, currentMapId: 2);
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "whois \"Player Name\"");

        Assert.That(source.Messages, Does.Contain($"Player: Player Name (#{player.id})"));
    }

    [Test]
    public void SinglePlayerArgument_UsesRemainingTextAsPlayerName()
    {
        var server = MakeServer();
        var player = AddPlayingPlayer(server, "Player Name", factionId: 7, currentMapId: 2);
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "whois Player Name");

        Assert.That(source.Messages, Does.Contain($"Player: Player Name (#{player.id})"));
    }

    [Test]
    public void QuotedArgument_MissingClosingQuoteStopsBeforeDispatch()
    {
        var server = MakeServer();
        var command = new RecordingCommand();
        var source = new RecordingChatSource();

        server.RegisterChatCommand("record", command);
        server.HandleChatCommand(source, "record \"unfinished");

        Assert.That(command.ExecutionCount, Is.Zero);
        Assert.That(source.Messages, Is.EqualTo(["Invalid command arguments: missing closing quote."]));
    }

    [Test]
    public void StatusCommand_ShowsServerSummary()
    {
        var server = MakeServer();
        server.running = true;
        server.gameTimer = 120;
        server.workTicks = 45;
        server.worldData.savedGame = [1, 2, 3];
        server.worldData.sessionData = [4, 5, 6];
        server.worldData.mapData[10] = [7, 8, 9];
        server.worldData.lastJoinPointAtTick = 90;
        AddPlayingPlayer(server, "host", isHost: true);
        AddPlayingPlayer(server, "guest");
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "status");

        Assert.That(source.Messages, Does.Contain("Server: running"));
        Assert.That(source.Messages, Does.Contain("World: loaded, maps=1, join point=last at tick 90"));
        Assert.That(source.Messages, Does.Contain("Ticks: game=120, net=0, work=45"));
        Assert.That(source.Messages, Does.Contain("Players: connected=2, joined=2, playing=2"));
    }

    [Test]
    public void ModsCommand_ShowsServerModList()
    {
        var server = MakeServer();
        server.StartInitData().SetResult(new ServerInitData(
            ClientInitDataPacket.ModData.ListBinder.Serialize([
                ModData("Core", "ludeon.rimworld"),
                ModData("Multiplayer", "rwmt.multiplayer")
            ]),
            false,
            "1.6.4633",
            [],
            [],
            default,
            []
        ));
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "mods");

        Assert.That(source.Messages, Does.Contain("RimWorld: 1.6.4633"));
        Assert.That(source.Messages, Does.Contain("Mods (2):"));
        Assert.That(source.Messages, Does.Contain("- Core (ludeon.rimworld)"));
        Assert.That(source.Messages, Does.Contain("- Multiplayer (rwmt.multiplayer)"));
    }

    [Test]
    public void ModsCommand_CanPageThroughServerModList()
    {
        var server = MakeServer();
        server.StartInitData().SetResult(new ServerInitData(
            ClientInitDataPacket.ModData.ListBinder.Serialize([
                ModData("Core", "ludeon.rimworld"),
                ModData("Royalty", "ludeon.rimworld.royalty"),
                ModData("Ideology", "ludeon.rimworld.ideology"),
                ModData("Biotech", "ludeon.rimworld.biotech"),
                ModData("Multiplayer", "rwmt.multiplayer")
            ]),
            false,
            "1.6.4633",
            [],
            [],
            default,
            []
        ));
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "mods 2 2");

        Assert.That(source.Messages, Does.Contain("Mods (5), page 2/3:"));
        Assert.That(source.Messages, Does.Contain("- Ideology (ludeon.rimworld.ideology)"));
        Assert.That(source.Messages, Does.Contain("- Biotech (ludeon.rimworld.biotech)"));
        Assert.That(source.Messages, Does.Not.Contain("- Core (ludeon.rimworld)"));
        Assert.That(source.Messages, Does.Not.Contain("- Multiplayer (rwmt.multiplayer)"));
    }

    [Test]
    public void ResyncCommand_SendsWorldDataReloadRequest()
    {
        var server = MakeServer();
        server.worldData.savedGame = [];
        server.worldData.sessionData = [];
        var player = AddPlayingPlayer(server, "guest");
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "resync guest");

        var connection = (RecordingConnection)player.conn;
        Assert.That(connection.PacketIds, Does.Contain(Packets.Server_RequestRejoin));
        Assert.That(connection.PacketIds, Does.Not.Contain(Packets.Server_WorldDataStart));
        Assert.That(connection.PacketIds, Does.Not.Contain(Packets.Server_WorldData));
        Assert.That(source.Messages, Does.Contain("Resync requested for guest."));
    }

    [Test]
    public void TimeControlCommands_SchedulePlayerScopedCommands()
    {
        var server = MakeServer();
        var source = AddPlayingPlayer(server, "guest", factionId: 7);

        server.HandleChatCommand(source, "pause");
        var pauseCommand = LastGlobalCommand(server);

        server.HandleChatCommand(source, "unpause");
        var unpauseCommand = LastGlobalCommand(server);

        server.HandleChatCommand(source, "speed 3");
        var speedCommand = LastGlobalCommand(server);

        Assert.That(pauseCommand.type, Is.EqualTo(CommandType.GlobalTimeSpeed));
        Assert.That(pauseCommand.factionId, Is.EqualTo(7));
        Assert.That(pauseCommand.playerId, Is.EqualTo(source.id));
        Assert.That(pauseCommand.data, Is.EqualTo([(byte)TimeVote.Paused]));
        Assert.That(unpauseCommand.type, Is.EqualTo(CommandType.GlobalTimeSpeed));
        Assert.That(unpauseCommand.factionId, Is.EqualTo(7));
        Assert.That(unpauseCommand.playerId, Is.EqualTo(source.id));
        Assert.That(unpauseCommand.data, Is.EqualTo([(byte)TimeVote.Normal]));
        Assert.That(speedCommand.type, Is.EqualTo(CommandType.GlobalTimeSpeed));
        Assert.That(speedCommand.factionId, Is.EqualTo(7));
        Assert.That(speedCommand.playerId, Is.EqualTo(source.id));
        Assert.That(speedCommand.data, Is.EqualTo([(byte)TimeVote.Superfast]));
        Assert.That(((RecordingConnection)source.conn).ChatMessages, Does.Contain("Speed set to Paused."));
        Assert.That(((RecordingConnection)source.conn).ChatMessages, Does.Contain("Speed set to Normal."));
        Assert.That(((RecordingConnection)source.conn).ChatMessages, Does.Contain("Speed set to Superfast."));
    }

    [Test]
    public void TimeControlCommands_UseLowestWinsVoteWhenEnabled()
    {
        var server = MakeServer();
        server.settings.timeControl = TimeControl.LowestWins;
        var source = AddPlayingPlayer(server, "guest", factionId: 7);

        server.HandleChatCommand(source, "pause");

        var command = LastGlobalCommand(server);
        var data = new ByteReader(command.data);
        Assert.That(command.type, Is.EqualTo(CommandType.TimeSpeedVote));
        Assert.That(command.factionId, Is.EqualTo(7));
        Assert.That(command.playerId, Is.EqualTo(source.id));
        Assert.That((TimeVote)data.ReadByte(), Is.EqualTo(TimeVote.Paused));
        Assert.That(data.ReadInt32(), Is.EqualTo(ScheduledCommand.Global));
    }

    [Test]
    public void TimeControlCommands_RespectHostOnlyTimeControlSetting()
    {
        var server = MakeServer();
        server.settings.timeControl = TimeControl.HostOnly;
        AddPlayingPlayer(server, "host", isHost: true);
        var guest = AddPlayingPlayer(server, "guest");

        server.HandleChatCommand(guest, "pause");

        Assert.That(server.worldData.mapCmds.ContainsKey(ScheduledCommand.Global), Is.False);
        Assert.That(((RecordingConnection)guest.conn).ChatMessages, Does.Contain("No permission"));
    }

    [Test]
    public void HelpCommand_CanShowOnlyCommandsThePlayerCanUse()
    {
        var server = MakeServer();
        server.settings.timeControl = TimeControl.HostOnly;
        AddPlayingPlayer(server, "host", isHost: true);
        var guest = AddPlayingPlayer(server, "guest");
        guest.helpOnlyUsableCommands = true;

        server.HandleChatCommand(guest, "help");

        var messages = ((RecordingConnection)guest.conn).ChatMessages;
        Assert.That(messages, Does.Contain("Available commands you can use:"));
        Assert.That(messages, Does.Contain("- help, ?: Show available commands or detailed help for one command."));
        Assert.That(messages, Does.Contain("- whois: Show details for a connected player."));
        Assert.That(messages.Any(message => message.StartsWith("- kick:")), Is.False);
        Assert.That(messages.Any(message => message.StartsWith("- pause:")), Is.False);
    }

    [Test]
    public void AnnounceCommand_BroadcastsServerAnnouncement()
    {
        var server = MakeServer();
        var player = AddPlayingPlayer(server, "guest");
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "announce raid soon");

        Assert.That(player.conn, Is.TypeOf<RecordingConnection>());
        Assert.That(((RecordingConnection)player.conn).ChatMessages, Does.Contain("[Announcement] raid soon"));
    }

    [Test]
    public void AnnounceCommand_PreservesQuotedMessageAsOneArgument()
    {
        var server = MakeServer();
        var player = AddPlayingPlayer(server, "guest");
        var source = new RecordingChatSource();

        server.HandleChatCommand(source, "announce \"raid soon\"");

        Assert.That(player.conn, Is.TypeOf<RecordingConnection>());
        Assert.That(((RecordingConnection)player.conn).ChatMessages, Does.Contain("[Announcement] raid soon"));
    }

    private static MultiplayerServer MakeServer()
    {
        return MultiplayerServer.instance = new MultiplayerServer(new ServerSettings
        {
            gameName = "Test",
            direct = false,
            lan = false
        });
    }

    private static ServerPlayer AddPlayingPlayer(
        MultiplayerServer server,
        string username,
        bool isHost = false,
        int factionId = 0,
        int currentMapId = -1
    )
    {
        var player = server.playerManager.OnConnected(new RecordingConnection(username));
        player.FactionId = factionId;
        player.currentMapId = currentMapId;
        player.hasJoined = true;
        player.status = PlayerStatus.Playing;
        player.conn.ChangeState(ConnectionStateEnum.ServerPlaying);

        if (isHost)
            server.hostUsername = username;

        return player;
    }

    private static ScheduledCommand LastGlobalCommand(MultiplayerServer server)
    {
        return ScheduledCommand.Deserialize(new ByteReader(server.worldData.mapCmds[ScheduledCommand.Global].Last()));
    }

    private static ClientInitDataPacket.ModData ModData(string name, string packageId)
    {
        return new ClientInitDataPacket.ModData
        {
            name = name,
            packageIdNonUnique = packageId,
            files = []
        };
    }

    private sealed class RecordingChatSource : IChatSource
    {
        public List<string> Messages { get; } = [];
        public List<string> RawMessages { get; } = [];

        public void SendMsg(string msg)
        {
            Messages.Add(msg);
        }

        public void SendRawMsg(string msg)
        {
            Messages.Add(msg);
            RawMessages.Add(msg);
        }
    }

    private sealed class RecordingCommand : ChatCommand
    {
        public int ExecutionCount { get; private set; }

        public override void Execute(ChatCommandContext context)
        {
            ExecutionCount++;
        }
    }

    private sealed class RecordingConnection : ConnectionBase
    {
        public List<string> ChatMessages { get; } = [];
        public List<string> RawChatMessages { get; } = [];
        public List<Packets> PacketIds { get; } = [];

        public RecordingConnection(string username)
        {
            this.username = username;
        }

        public override int Latency { get => 0; set { } }

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            var packetId = (Packets)(raw[0] & 0x3F);
            PacketIds.Add(packetId);
            if (packetId != Packets.Server_Chat)
                return;

            var packet = new ServerChatPacket();
            packet.Bind(new PacketReader(new ByteReader(raw[1..])));
            ChatMessages.Add(packet.msg ?? "");
            if (packet.rawMessage)
                RawChatMessages.Add(packet.msg ?? "");
        }

        protected override void OnClose(ServerDisconnectPacket? goodbye) { }
    }

#pragma warning disable CS0618
    private sealed class LegacyRecordingCommand : ChatCmdHandler
    {
        public string[] Args { get; private set; } = [];
        public override string Description => "Legacy description.";
        public override string Usage => "legacy <value>";

        public LegacyRecordingCommand()
        {
            requiresHost = true;
        }

        public override void Handle(IChatSource source, string[] args)
        {
            Args = args;
        }
    }
#pragma warning restore CS0618
}
