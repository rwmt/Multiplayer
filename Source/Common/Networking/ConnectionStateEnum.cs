namespace Multiplayer.Common;

public enum ConnectionStateEnum : byte
{
    ClientJoining,
    ClientLoading,
    ClientBootstrap,
    ClientPlaying,
    ClientSteam,

    ServerJoining,
    ServerBootstrap,
    ServerLoading,
    ServerPlaying,
    ServerSteam, // unused

    Count,
    Disconnected
}

public static class ConnectionStateEnumExt
{
    public static bool IsClient(this ConnectionStateEnum state) =>
        state is >= ConnectionStateEnum.ClientJoining and <= ConnectionStateEnum.ClientSteam;

    public static bool IsServer(this ConnectionStateEnum state) =>
        state is >= ConnectionStateEnum.ServerJoining and <= ConnectionStateEnum.ServerSteam;
}
