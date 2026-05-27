using Multiplayer.Common;
using Multiplayer.Common.ChatCommands;

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

    private sealed class RecordingChatSource : IChatSource
    {
        public List<string> Messages { get; } = [];

        public void SendMsg(string msg)
        {
            Messages.Add(msg);
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
