namespace Multiplayer.Common;

public enum CommandType : byte
{
    // Global scope
    GlobalTimeSpeed,
    TimeSpeedVote,
    PauseAll,
    CreateJoinPoint,
    InitPlayerData,

    // Mixed scope
    Sync,
    DebugTools,

    // Map scope
    MapTimeSpeed,
    Designator,
}
