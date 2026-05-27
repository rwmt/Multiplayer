using Multiplayer.Common;

namespace Multiplayer.Common.ChatCommands;

public readonly record struct HelpCommandArgs(string? Command = null);

[ChatCommand("help", "?", Description = "Show available commands or detailed help for one command.", Usage = "help [command]")]
public class HelpCommand : ChatCommand<HelpCommandArgs>
{
    protected override void Execute(ChatCommandContext context, HelpCommandArgs args)
    {
        var source = context.Source;
        if (args.Command != null)
        {
            if (Server.chatCmdManager.TryGetCommandInfo(args.Command, out var command))
            {
                source.SendMsg($"Command: {command.DisplayNames}");

                if (!string.IsNullOrEmpty(command.Description))
                    source.SendMsg($"Description: {command.Description}");

                if (!string.IsNullOrEmpty(command.Usage))
                    source.SendMsg($"Usage: {command.Usage}");

                if (command.RequiresHost)
                    source.SendMsg("Requires host permissions.");

                return;
            }

            source.SendMsg($"Unknown command '{args.Command}'. Use help to list available commands.");
            return;
        }

        source.SendMsg("Available commands:");
        foreach (var command in Server.chatCmdManager.GetCommandInfos())
        {
            var summary = command.Description;
            if (command.RequiresHost)
                summary = string.IsNullOrEmpty(summary) ? "Requires host permissions." : $"{summary} Requires host permissions.";

            source.SendMsg($"- {command.DisplayNames}: {summary}");
        }

        source.SendMsg("Use help <command> for detailed usage.");
    }
}
