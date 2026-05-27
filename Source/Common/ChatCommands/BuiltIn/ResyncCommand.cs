namespace Multiplayer.Common.ChatCommands;

[ChatCommand("resync", Description = "Force a player to reload world data.", Usage = "resync <username>", RequiresHost = true)]
public class ResyncCommand : ChatCommand<ResyncCommandArgs>
{
    protected override void Execute(ChatCommandContext context, ResyncCommandArgs args)
    {
        var player = PlayerCommandUtility.FindPlayer(Server, args.Username);

        if (player == null)
        {
            context.Source.SendMsg("Couldn't find the player.");
            return;
        }

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

        player.conn.ChangeState(ConnectionStateEnum.ServerLoading);
        player.ResetTimeVotes();
        context.Source.SendMsg($"Resync requested for {player.Username}.");
    }
}

public readonly record struct ResyncCommandArgs([ChatArgument("username")] string Username);
