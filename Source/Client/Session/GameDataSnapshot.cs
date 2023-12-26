using System.Collections.Generic;
using Multiplayer.Common;

namespace Multiplayer.Client;

public record GameDataSnapshot(
    int CachedAtTime,
    byte[] GameData,
    byte[] SessionData,
    Dictionary<int, byte[]> MapData,
    Dictionary<int, List<ScheduledCommand>> MapCmds // Global cmds are -1, this is mutated by MultiplayerSession.ScheduleCommand
);
