using System;
using System.Linq;
using Multiplayer.Common.ChatCommands;

namespace Multiplayer.Common;

[Obsolete("Use Multiplayer.Common.ChatCommands.ChatCommandInfo instead.")]
public sealed class ChatCmdInfo(ChatCmdHandler handler, string[] names)
{
    public ChatCmdHandler Handler { get; } = handler;
    public string[] Names { get; } = names;
    public string PrimaryName => Names.First();
    public string DisplayNames => string.Join(", ", Names);
}

[Obsolete("Use Multiplayer.Common.ChatCommands.ChatCommand or ChatCommand<TArgs> instead.")]
public abstract class ChatCmdHandler : IChatCommand, IChatCommandMetadata
{
    public bool requiresHost;

    public MultiplayerServer Server => MultiplayerServer.instance!;

    public virtual string Description => string.Empty;
    public virtual string Usage => string.Empty;
    bool IChatCommandMetadata.RequiresHost => requiresHost;
    public virtual string PermissionDeniedMessage => "No permission";

    public virtual bool CanUse(IChatSource source)
    {
        return !requiresHost || source is not ServerPlayer { IsHost: false };
    }

    public void Execute(ChatCommandContext context)
    {
        Handle(context.Source, context.RawArgs.ToArray());
    }

    public abstract void Handle(IChatSource source, string[] args);

    public void SendNoPermission(ServerPlayer player)
    {
        player.SendMsg("You don't have permission.");
    }

    public ServerPlayer? FindPlayer(string username)
    {
        return Server.GetPlayer(username);
    }
}

internal interface IChatCommandMetadata
{
    string Description { get; }
    string Usage { get; }
    bool RequiresHost { get; }
}
