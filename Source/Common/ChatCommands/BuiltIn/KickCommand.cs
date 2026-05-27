using Multiplayer.Common;

namespace Multiplayer.Common.ChatCommands;

public readonly record struct KickCommandArgs([ChatArgument("username")] string Username);

[ChatCommand("kick", Description = "Disconnect a player by username.", Usage = "kick <username>", RequiresHost = true)]
public class KickCommand : ChatCommand<KickCommandArgs>
{
    protected override void Execute(ChatCommandContext context, KickCommandArgs args)
    {
        var toKick = FindPlayer(args.Username);
        if (toKick == null)
        {
            context.Source.SendMsg("Couldn't find the player.");
            return;
        }

        if (toKick.IsHost)
        {
            context.Source.SendMsg("You can't kick the host.");
            return;
        }

        toKick.Disconnect(MpDisconnectReason.Kick);
    }
}
