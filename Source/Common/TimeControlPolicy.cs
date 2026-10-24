using System.Linq;

namespace Multiplayer.Common;

/// <summary>
/// Who may change the time, shared by the command handler and the chat commands. The rules of
/// <see cref="TimeControl.MapOwners"/> can not be decided by the server alone, because a standalone server never
/// loads the maps, so the player reports the maps they control inside the command payload, like they report the map
/// they are looking at for PlayerCount commands (see <see cref="MapTimeClaimIndex"/>).
/// </summary>
public static class TimeControlPolicy
{
    /// <summary>The byte after the speed byte of a <see cref="CommandType.MapTimeSpeed"/> command.</summary>
    public const int MapTimeClaimIndex = 1;

    /// <summary>
    /// Time commands are host only under <see cref="TimeControl.HostOnly"/> and <see cref="TimeControl.MapOwners"/>,
    /// unless no host is in the session, so the time of a session is not stuck when the host leaves.
    /// </summary>
    public static bool IsHostExempt(MultiplayerServer server, ServerPlayer player) =>
        player.IsHost || !server.PlayingPlayers.Any(p => p.IsHost);

    /// <summary>May <paramref name="player"/> change the time everywhere, which is always host only.</summary>
    public static bool CanChangeGlobalTime(MultiplayerServer server, ServerPlayer player) =>
        server.settings.timeControl is TimeControl.HostOnly or TimeControl.MapOwners
            ? IsHostExempt(server, player)
            : true;

    /// <summary>
    /// May <paramref name="player"/> change the time of a single map. Under
    /// <see cref="TimeControl.MapOwners"/> every player with colonists on a map controls its time, everyone else can
    /// change every map, and players that were not updated with the claim keep the previous behaviour.
    /// </summary>
    public static bool CanChangeMapTime(MultiplayerServer server, ServerPlayer player, bool? claimedMap)
    {
        if (IsHostExempt(server, player)) return true;

        return server.settings.timeControl switch
        {
            TimeControl.MapOwners => claimedMap != false,
            TimeControl.HostOnly => false,
            _ => true
        };
    }

    /// <summary>The ownership claim of a <see cref="CommandType.MapTimeSpeed"/> payload, null when it was logged by a client that does not know about it.</summary>
    public static bool? ReadMapTimeClaim(byte[] data)
    {
        if (data == null || data.Length <= MapTimeClaimIndex) return null;
        return data[MapTimeClaimIndex] != 0;
    }

    /// <summary>The payload of a <see cref="CommandType.MapTimeSpeed"/> command: the speed byte and the ownership claim.</summary>
    public static byte[] MapTimeSpeedData(byte speed, bool claimedMap) => [speed, (byte)(claimedMap ? 1 : 0)];
}
