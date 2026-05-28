namespace Multiplayer.Common.ChatCommands;

[ChatCommand("announce", Description = "Broadcast a server announcement.", Usage = "announce <message>", RequiresHost = true)]
public class AnnounceCommand : ChatCommand<AnnounceCommandArgs>
{
    protected override void Execute(ChatCommandContext context, AnnounceCommandArgs args)
    {
        Server.SendChat($"[Announcement] {args.Message}");
    }
}

public readonly record struct AnnounceCommandArgs(string Message);
