using System.Linq;

namespace Multiplayer.Common.ChatCommands;

public sealed class ChatCommandInfo(IChatCommand command, string[] names, string description, string usage, bool requiresHost)
{
    public IChatCommand Command { get; } = command;
    public string[] Names { get; } = [.. names];
    public string PrimaryName => Names.First();
    public string DisplayNames => string.Join(", ", Names);
    public string Description { get; } = description;
    public string Usage { get; } = usage;
    public bool RequiresHost { get; } = requiresHost;

    public bool CanUse(IChatSource source) => Command.CanUse(source);

    public string PermissionDeniedMessage => Command.PermissionDeniedMessage;
}
