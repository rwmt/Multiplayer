using Multiplayer.Common;

namespace Multiplayer.Common.ChatCommands;

[ChatCommand("joinpoint", Description = "Create a fresh join point immediately.", Usage = "joinpoint", RequiresHost = true)]
public class JoinPointCommand : ChatCommand
{
    public override void Execute(ChatCommandContext context)
    {
        if (!Server.worldData.TryStartJoinPointCreation(true, sourcePlayer: context.Source as ServerPlayer))
            context.Source.SendMsg("Join point creation already in progress.");
    }
}
