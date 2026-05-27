using System.Linq;

namespace Multiplayer.Common.ChatCommands;

[ChatCommand("players", "list", Description = "List connected players.", Usage = "players")]
public class PlayersCommand : ChatCommand
{
    public override void Execute(ChatCommandContext context)
    {
        var players = Server.playerManager.Players.OrderBy(player => player.id).ToList();
        if (players.Count == 0)
        {
            context.Source.SendMsg("No players connected.");
            return;
        }

        context.Source.SendMsg($"Players ({players.Count}):");
        foreach (var player in players)
        {
            context.Source.SendMsg(
                $"- #{player.id} {player.Username} [{player.status}] {PlayerCommandUtility.GetRole(player)} faction={player.FactionId} map={player.currentMapId} ping={player.Latency}ms behind={player.ExtrapolatedTicksBehind}"
            );
        }
    }
}
