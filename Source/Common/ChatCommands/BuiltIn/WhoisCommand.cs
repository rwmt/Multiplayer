namespace Multiplayer.Common.ChatCommands;

[ChatCommand("whois", Description = "Show details for a connected player.", Usage = "whois <username>")]
public class WhoisCommand : ChatCommand<WhoisCommandArgs>
{
    protected override void Execute(ChatCommandContext context, WhoisCommandArgs args)
    {
        ServerPlayer player = args.Player;

        context.Source.SendMsg($"Player: {player.Username} (#{player.id})");
        context.Source.SendMsg($"Status: {player.status}");
        context.Source.SendMsg($"Role: {PlayerCommandUtility.GetRole(player)}");
        context.Source.SendMsg($"Faction: {player.FactionId}");
        context.Source.SendMsg($"Map: {player.currentMapId}");
        context.Source.SendMsg($"Latency: {player.Latency}ms");
        context.Source.SendMsg($"Ticks behind: {player.ExtrapolatedTicksBehind}");

        if (player.steamId != 0 || !string.IsNullOrWhiteSpace(player.steamPersonaName))
            context.Source.SendMsg($"Steam: {player.steamPersonaName} ({player.steamId})");
    }
}

public readonly record struct WhoisCommandArgs([ChatArgument("username")] ServerPlayer Player);
