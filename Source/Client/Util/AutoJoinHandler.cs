using System;
using Verse;

namespace Multiplayer.Client.Util;

public static class AutoJoinHandler
{
    // Start the game with this argument passed (and a connector string afterward) to automatically join the specified
    // server during startup.
    public const string AutoJoinCommandLineArgName = "connect";
    private const string RestartConnectVariable = "MultiplayerRestartConnect";

    private static bool GetConnectionStringFromEnv(out string connString)
    {
        connString = Environment.GetEnvironmentVariable(RestartConnectVariable);
        if (connString.NullOrEmpty()) return false;
        Environment.SetEnvironmentVariable(RestartConnectVariable, "");
        return true;
    }

    private static bool GetConnectionStringFromCmdLine(out string connString) =>
        GenCommandLine.TryGetCommandLineArg(AutoJoinCommandLineArgName, out connString);

    public static void JoinIfApplicable()
    {
        // TODO consider support for SteamApps.GetLaunchCommandLine. Not sure if Steam is passing args through that
        //   for RimWorld
        var env = false;
        if (!(env = GetConnectionStringFromEnv(out var connString)) && !GetConnectionStringFromCmdLine(out connString))
            return;

        if (!ConnectorRegistry.TryParse(connString, out var connector))
        {
            // As a fallback, try to just parse it as an IP address
            if (!LiteNetConnector.TryParse(connString, out connector))
            {
                var source = env ? "env" : "command line argument";
                Log.Error($"Failed to parse connection string from {source}: {connString}");
                return;
            }
        }

        ClientUtil.DoubleLongEvent(() => ClientUtil.TryConnectWithWindow(connector, false), "MpConnecting");
    }

    public static void SetForChildProcess(IConnector connector) =>
        Environment.SetEnvironmentVariable(RestartConnectVariable, connector.GetConnectionString());
}
