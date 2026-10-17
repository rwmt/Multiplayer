using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Multiplayer.Common;

/// <summary>
/// Manages the Saved/ directory for standalone server durable persistence.
/// Uses atomic temp-file + rename pattern for crash safety.
/// </summary>
public class StandalonePersistence
{
    public string SavedDir { get; }

    private string MapsDir => Path.Combine(SavedDir, "maps");
    private string WorldPath => Path.Combine(SavedDir, "world.dat");
    // All cmds since the join point in send order. Replaces the legacy per-tickable world_cmds.dat + maps/N_cmds.dat.
    private string CmdsPath => Path.Combine(SavedDir, "cmds.dat");
    private string LegacyWorldCmdsPath => Path.Combine(SavedDir, "world_cmds.dat");
    private string SessionPath => Path.Combine(SavedDir, "session.dat");
    private string InfoPath => Path.Combine(SavedDir, "info.xml");
    private string StatePath => Path.Combine(SavedDir, "state.bin");

    public StandalonePersistence(string baseDir)
    {
        SavedDir = Path.Combine(baseDir, "Saved");
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(SavedDir);
        Directory.CreateDirectory(MapsDir);
    }

    public bool HasValidState()
    {
        return File.Exists(WorldPath);
    }

    /// <summary>
    /// Seed the Saved/ directory from a save.zip (replay format).
    /// </summary>
    public void SeedFromSaveZip(string zipPath)
    {
        EnsureDirectories();

        using var zip = ZipFile.OpenRead(zipPath);

        // World save
        var worldEntry = zip.GetEntry("world/000_save");
        if (worldEntry != null)
        {
            var worldBytes = ReadEntry(worldEntry);
            AtomicWrite(WorldPath, Compress(worldBytes));
        }

        // World commands (all commands for replays written by current versions)
        var worldCmdsEntry = zip.GetEntry("world/000_cmds");
        var worldCmds = worldCmdsEntry != null
            ? ScheduledCommand.DeserializeCmds(ReadEntry(worldCmdsEntry))
            : new List<ScheduledCommand>();
        var mapCmds = new List<List<ScheduledCommand>>();

        // Map saves and commands of the first section
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith("maps/")) continue;

            var parts = entry.FullName.Replace("maps/", "").Split('_');
            if (parts.Length < 3 || parts[0] != "000") continue;

            if (!int.TryParse(parts[1], out int mapId)) continue;

            if (entry.FullName.EndsWith("_save"))
                AtomicWrite(Path.Combine(MapsDir, $"{mapId}.dat"), Compress(ReadEntry(entry)));
            else if (entry.FullName.EndsWith("_cmds"))
                mapCmds.Add(ScheduledCommand.DeserializeCmds(ReadEntry(entry)));
        }

        AtomicWrite(CmdsPath, ScheduledCommand.SerializeCmds(ScheduledCommand.MergeLegacyCmds(worldCmds, mapCmds)));
        DeleteLegacyCmdFiles();

        // Info/metadata
        var infoEntry = zip.GetEntry("info");
        if (infoEntry != null)
        {
            var infoBytes = ReadEntry(infoEntry);
            AtomicWrite(InfoPath, infoBytes);

            try
            {
                WritePersistedTick(GetLatestTick(ReplayInfo.Read(infoBytes)));
            }
            catch (Exception e)
            {
                ServerLog.Error($"Failed to seed persisted tick from info.xml: {e.Message}");
            }
        }

        // Session data (empty for replay format, but create the file)
        AtomicWrite(SessionPath, Array.Empty<byte>());

        ServerLog.Log($"Seeded Saved/ directory from {zipPath}");
    }

    /// <summary>
    /// Load persisted state into WorldData and return ReplayInfo if available.
    /// </summary>
    public ReplayInfo? LoadInto(MultiplayerServer server)
    {
        if (!File.Exists(WorldPath))
            return null;

        server.worldData.savedGame = File.ReadAllBytes(WorldPath);

        server.worldData.sessionData = File.Exists(SessionPath) ? File.ReadAllBytes(SessionPath) : Array.Empty<byte>();

        // Load maps
        var legacyMapCmds = new List<List<ScheduledCommand>>();
        if (Directory.Exists(MapsDir))
        {
            foreach (var mapFile in Directory.GetFiles(MapsDir, "*.dat"))
            {
                var fileName = Path.GetFileNameWithoutExtension(mapFile);
                if (fileName.EndsWith("_cmds")) continue;

                if (int.TryParse(fileName, out int mapId))
                {
                    server.worldData.mapData[mapId] = File.ReadAllBytes(mapFile);

                    var cmdsPath = Path.Combine(MapsDir, $"{mapId}_cmds.dat");
                    if (File.Exists(cmdsPath))
                        legacyMapCmds.Add(ScheduledCommand.DeserializeCmds(File.ReadAllBytes(cmdsPath)));
                }
            }
        }

        // Load commands
        List<ScheduledCommand> cmds;
        if (File.Exists(CmdsPath))
        {
            cmds = ScheduledCommand.DeserializeCmds(File.ReadAllBytes(CmdsPath));
        }
        else
        {
            var legacyWorldCmds = File.Exists(LegacyWorldCmdsPath)
                ? ScheduledCommand.DeserializeCmds(File.ReadAllBytes(LegacyWorldCmdsPath))
                : new List<ScheduledCommand>();
            cmds = ScheduledCommand.MergeLegacyCmds(legacyWorldCmds, legacyMapCmds);
        }

        server.worldData.cmds = cmds.Select(ScheduledCommand.Serialize).ToList();

        // Load replay info for metadata
        ReplayInfo? info = null;
        if (File.Exists(InfoPath))
        {
            try { info = ReplayInfo.Read(File.ReadAllBytes(InfoPath)); }
            catch (Exception e) { ServerLog.Error($"Failed to read info.xml: {e.Message}"); }
        }

        var loadedTick = ReadPersistedTick() ?? GetLatestTick(info);
        server.gameTimer = loadedTick;
        server.startingTimer = loadedTick;
        server.worldData.standaloneWorldSnapshot.tick = loadedTick;

        foreach (var mapId in server.worldData.mapData.Keys)
        {
            server.worldData.standaloneMapSnapshots[mapId] = new StandaloneMapSnapshotState
            {
                tick = loadedTick,
            };
        }

        ServerLog.Log($"Loaded state from Saved/ directory ({server.worldData.mapData.Count} maps) at tick {loadedTick}");
        return info;
    }

    public void WriteJoinPoint(WorldData worldData, int tick)
    {
        EnsureDirectories();

        if (worldData.savedGame != null)
            AtomicWrite(WorldPath, worldData.savedGame);

        AtomicWrite(SessionPath, worldData.sessionData ?? Array.Empty<byte>());

        var currentMapIds = worldData.mapData.Keys.ToHashSet();
        DeleteStaleMapFiles(currentMapIds);

        foreach (var (mapId, mapData) in worldData.mapData)
            AtomicWrite(Path.Combine(MapsDir, $"{mapId}.dat"), mapData);

        // cmds.dat takes precedence over the legacy files, so they are only deleted once it exists
        AtomicWrite(CmdsPath, SerializeStoredCmds(worldData.cmds));
        DeleteLegacyCmdFiles();

        WritePersistedTick(tick);
    }

    /// <summary>
    /// Write an accepted map snapshot to disk atomically.
    /// </summary>
    public void WriteMapSnapshot(int mapId, byte[] compressedMapData)
    {
        EnsureDirectories();
        AtomicWrite(Path.Combine(MapsDir, $"{mapId}.dat"), compressedMapData);
    }

    /// <summary>
    /// Write an accepted world snapshot to disk atomically.
    /// </summary>
    public void WriteWorldSnapshot(byte[] compressedWorldData, byte[] sessionData, int tick)
    {
        EnsureDirectories();
        AtomicWrite(WorldPath, compressedWorldData);
        AtomicWrite(SessionPath, sessionData);
        WritePersistedTick(tick);
    }

    /// <summary>
    /// Cleanup any leftover .tmp files from interrupted writes.
    /// </summary>
    public void CleanupTempFiles()
    {
        if (!Directory.Exists(SavedDir)) return;

        foreach (var tmp in Directory.GetFiles(SavedDir, "*.tmp", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(tmp);
                ServerLog.Detail($"Cleaned up leftover temp file: {tmp}");
            }
            catch (Exception e)
            {
                ServerLog.Error($"Failed to clean temp file {tmp}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Atomic write: write to .tmp, then replace/rename over the target.
    /// </summary>
    private static void AtomicWrite(string targetPath, byte[] data)
    {
        var tmpPath = targetPath + ".tmp";
        File.WriteAllBytes(tmpPath, data);

        if (File.Exists(targetPath))
            File.Replace(tmpPath, targetPath, destinationBackupFileName: null);
        else
            File.Move(tmpPath, targetPath);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[] Compress(byte[] input)
    {
        using var result = new MemoryStream();
        using (var gz = new GZipStream(result, CompressionMode.Compress))
        {
            gz.Write(input, 0, input.Length);
            gz.Flush();
        }
        return result.ToArray();
    }

    private void DeleteStaleMapFiles(HashSet<int> currentMapIds)
    {
        if (!Directory.Exists(MapsDir))
            return;

        foreach (var path in Directory.GetFiles(MapsDir, "*.dat"))
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            var mapIdText = fileName.EndsWith("_cmds") ? fileName[..^5] : fileName;
            if (int.TryParse(mapIdText, out var mapId) && !currentMapIds.Contains(mapId))
                File.Delete(path);
        }
    }

    private void DeleteLegacyCmdFiles()
    {
        if (File.Exists(LegacyWorldCmdsPath))
            File.Delete(LegacyWorldCmdsPath);

        if (!Directory.Exists(MapsDir))
            return;

        foreach (var path in Directory.GetFiles(MapsDir, "*_cmds.dat"))
            File.Delete(path);
    }

    private void WritePersistedTick(int tick)
    {
        AtomicWrite(StatePath, BitConverter.GetBytes(tick));
    }

    private int? ReadPersistedTick()
    {
        if (!File.Exists(StatePath))
            return null;

        var data = File.ReadAllBytes(StatePath);
        return data.Length >= sizeof(int) ? BitConverter.ToInt32(data, 0) : null;
    }

    private static int GetLatestTick(ReplayInfo? info)
    {
        if (info?.sections.Count > 0)
        {
            var section = info.sections.Last();
            return Math.Max(section.start, section.end);
        }

        return 0;
    }

    private static byte[] SerializeStoredCmds(List<byte[]> cmds)
    {
        var writer = new ByteWriter();
        writer.WriteInt32(cmds.Count);
        foreach (var cmd in cmds)
            writer.WritePrefixedBytes(cmd);
        return writer.ToArray();
    }
}
