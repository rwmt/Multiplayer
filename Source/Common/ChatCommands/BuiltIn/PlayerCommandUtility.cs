using System;
using System.Linq;

namespace Multiplayer.Common.ChatCommands;

internal static class PlayerCommandUtility
{
    public static ServerPlayer? FindPlayer(MultiplayerServer server, string username)
    {
        return server.playerManager.Players.FirstOrDefault(player =>
            string.Equals(player.Username, username, StringComparison.OrdinalIgnoreCase)
        );
    }

    public static string GetRole(ServerPlayer player)
    {
        if (player.IsHost)
            return "host";

        return player.IsArbiter ? "arbiter" : "player";
    }
}
