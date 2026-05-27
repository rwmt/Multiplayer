using Multiplayer.Common;

namespace Multiplayer.Common.ChatCommands;

[ChatCommand("stop", Description = "Stop the standalone server.", Usage = "stop", RequiresHost = true)]
public class StopCommand : ChatCommand
{
    public override void Execute(ChatCommandContext context)
    {
        Server.running = false;
    }
}
