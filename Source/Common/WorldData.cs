using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Multiplayer.Common;

public class WorldData
{
    public int hostFactionId;
    public int spectatorFactionId;
    public byte[]? savedGame; // Compressed game save
    public byte[]? sessionData; // Compressed semi persistent data
    public Dictionary<int, byte[]> mapData = new(); // Map id to compressed map data

    public Dictionary<int, List<byte[]>> mapCmds = new(); // Map id to serialized cmds list
    public Dictionary<int, List<byte[]>>? tmpMapCmds;
    public int lastJoinPointAtWorkTicks = -1;

    public List<byte[]> syncInfos = new();

    private TaskCompletionSource<WorldData>? dataSource;

    public bool CreatingJoinPoint => tmpMapCmds != null;

    public MultiplayerServer Server { get; }

    public WorldData(MultiplayerServer server)
    {
        Server = server;
    }

    public bool TryStartJoinPointCreation(bool force = false)
    {
        if (!force && Server.workTicks - lastJoinPointAtWorkTicks < 30)
            return false;

        if (CreatingJoinPoint)
            return false;

        Server.SendChat("Creating a join point...");

        Server.commands.Send(CommandType.CreateJoinPoint, ScheduledCommand.NoFaction, ScheduledCommand.Global, Array.Empty<byte>());
        tmpMapCmds = new Dictionary<int, List<byte[]>>();
        dataSource = new TaskCompletionSource<WorldData>();

        return true;
    }

    public void EndJoinPointCreation()
    {
        mapCmds = tmpMapCmds!;
        tmpMapCmds = null;
        lastJoinPointAtWorkTicks = Server.workTicks;
        dataSource!.SetResult(this);
    }

    public Task<WorldData> WaitJoinPoint()
    {
        return dataSource?.Task ?? Task.FromResult(this);
    }
}
