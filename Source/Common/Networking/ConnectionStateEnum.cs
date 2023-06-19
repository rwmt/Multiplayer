namespace Multiplayer.Common;

public enum ConnectionStateEnum : byte
{
    ClientJoining,
    ClientLoading,
    ClientPlaying,
    ClientSteam,

    ServerJoining,
    ServerLoading,
    ServerPlaying,
    ServerSteam, // unused

    Count,
    Disconnected
}
