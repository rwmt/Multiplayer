using System.Threading;
using Multiplayer.Common;

namespace Server;

public static class BootstrapMode
{
    public static void WaitForClient(MultiplayerServer server, CancellationToken token)
    {
        ServerLog.Log("Bootstrap: waiting for first client connection...");

        while (server.running && !token.IsCancellationRequested)
            Thread.Sleep(250);
    }
}