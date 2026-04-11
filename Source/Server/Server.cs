using System.Net;
using Multiplayer.Common;
using Multiplayer.Common.Util;
using Server;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);

const string settingsFile = "settings.toml";
const string stopCmd = "stop";
const string saveFile = "save.zip";

var settings = new ServerSettings
{
    direct = true,
    lan = false
};

var bootstrap = !File.Exists(settingsFile);

if (!bootstrap)
    settings = TomlSettings.Load(settingsFile);
else
    ServerLog.Log($"Bootstrap mode: '{settingsFile}' not found. Waiting for a client to upload it.");

settings.EnforceStandaloneRequirements(isStandaloneServer: true);

if (settings.steam) ServerLog.Error("Steam is not supported in standalone server.");
if (settings.arbiter) ServerLog.Error("Arbiter is not supported in standalone server.");

var server = MultiplayerServer.instance = new MultiplayerServer(settings)
{
    running = true,
    IsStandaloneServer = true,
};

var persistence = new StandalonePersistence(AppContext.BaseDirectory);
server.persistence = persistence;

// Cleanup leftover temp files from any previous interrupted writes
persistence.CleanupTempFiles();

var consoleSource = new ConsoleSource();

if (!bootstrap && persistence.HasValidState())
{
    // Prefer loading from the Saved/ directory (structured persistence)
    var info = persistence.LoadInto(server);
    if (info != null)
    {
        server.settings.gameName = info.name;
        server.worldData.hostFactionId = info.playerFaction;
        var spectatorFaction = info.spectatorFaction;
        if (server.settings.multifaction && spectatorFaction == 0)
            ServerLog.Error("Multifaction is enabled but the save doesn't contain spectator faction id.");
        server.worldData.spectatorFactionId = spectatorFaction;
    }
    ServerLog.Log("Loaded state from Saved/ directory.");
}
else if (!bootstrap && File.Exists(saveFile))
{
    // Seed the Saved/ directory from save.zip, then load from it
    ServerLog.Log($"Seeding Saved/ directory from {saveFile}...");
    persistence.SeedFromSaveZip(saveFile);
    var info = persistence.LoadInto(server);
    if (info != null)
    {
        server.settings.gameName = info.name;
        server.worldData.hostFactionId = info.playerFaction;
        var spectatorFaction = info.spectatorFaction;
        if (server.settings.multifaction && spectatorFaction == 0)
            ServerLog.Error("Multifaction is enabled but the save doesn't contain spectator faction id.");
        server.worldData.spectatorFactionId = spectatorFaction;
    }
}
else
{
    bootstrap = true;
    ServerLog.Log($"Bootstrap mode: neither Saved/ directory nor '{saveFile}' found.");
    ServerLog.Log("Waiting for a client to upload world data.");
}

server.BootstrapMode = bootstrap;

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

if (bootstrap)
    BootstrapMode.WaitForClient(server, CancellationToken.None);

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

class ConsoleSource : IChatSource
{
    public void SendMsg(string msg)
    {
        ServerLog.Log(msg);
    }
}
