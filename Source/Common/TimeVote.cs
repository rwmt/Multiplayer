namespace Multiplayer.Common;

public enum TimeVote : byte
{
    Paused,
    Normal,
    Fast,
    Superfast,
    Ultrafast,

    PlayerResetTickable,
    PlayerResetGlobal,
    ResetTickable,
    ResetGlobal
}
