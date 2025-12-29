using System.IO.Compression;
using System.Net;
using Multiplayer.Common;
using Multiplayer.Common.Util;
using Server;

ServerLog.detailEnabled = true;

const string settingsFile = "settings.toml";
const string stopCmd = "stop";
const string saveFile = "save.zip";

var settings = new ServerSettings
{
    direct = true,
    lan = false
};

if (File.Exists(settingsFile))
    settings = TomlSettings.Load(settingsFile);
else
    ServerLog.Log($"Bootstrap mode: '{settingsFile}' not found. Waiting for a client to upload it.");

if (settings.steam) ServerLog.Error("Steam is not supported in standalone server.");
if (settings.arbiter) ServerLog.Error("Arbiter is not supported in standalone server.");

var server = MultiplayerServer.instance = new MultiplayerServer(settings)
{
    running = true,
};

var bootstrap = !File.Exists(settingsFile);

var consoleSource = new ConsoleSource();

if (!bootstrap && File.Exists(saveFile))
{
    LoadSave(server, saveFile);
}
else
{
    bootstrap = true;
    ServerLog.Log($"Bootstrap mode: '{saveFile}' not found. Server will start without a loaded save.");
    ServerLog.Log("Waiting for a client to upload world data.");
}
server.BootstrapMode = bootstrap;

if (bootstrap)
    ServerLog.Detail("Bootstrap flag is enabled.");

if (settings.direct) {
    var badEndpoint = settings.TryParseEndpoints(out var endpoints);
    if (badEndpoint != null)
    {
        ServerLog.Error($"Failed to parse endpoint: {badEndpoint}");
        return;
    }

    if (!LiteNetManager.Create(server, endpoints, out var liteNet))
    {
        ServerLog.Error("Failed to start net manager");
        return;
    }
    server.netManagers.Add(liteNet);
}

if (settings.lan)
{
    if (!IPAddress.TryParse(settings.lanAddress, out var ipAddr))
    {
        ServerLog.Error($"Failed to parse lan address: {settings.lanAddress}");
        return;
    }

    var lan = LiteNetLanManager.Create(server, ipAddr);
    if (lan == null)
    {
        ServerLog.Error("Failed to start lan manager");
        return;
    }
    server.netManagers.Add(lan);
}

new Thread(server.Run) { Name = "Server thread" }.Start();

// In bootstrap mode we keep the server alive and wait for any client to connect.
// The actual world data upload is handled by the normal networking code paths.
if (bootstrap)
    BootstrapMode.WaitForClient(server, CancellationToken.None);

// If bootstrap mode completed (a client uploaded save.zip) the server thread will have set
// server.running = false. In that case, exit so the user can restart the server normally.
if (bootstrap && !server.running)
    return;

while (true)
{
    var cmd = Console.ReadLine();
    if (cmd != null)
        server.Enqueue(() => server.HandleChatCmd(consoleSource, cmd));

    if (cmd == stopCmd)
        break;
}

static void LoadSave(MultiplayerServer server, string path)
{
    using var zip = ZipFile.OpenRead(path);

    ServerLog.Detail($"Bootstrap: loading save from {path}. Zip contains {zip.Entries.Count} entries:");
    foreach (var entry in zip.Entries)
        ServerLog.Detail($"  - {entry.FullName} ({entry.Length} bytes)");

    var replayInfo = ReplayInfo.Read(zip.GetBytes("info"));
    ServerLog.Detail($"Loading {path} saved in RW {replayInfo.rwVersion} with {replayInfo.modNames.Count} mods");

    server.settings.gameName = replayInfo.name;
    server.worldData.hostFactionId = replayInfo.playerFaction;
    var spectatorFaction = replayInfo.spectatorFaction;
    if (server.settings.multifaction && spectatorFaction == 0)
        ServerLog.Error("Multifaction is enabled but the save doesn't contain spectator faction id.");
    server.worldData.spectatorFactionId = spectatorFaction;

    //This parses multiple saves as long as they are named correctly
    server.gameTimer = replayInfo.sections[0].start;
    server.startingTimer = replayInfo.sections[0].start;


    var worldSaveData = zip.GetBytes("world/000_save");
    server.worldData.savedGame = Compress(worldSaveData);
    ServerLog.Detail($"Bootstrap: loaded world/000_save ({worldSaveData.Length} bytes), compressed to {server.worldData.savedGame.Length} bytes");

    // Parse cmds entry for each map
    int mapCmdsCount = 0;
    foreach (var entry in zip.GetEntries("maps/*_cmds"))
    {
        var parts = entry.FullName.Split('_');

        if (parts.Length == 3)
        {
            int mapNumber = int.Parse(parts[1]);
            var cmds = ScheduledCommand.DeserializeCmds(zip.GetBytes(entry.FullName)).Select(ScheduledCommand.Serialize).ToList();
            server.worldData.mapCmds[mapNumber] = cmds;
            ServerLog.Detail($"Bootstrap: loaded {entry.FullName} ({entry.Length} bytes) -> {cmds.Count} commands for map {mapNumber}");
            mapCmdsCount++;
        }
    }

    // Parse save entry for each map
    int mapDataCount = 0;
    foreach (var entry in zip.GetEntries("maps/*_save"))
    {
        var parts = entry.FullName.Split('_');

        if (parts.Length == 3)
        {
            int mapNumber = int.Parse(parts[1]);
            var mapSaveData = zip.GetBytes(entry.FullName);
            server.worldData.mapData[mapNumber] = Compress(mapSaveData);
            ServerLog.Detail($"Bootstrap: loaded {entry.FullName} ({mapSaveData.Length} bytes), compressed to {server.worldData.mapData[mapNumber].Length} bytes");
            mapDataCount++;
        }
    }

    var worldCmds = zip.GetBytes("world/000_cmds");
    server.worldData.mapCmds[-1] = ScheduledCommand.DeserializeCmds(worldCmds).Select(ScheduledCommand.Serialize).ToList();
    ServerLog.Detail($"Bootstrap: loaded world/000_cmds ({worldCmds.Length} bytes) -> {server.worldData.mapCmds[-1].Count} world commands");

    server.worldData.sessionData = Array.Empty<byte>();
    ServerLog.Detail($"Bootstrap: loaded {mapDataCount} maps with {mapCmdsCount} map command entries. SessionData is empty (vanilla save)");
}

static byte[] Compress(byte[] input)
{
    using var result = new MemoryStream();

    using (var compressionStream = new GZipStream(result, CompressionMode.Compress))
    {
        compressionStream.Write(input, 0, input.Length);
        compressionStream.Flush();

    }
    return result.ToArray();
}

class ConsoleSource : IChatSource
{
    public void SendMsg(string msg)
    {
        ServerLog.Log(msg);
    }
}
