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
    PlayerCount,

    // Global scope, server only: viewer counts saved with the game belong to players of an earlier session
    ResetPlayerCounts,
}
