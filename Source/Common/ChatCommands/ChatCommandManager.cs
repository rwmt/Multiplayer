using System;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.Common;

namespace Multiplayer.Common.ChatCommands;

public class ChatCommandManager
{
    private readonly MultiplayerServer server;
    private readonly IDictionary<string, ChatCommandRegistration> handlers = new Dictionary<string, ChatCommandRegistration>(StringComparer.OrdinalIgnoreCase);

    public ChatCommandManager(MultiplayerServer server)
    {
        this.server = server;
    }

    public void Handle(IChatSource source, string cmd)
    {
        if (!CommandTokenizer.TryTokenize(cmd, out var parts, out var error))
        {
            source.SendMsg(error ?? "Invalid command arguments.");
            return;
        }

        if (parts.Length == 0)
            return;

        if (handlers.TryGetValue(parts[0], out var registration))
        {
            if (!registration.Info.CanUse(source))
                source.SendMsg(registration.Info.PermissionDeniedMessage);
            else
                registration.Command.Execute(new ChatCommandContext(source, parts[0], parts.Skip(1).ToArray()));
        }
        else
        {
            source.SendMsg("Invalid command. Use help or ? to list available commands.");
        }
    }

    public void AddCommand(string name, IChatCommand command, ChatCommandInfo info) =>
        AddCommands([name], command, info);

    public void AddCommand(string name, IChatCommand command)
    {
        var names = handlers
            .Where(entry => ReferenceEquals(entry.Value.Command, command))
            .Select(entry => entry.Key)
            .Append(name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var info = new ChatCommandInfo(command, names, GetDescription(command), GetUsage(command), RequiresHost(command));
        AddCommands(names, command, info);
    }

    public void AddCommands(string[] names, IChatCommand command, string description, string usage, bool requiresHost)
    {
        var registeredNames = names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var info = new ChatCommandInfo(command, registeredNames, description, usage, requiresHost);
        AddCommands(registeredNames, command, info);
    }

    private void AddCommands(string[] names, IChatCommand command, ChatCommandInfo info)
    {
        if (command is ChatCommand chatCommand)
            chatCommand.ConfigurePermissions(info.RequiresHost);

        foreach (var name in names)
            handlers[name] = new ChatCommandRegistration(command, info);
    }

    private static string GetDescription(IChatCommand command) =>
        command is IChatCommandMetadata metadata ? metadata.Description : string.Empty;

    private static string GetUsage(IChatCommand command) =>
        command is IChatCommandMetadata metadata ? metadata.Usage : string.Empty;

    private static bool RequiresHost(IChatCommand command) =>
        command is IChatCommandMetadata { RequiresHost: true };

    public IEnumerable<string> GetCommandNames() => handlers.Keys.OrderBy(name => name);

    public IEnumerable<ChatCommandInfo> GetCommandInfos()
    {
        return handlers
            .Select(entry => entry.Value.Info)
            .Distinct()
            .OrderBy(info => info.PrimaryName);
    }

    public bool TryGetCommandInfo(string name, out ChatCommandInfo info)
    {
        if (handlers.TryGetValue(name, out var registration))
        {
            info = registration.Info;
            return true;
        }

        info = null!;
        return false;
    }
}
