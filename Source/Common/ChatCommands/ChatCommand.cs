using Multiplayer.Common;

namespace Multiplayer.Common.ChatCommands;

public abstract class ChatCommand : IChatCommand
{
    private bool requiresHost;

    protected MultiplayerServer Server => MultiplayerServer.instance!;

    internal void ConfigurePermissions(bool requiresHost)
    {
        this.requiresHost = requiresHost;
    }

    public virtual bool CanUse(IChatSource source)
    {
        return !requiresHost || source is not ServerPlayer { IsHost: false };
    }

    public virtual string PermissionDeniedMessage => "No permission";

    public abstract void Execute(ChatCommandContext context);

    public ServerPlayer? FindPlayer(string username)
    {
        return Server.GetPlayer(username);
    }
}
