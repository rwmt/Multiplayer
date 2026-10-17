using System.Collections.Generic;
using Multiplayer.Common;

namespace Multiplayer.Client;

public record GameDataSnapshot(
    int CachedAtTime,
    byte[] GameData,
    byte[] SessionData,
    Dictionary<int, byte[]> MapData,
    // All cmds (world and map) since CachedAtTime in execution order, appended to by MultiplayerSession.ScheduleCommand
    List<ScheduledCommand> Cmds
);
