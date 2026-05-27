namespace Multiplayer.Common.ChatCommands;

[ChatCommand("pause", Description = "Pause the multiplayer session.", Usage = "pause", RequiresHost = true)]
public class PauseCommand : ChatCommand
{
    public override void Execute(ChatCommandContext context)
    {
        Server.commands.PauseAll();
        context.Source.SendMsg("Pause requested.");
    }
}

[ChatCommand("unpause", Description = "Resume the multiplayer session at normal speed.", Usage = "unpause", RequiresHost = true)]
public class UnpauseCommand : ChatCommand
{
    public override void Execute(ChatCommandContext context)
    {
        TimeControlCommandUtil.SetGlobalSpeed(Server, TimeVote.Normal);
        context.Source.SendMsg("Speed set to Normal.");
    }
}

[ChatCommand("speed", Description = "Set global multiplayer speed.", Usage = "speed <1-4>", RequiresHost = true)]
public class SpeedCommand : ChatCommand<SpeedCommandArgs>
{
    protected override void Execute(ChatCommandContext context, SpeedCommandArgs args)
    {
        if (args.Speed is < 1 or > 4)
        {
            context.Source.SendMsg("Usage: speed <1-4>");
            return;
        }

        var speed = (TimeVote)args.Speed;
        TimeControlCommandUtil.SetGlobalSpeed(Server, speed);
        context.Source.SendMsg($"Speed set to {speed}.");
    }
}

public readonly record struct SpeedCommandArgs([ChatArgument("speed")] int Speed);

internal static class TimeControlCommandUtil
{
    public static void SetGlobalSpeed(MultiplayerServer server, TimeVote speed)
    {
        server.commands.Send(
            CommandType.GlobalTimeSpeed,
            ScheduledCommand.NoFaction,
            ScheduledCommand.Global,
            [(byte)speed]
        );
    }
}
