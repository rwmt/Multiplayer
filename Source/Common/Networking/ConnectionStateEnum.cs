namespace Multiplayer.Common;

public enum ConnectionStateEnum : byte
{
    ClientJoining,
    ClientLoading,
    ClientPlaying,
    ClientSteam,
    ClientBootstrap,

    ServerJoining,
    ServerLoading,
    ServerPlaying,
    ServerSteam, // unused
    ServerBootstrap,

    Count,
    Disconnected
}

public static class ConnectionStateEnumExt
{
    public static bool IsClient(this ConnectionStateEnum state) =>
        state is >= ConnectionStateEnum.ClientJoining and <= ConnectionStateEnum.ClientBootstrap;

    public static bool IsServer(this ConnectionStateEnum state) =>
        state is >= ConnectionStateEnum.ServerJoining and <= ConnectionStateEnum.ServerBootstrap;
}
