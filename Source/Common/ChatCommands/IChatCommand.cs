namespace Multiplayer.Common.ChatCommands;

public interface IChatCommand
{
    void Execute(ChatCommandContext context);
}
