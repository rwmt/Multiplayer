namespace Multiplayer.Common.ChatCommands;

public interface IChatCommand
{
    bool CanUse(IChatSource source);

    string PermissionDeniedMessage { get; }

    void Execute(ChatCommandContext context);
}
