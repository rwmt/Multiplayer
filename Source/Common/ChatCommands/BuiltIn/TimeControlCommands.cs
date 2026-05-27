using System.Linq;

namespace Multiplayer.Common.ChatCommands;

[ChatCommand("pause", Description = "Pause the multiplayer session.", Usage = "pause")]
public class PauseCommand : TimeControlCommand
{
    public override void Execute(ChatCommandContext context)
    {
        TimeControlCommandUtil.SetSpeed(Server, context.Source, TimeVote.Paused);
        context.Source.SendMsg("Speed set to Paused.");
    }
}

[ChatCommand("unpause", Description = "Resume the multiplayer session at normal speed.", Usage = "unpause")]
public class UnpauseCommand : TimeControlCommand
{
    public override void Execute(ChatCommandContext context)
    {
        TimeControlCommandUtil.SetSpeed(Server, context.Source, TimeVote.Normal);
        context.Source.SendMsg("Speed set to Normal.");
    }
}

[ChatCommand("speed", Description = "Set global multiplayer speed.", Usage = "speed <1-4>")]
public class SpeedCommand : TimeControlCommand<SpeedCommandArgs>
{
    protected override void Execute(ChatCommandContext context, SpeedCommandArgs args)
    {
        if (args.Speed is < 1 or > 4)
        {
            context.Source.SendRawMsg("Usage: speed <1-4>");
            return;
        }

        var speed = (TimeVote)args.Speed;
        TimeControlCommandUtil.SetSpeed(Server, context.Source, speed);
        context.Source.SendMsg($"Speed set to {speed}.");
    }
}

public readonly record struct SpeedCommandArgs([ChatArgument("speed")] int Speed);

public abstract class TimeControlCommand : ChatCommand
{
    public override bool CanUse(IChatSource source) => TimeControlCommandUtil.CanUse(Server, source);
}

public abstract class TimeControlCommand<TArgs> : ChatCommand<TArgs>
{
    public override bool CanUse(IChatSource source) => TimeControlCommandUtil.CanUse(Server, source);
}

internal static class TimeControlCommandUtil
{
    public static bool CanUse(MultiplayerServer server, IChatSource source)
    {
        return server.settings.timeControl != TimeControl.HostOnly ||
               source is not ServerPlayer { IsHost: false } ||
               !server.PlayingPlayers.Any(player => player.IsHost);
    }

    public static void SetSpeed(MultiplayerServer server, IChatSource source, TimeVote speed)
    {
        var sourcePlayer = source as ServerPlayer;
        var factionId = sourcePlayer?.FactionId ?? ScheduledCommand.NoFaction;

        if (server.settings.timeControl == TimeControl.LowestWins)
        {
            server.commands.Send(
                CommandType.TimeSpeedVote,
                factionId,
                ScheduledCommand.Global,
                ByteWriter.GetBytes(speed, ScheduledCommand.Global),
                sourcePlayer
            );
            return;
        }

        server.commands.Send(
            CommandType.GlobalTimeSpeed,
            factionId,
            ScheduledCommand.Global,
            [(byte)speed],
            sourcePlayer
        );
    }
}
