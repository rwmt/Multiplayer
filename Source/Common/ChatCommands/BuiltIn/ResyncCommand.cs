using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common.ChatCommands;

[ChatCommand("resync", Description = "Force a player to reload world data.", Usage = "resync <username>", RequiresHost = true)]
public class ResyncCommand : ChatCommand<ResyncCommandArgs>
{
    protected override void Execute(ChatCommandContext context, ResyncCommandArgs args)
    {
        ServerPlayer player = args.Player;

        if (player.IsHost)
        {
            context.Source.SendMsg("You can't force-resync the host.");
            return;
        }

        if (!player.IsPlaying)
        {
            context.Source.SendMsg("Player is not in the playing state.");
            return;
        }

        player.SendPacket(new ServerRequestRejoinPacket());
        context.Source.SendMsg($"Resync requested for {player.Username}.");
    }
}

public readonly record struct ResyncCommandArgs([ChatArgument("username")] ServerPlayer Player);
