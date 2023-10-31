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

    // todo this assumes only one zero-tick section and map id 0
    server.gameTimer = replayInfo.sections[0].start;
    server.startingTimer = replayInfo.sections[0].start;

    server.worldData.savedGame = Compress(zip.GetBytes("world/000_save"));
    server.worldData.mapData[0] = Compress(zip.GetBytes("maps/000_0_save"));
    server.worldData.mapCmds[0] = ScheduledCommand.DeserializeCmds(zip.GetBytes("maps/000_0_cmds")).Select(ScheduledCommand.Serialize).ToList();
    server.worldData.mapCmds[-1] = ScheduledCommand.DeserializeCmds(zip.GetBytes("world/000_cmds")).Select(ScheduledCommand.Serialize).ToList();
    server.worldData.semiPersistent = Array.Empty<byte>();
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
