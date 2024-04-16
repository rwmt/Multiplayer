using System.IO.Compression;
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
    TomlSettings.Save(settings, settingsFile); // Save default settings

var server = MultiplayerServer.instance = new MultiplayerServer(settings)
{
    running = true,
    initDataState = InitDataState.Waiting
};

var consoleSource = new ConsoleSource();

LoadSave(server, saveFile);
server.liteNet.StartNet();

new Thread(server.Run) { Name = "Server thread" }.Start();

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

    var replayInfo = ReplayInfo.Read(zip.GetBytes("info"));
    ServerLog.Detail($"Loading {path} saved in RW {replayInfo.rwVersion} with {replayInfo.modNames.Count} mods");

    server.settings.gameName = replayInfo.name;
    server.worldData.hostFactionId = replayInfo.playerFaction;

    //This parses multiple saves as long as they are named correctly
    server.gameTimer = replayInfo.sections[0].start;
    server.startingTimer = replayInfo.sections[0].start;


    server.worldData.savedGame = Compress(zip.GetBytes("world/000_save"));

    // Parse cmds entry for each map
    foreach (var entry in zip.GetEntries("maps/*_cmds"))
    {
        var parts = entry.FullName.Split('_');

        if (parts.Length == 3)
        {
            int mapNumber = int.Parse(parts[1]);
            server.worldData.mapCmds[mapNumber] = ScheduledCommand.DeserializeCmds(zip.GetBytes(entry.FullName)).Select(ScheduledCommand.Serialize).ToList();
        }
    }

    // Parse save entry for each map
    foreach (var entry in zip.GetEntries("maps/*_save"))
    {
        var parts = entry.FullName.Split('_');

        if (parts.Length == 3)
        {
            int mapNumber = int.Parse(parts[1]);
            server.worldData.mapData[mapNumber] = Compress(zip.GetBytes(entry.FullName));
        }
    }
    

    server.worldData.mapCmds[-1] = ScheduledCommand.DeserializeCmds(zip.GetBytes("world/000_cmds")).Select(ScheduledCommand.Serialize).ToList();
    server.worldData.sessionData = Array.Empty<byte>();
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
