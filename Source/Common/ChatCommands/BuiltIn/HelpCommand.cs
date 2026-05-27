using System.Linq;
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
                    source.SendRawMsg($"Usage: {command.Usage}");

                if (command.RequiresHost)
                    source.SendMsg("Requires host permissions.");

                return;
            }

            source.SendMsg($"Unknown command '{args.Command}'. Use help to list available commands.");
            return;
        }

        var onlyUsable = source is ServerPlayer { helpOnlyUsableCommands: true };
        source.SendMsg(onlyUsable ? "Available commands you can use:" : "Available commands:");

        var commands = Server.chatCmdManager.GetCommandInfos();
        if (onlyUsable)
            commands = commands.Where(command => command.CanUse(source));

        foreach (var command in commands)
        {
            var summary = command.Description;
            if (command.RequiresHost)
                summary = string.IsNullOrEmpty(summary) ? "Requires host permissions." : $"{summary} Requires host permissions.";

            source.SendMsg($"- {command.DisplayNames}: {summary}");
        }

        source.SendRawMsg("Use help <command> for detailed usage.");
    }
}
