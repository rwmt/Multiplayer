using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

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
    // The protocol of the server that wrote the command log. Sync data in commands is encoded differently across
    // protocols and the log is only replayed with the same one. Removed before and written after the log and the
    // world are written, so a log of an older version or from an interrupted write has none.
    private string CmdsProtocolPath => Path.Combine(SavedDir, "cmds_protocol.txt");
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
        DeleteCmdsProtocol();

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
                var info = ReplayInfo.Read(infoBytes);
                WritePersistedTick(GetLatestTick(info));
                // The replay's commands were written by its protocol
                WriteCmdsProtocol(info.protocol);
            }
            catch (Exception e)
            {
                ServerLog.Error($"Failed to seed persisted tick and protocol from info.xml: {e.Message}");
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
        if (Directory.Exists(MapsDir))
        {
            foreach (var mapFile in Directory.GetFiles(MapsDir, "*.dat"))
            {
                var fileName = Path.GetFileNameWithoutExtension(mapFile);
                if (fileName.EndsWith("_cmds")) continue;

                if (int.TryParse(fileName, out int mapId))
                    server.worldData.mapData[mapId] = File.ReadAllBytes(mapFile);
            }
        }

        // Load replay info for metadata
        ReplayInfo? info = null;
        if (File.Exists(InfoPath))
        {
            try { info = ReplayInfo.Read(File.ReadAllBytes(InfoPath)); }
            catch (Exception e) { ServerLog.Error($"Failed to read info.xml: {e.Message}"); }
        }

        var loadedTick = ReadPersistedTick() ?? GetLatestTick(info);
        server.worldData.cmds = LoadCmds();
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
        DeleteCmdsProtocol();

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
        WriteCmdsProtocol(MpVersion.Protocol);
    }

    /// The command log is only replayed when this protocol wrote it. Commands of another protocol can be misread:
    /// they then do something else than what was played, fail halfway or depend on client local state, and the
    /// clients desync. Without them every client continues from the same saved world, which only loses the progress
    /// since that save. Logs from before cmds.dat (world_cmds.dat, maps/N_cmds.dat) always come from older protocols.
    private List<byte[]> LoadCmds()
    {
        var protocol = ReadCmdsProtocol();
        if (protocol == MpVersion.Protocol)
            return File.Exists(CmdsPath)
                ? ScheduledCommand.DeserializeCmds(File.ReadAllBytes(CmdsPath)).Select(ScheduledCommand.Serialize).ToList()
                : new List<byte[]>();

        var cmdFiles = new[] { CmdsPath, LegacyWorldCmdsPath }
            .Concat(Directory.Exists(MapsDir) ? Directory.GetFiles(MapsDir, "*_cmds.dat") : []);
        // Every format starts with the command count
        var discarded = cmdFiles.Where(File.Exists).Select(File.ReadAllBytes)
            .Sum(data => data.Length >= sizeof(int) ? Math.Max(0L, BitConverter.ToInt32(data, 0)) : 0L);

        if (discarded > 0)
        {
            var writer = protocol == null
                ? "an older version of the server (or its write was interrupted)"
                : $"protocol {protocol}";
            ServerLog.Error($"Discarding the saved command log ({discarded} commands): it was written by {writer} " +
                            $"and this server (protocol {MpVersion.Protocol}) could misread it, replaying misread " +
                            "commands desyncs. The game continues from the last saved world, the progress since " +
                            "that save is lost.");
        }

        return new List<byte[]>();
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

    private void WriteCmdsProtocol(int protocol)
    {
        AtomicWrite(CmdsProtocolPath, Encoding.UTF8.GetBytes(protocol.ToString(CultureInfo.InvariantCulture)));
    }

    private void DeleteCmdsProtocol()
    {
        if (File.Exists(CmdsProtocolPath))
            File.Delete(CmdsProtocolPath);
    }

    private int? ReadCmdsProtocol()
    {
        if (!File.Exists(CmdsProtocolPath))
            return null;

        return int.TryParse(File.ReadAllText(CmdsProtocolPath).Trim(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var protocol) ? protocol : null;
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
