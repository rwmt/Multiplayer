using Multiplayer.Common;

namespace Server;

/// <summary>
/// Helpers for running the server in bootstrap mode (no save loaded yet).
/// </summary>
public static class BootstrapMode
{
    /// <summary>
    /// Keeps the process alive while the server is waiting for a client to provide the initial world data.
    ///
    /// This is intentionally minimal for now: it just sleeps and checks the stop flag.
    /// The networking + actual upload handling happens in the server thread/state machine.
    /// </summary>
    public static void WaitForClient(MultiplayerServer server, CancellationToken token)
    {
        ServerLog.Log("Bootstrap: waiting for first client connection...");

        // Keep the process alive. The server's net loop runs on its own thread.
        while (server.running && !token.IsCancellationRequested)
        {
            Thread.Sleep(250);
        }
    }
}
