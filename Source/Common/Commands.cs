namespace Multiplayer.Common
{
    public enum CommandType : byte
    {
        // Global scope
        WorldTimeSpeed,
        PauseAll,
        CreateJoinPoint,
        SetupFaction,
        GlobalIdBlock,
        FactionOnline,
        FactionOffline,
        InitPlayerData,

        // Mixed scope
        Sync,
        DebugTools,

        // Map scope
        MapTimeSpeed,
        CreateMapFactionData,
        MapIdBlock,
        Designator,
    }
}
