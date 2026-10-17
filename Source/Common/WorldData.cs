using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common;

public class WorldData
{
    public int hostFactionId;
    public int spectatorFactionId;
    public byte[]? savedGame; // Compressed game save
    public byte[]? sessionData; // Compressed semi persistent data
    public Dictionary<int, byte[]> mapData = new(); // Map id to compressed map data

    // Serialized cmds since the last join point, in the order they were sent to clients. Live clients execute
    // commands in arrival order, so joiners must get exactly this order (not grouped by map).
    public List<byte[]> cmds = new();
    public List<byte[]>? tmpCmds;
    public int lastJoinPointAtTick = -1;

    // The sync reference's newest opinions (oldest first). Joiners only use them to check their catch-up
    // simulation, after that they get the live ones, so losing the oldest ones only skips a few checks.
    public List<byte[]> syncInfos = new();
    public const int MaxSyncInfos = 10;
    // With desync traces on, one opinion can be a few MiB on big saves and they all go into the world data packet
    public const int MaxSyncInfosLength = ClientSyncInfoPacket.MaxLength;

    public StandaloneWorldSnapshotState standaloneWorldSnapshot = new();
    public Dictionary<int, StandaloneMapSnapshotState> standaloneMapSnapshots = new();

    private TaskCompletionSource<WorldData>? dataSource;

    // The join point being created: the tick of its CreateJoinPoint command and the only player whose upload
    // completes it
    public int JoinPointTick { get; private set; } = -1;
    public int JoinPointUploaderId { get; private set; } = ScheduledCommand.NoPlayer;
    private int joinPointProgressAt; // NetTimer
    private int? lastForcedJoinPointAt; // NetTimer
    private int standaloneAutosaveBaseTick = -1;

    // Every client reloads at a join point and the game pauses meanwhile, so forced requests (saves, desyncs) are
    // rate limited. Abort if the uploader shows no progress, joiners would wait forever otherwise.
    public const int ForcedJoinPointCooldown = MultiplayerServer.NetTicksPerSecond * 10;
    public const int JoinPointTimeout = MultiplayerServer.NetTicksPerSecond * 60;
    // Keep alives are answered several times a second unless the client's main thread is blocked
    public const int UploaderBusyAfter = MultiplayerServer.NetTicksPerSecond * 5;
    private const int TicksPerMinute = 60 * 60;

    public bool CreatingJoinPoint => tmpCmds != null;

    public MultiplayerServer Server { get; }

    public WorldData(MultiplayerServer server)
    {
        Server = server;
    }

    private int CurrentJoinPointTick => Server.IsStandaloneServer ? Server.gameTimer : Server.workTicks;

    /// sourcePlayer is the requester, only used for logging: the upload always comes from the sync reference
    /// (host/arbiter, or the longest connected player on standalone), the client every other one is compared against.
    public bool TryStartJoinPointCreation(bool force = false, ServerPlayer? sourcePlayer = null)
    {
        int currentTick = CurrentJoinPointTick;

        if (!force && lastJoinPointAtTick >= 0 && currentTick - lastJoinPointAtTick < 30)
        {
            ServerLog.Detail($"Join point skipped: cooldown active at tick={currentTick}, last={lastJoinPointAtTick}, standalone={Server.IsStandaloneServer}");
            return false;
        }

        if (CreatingJoinPoint)
        {
            ServerLog.Detail("Join point skipped: already creating one");
            return false;
        }

        if (force && Server.NetTimer - lastForcedJoinPointAt < ForcedJoinPointCooldown)
        {
            ServerLog.Detail($"Join point skipped: forced join point rate limited (requested by {sourcePlayer?.Username})");
            return false;
        }

        var uploader = Server.playerManager.SyncReference;
        if (uploader == null)
        {
            ServerLog.Detail("Join point skipped: no playing player available to upload it");
            return false;
        }

        ServerLog.Detail($"Join point started at tick={Server.gameTimer}, force={force}, uploader={uploader.Username}, " +
                         $"requested by {sourcePlayer?.Username}, standalone={Server.IsStandaloneServer}");
        Server.SendChat("Creating a join point...");

        JoinPointTick = Server.gameTimer;
        JoinPointUploaderId = uploader.id;
        joinPointProgressAt = Server.NetTimer;
        if (force)
            lastForcedJoinPointAt = Server.NetTimer;

        // Every client reloads when executing it, only the uploader sends the save back
        Server.commands.Send(CommandType.CreateJoinPoint, ScheduledCommand.NoFaction, ScheduledCommand.Global,
            ByteWriter.GetBytes(uploader.id));
        tmpCmds = new List<byte[]>();
        dataSource = new TaskCompletionSource<WorldData>();

        return true;
    }

    /// Accepts the uploaded world only if it belongs to the join point being created. World, maps and cmds are
    /// replaced together so the stored save and its command log always match.
    public bool TryCompleteJoinPoint(ServerPlayer player, int tick, Dictionary<int, byte[]> uploadedMaps,
        byte[] uploadedGame, byte[] uploadedSession)
    {
        if (!CreatingJoinPoint)
        {
            ServerLog.Detail($"Rejected world upload from {player.Username}: no join point in progress");
            return false;
        }

        if (player.id != JoinPointUploaderId || tick != JoinPointTick)
        {
            ServerLog.Detail($"Rejected world upload from {player.Username} at tick={tick}: expected " +
                             $"player {JoinPointUploaderId} at tick={JoinPointTick}");
            return false;
        }

        mapData = uploadedMaps;
        savedGame = uploadedGame;
        sessionData = uploadedSession;

        if (Server.IsStandaloneServer)
            RecordStandaloneSnapshot(player, tick);

        EndJoinPointCreation();
        return true;
    }

    public void EndJoinPointCreation()
    {
        int currentTick = CurrentJoinPointTick;
        ServerLog.Detail($"Join point completed at tick={currentTick}, standalone={Server.IsStandaloneServer}");
        cmds = tmpCmds!;
        tmpCmds = null;
        lastJoinPointAtTick = currentTick;
        ClearPendingJoinPoint();

        if (Server.IsStandaloneServer && Server.persistence != null)
        {
            try
            {
                Server.persistence.WriteJoinPoint(this, currentTick);
            }
            catch (Exception e)
            {
                ServerLog.Error($"Failed to persist standalone join point at tick={currentTick}: {e}");
            }
        }

        dataSource!.SetResult(this);
        dataSource = null;
    }

    /// The previous world and the full command log (including the CreateJoinPoint command, which joiners replay
    /// as a reload like live clients did) stay valid, so waiting joiners continue with those.
    public void AbortJoinPointCreation(string reason = "")
    {
        if (!CreatingJoinPoint)
            return;

        ServerLog.Log($"Aborted join point creation at tick={JoinPointTick}. {reason}");

        tmpCmds = null;
        ClearPendingJoinPoint();
        dataSource?.SetResult(this);
        dataSource = null;
    }

    /// Called when the expected uploader disconnects, desyncs or starts rejoining
    public void OnPlayerUnavailable(ServerPlayer player, string reason)
    {
        if (CreatingJoinPoint && player.id == JoinPointUploaderId)
            AbortJoinPointCreation($"The uploader {player.Username} {reason}.");
    }

    public void OnUploadProgress(ServerPlayer player)
    {
        if (CreatingJoinPoint && player.id == JoinPointUploaderId)
            joinPointProgressAt = Server.NetTimer;
    }

    private void ClearPendingJoinPoint()
    {
        JoinPointTick = -1;
        JoinPointUploaderId = ScheduledCommand.NoPlayer;
    }

    public void AddSyncInfo(byte[] rawSyncOpinion)
    {
        syncInfos.Add(rawSyncOpinion);

        long length = syncInfos.Sum(info => (long)info.Length);
        while (syncInfos.Count > MaxSyncInfos || syncInfos.Count > 1 && length > MaxSyncInfosLength)
        {
            length -= syncInfos[0].Length;
            syncInfos.RemoveAt(0);
        }
    }

    /// The newest opinions whose length prefixed size fits into maxLength, oldest first
    public List<byte[]> NewestSyncInfosFitting(long maxLength)
    {
        int start = syncInfos.Count;
        long length = 0;
        while (start > 0 && length + sizeof(int) + syncInfos[start - 1].Length <= maxLength)
            length += sizeof(int) + syncInfos[--start].Length;

        return syncInfos.GetRange(start, syncInfos.Count - start);
    }

    public Task<WorldData> WaitJoinPoint()
    {
        return dataSource?.Task ?? Task.FromResult(this);
    }

    public void Tick()
    {
        CheckJoinPointTimeout(Server.NetTimer);
        TickStandaloneAutosave();
    }

    public void CheckJoinPointTimeout(int netTimer)
    {
        if (!CreatingJoinPoint) return;

        // Saving and reloading for the join point blocks the uploader's main thread, for minutes with big games, so
        // it doesn't answer keep alives meanwhile. That's progress too: a client that hangs for good is dropped by
        // the stale keep alive timeout (PlayerManager.CheckTimeouts), which aborts the join point.
        if (Server.GetPlayer(JoinPointUploaderId) is { } uploader && netTimer - uploader.keepAliveAt > UploaderBusyAfter)
            joinPointProgressAt = netTimer;

        if (netTimer - joinPointProgressAt > JoinPointTimeout)
            AbortJoinPointCreation("Timed out waiting for the world upload.");
    }

    /// Standalone autosaves are join points issued by the server at a tick, so every client reloads at the same
    /// moment and the persisted save matches its command log. Per-client timers would create a join point per client.
    /// In-game day intervals are only known to clients (deterministically), see ConstantTicker.TickAutosave.
    private void TickStandaloneAutosave()
    {
        var settings = Server.settings;
        if (!Server.IsStandaloneServer || settings.autosaveUnit != AutosaveUnit.Minutes || settings.autosaveInterval <= 0)
            return;

        if (standaloneAutosaveBaseTick < 0)
            standaloneAutosaveBaseTick = Server.gameTimer;

        var since = Math.Max(standaloneAutosaveBaseTick, lastJoinPointAtTick);
        if (CreatingJoinPoint || Server.gameTimer - since < settings.autosaveInterval * TicksPerMinute)
            return;

        // Wait another interval if it can't start now
        standaloneAutosaveBaseTick = Server.gameTimer;
        TryStartJoinPointCreation();
    }

    private void RecordStandaloneSnapshot(ServerPlayer player, int tick)
    {
        standaloneWorldSnapshot = new StandaloneWorldSnapshotState
        {
            tick = tick,
            producerPlayerId = player.id,
            producerUsername = player.Username,
            sha256Hash = ComputeHash(savedGame!, sessionData!)
        };

        standaloneMapSnapshots.Clear();
        foreach (var (mapId, data) in mapData)
        {
            standaloneMapSnapshots[mapId] = new StandaloneMapSnapshotState
            {
                tick = tick,
                producerPlayerId = player.id,
                producerUsername = player.Username,
                sha256Hash = ComputeHash(data)
            };
        }
    }

    private static byte[] ComputeHash(params byte[][] payloads)
    {
        using var hasher = SHA256.Create();
        foreach (var payload in payloads)
        {
            hasher.TransformBlock(payload, 0, payload.Length, null, 0);
        }

        hasher.TransformFinalBlock([], 0, 0);
        return hasher.Hash;
    }
}

public struct StandaloneWorldSnapshotState
{
    public StandaloneWorldSnapshotState() { }
    public int tick;
    public int producerPlayerId;
    public string producerUsername = "";
    public byte[] sha256Hash = Array.Empty<byte>();
}

public struct StandaloneMapSnapshotState
{
    public StandaloneMapSnapshotState() { }
    public int tick;
    public int producerPlayerId;
    public string producerUsername = "";
    public byte[] sha256Hash = Array.Empty<byte>();
}
