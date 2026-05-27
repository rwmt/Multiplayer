namespace Multiplayer.Common.ChatCommands;

internal static class PlayerCommandUtility
{
    public static string GetRole(ServerPlayer player)
    {
        if (player.IsHost)
            return "host";

        return player.IsArbiter ? "arbiter" : "player";
    }
}
