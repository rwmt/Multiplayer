using System.Linq;

namespace Multiplayer.Common.ChatCommands;

[ChatCommand("status", Description = "Show multiplayer server status.", Usage = "status")]
public class StatusCommand : ChatCommand
{
    public override void Execute(ChatCommandContext context)
    {
        var worldState = Server.worldData.savedGame != null ? "loaded" : "not loaded";
        var joinPointState = Server.worldData.CreatingJoinPoint
            ? "creating"
            : Server.worldData.lastJoinPointAtTick >= 0
                ? $"last at tick {Server.worldData.lastJoinPointAtTick}"
                : "never";

        context.Source.SendMsg($"Server: {(Server.running ? "running" : "stopped")}");
        context.Source.SendMsg($"World: {worldState}, maps={Server.worldData.mapData.Count}, join point={joinPointState}");
        context.Source.SendMsg($"Ticks: game={Server.gameTimer}, net={Server.NetTimer}, work={Server.workTicks}");
        context.Source.SendMsg(
            $"Players: connected={Server.playerManager.Players.Count}, joined={Server.JoinedPlayers.Count()}, playing={Server.PlayingPlayers.Count()}"
        );
    }
}
