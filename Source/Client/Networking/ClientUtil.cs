using Multiplayer.Client.Util;
using Verse;

namespace Multiplayer.Client
{
    public interface ITickableConnection
    {
        public void Tick();
    }

    public static class ClientUtil
    {
        public static void TryConnectWithWindow(IConnector connector, bool returnToServerBrowser = true)
        {
            var (conn, window) = connector.Connect();
            conn.username = Multiplayer.username;

            Multiplayer.session = new MultiplayerSession
            {
                client = conn,
                connector = connector
            };
            Multiplayer.session.ReapplyPrefs();

            window.returnToServerBrowser = returnToServerBrowser;
            Find.WindowStack.Add(window);
        }
    }
}
