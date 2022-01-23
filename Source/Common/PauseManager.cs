using System.Linq;

namespace Multiplayer.Common
{
    public class PauseManager
    {
        private bool paused;

        public bool Paused
        {
            get => paused;
            set
            {
                paused = value;
                Server.SendToAll(Packets.Server_Pause, new object[] { paused, Server.gameTimer });
            }
        }

        public MultiplayerServer Server { get; }

        public PauseManager(MultiplayerServer server)
        {
            Server = server;
        }

        private const int MaxPauseWaitTime = 60 * 10; // 10 seconds

        public void Tick()
        {
            if (!Paused && Server.HostPlayer.paused)
                Paused = true;

            if (Paused && !Server.HostPlayer.paused && (!Server.PlayingPlayers.Any(p => p.paused) || Server.net.NetTimer - Server.HostPlayer.unpausedAt > MaxPauseWaitTime))
                Paused = false;
        }
    }
}
