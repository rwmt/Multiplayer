using Multiplayer.Common;

namespace Multiplayer.Common.ChatCommands;

public abstract class ChatCommand : IChatCommand
{
    protected MultiplayerServer Server => MultiplayerServer.instance!;

    public abstract void Execute(ChatCommandContext context);

    public ServerPlayer? FindPlayer(string username)
    {
        return Server.GetPlayer(username);
    }
}
