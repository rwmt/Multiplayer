using System;
using System.Collections.Generic;

namespace Multiplayer.Common
{
    public class CommandHandler
    {
        private MultiplayerServer server;
        public HashSet<int> debugOnlySyncCmds = new();
        public HashSet<int> hostOnlySyncCmds = new();

        public int NextCmdId { get; private set; }

        public CommandHandler(MultiplayerServer server)
        {
            this.server = server;
        }

        public void Send(CommandType cmd, int factionId, int mapId, byte[] data, ServerPlayer sourcePlayer = null)
        {
            if (sourcePlayer != null)
            {
                bool debugCmd =
                    cmd == CommandType.DebugTools ||
                    cmd == CommandType.Sync && debugOnlySyncCmds.Contains(BitConverter.ToInt32(data, 0));

                if (!server.settings.debugMode && debugCmd)
                    return;

                bool hostOnly = cmd == CommandType.Sync && hostOnlySyncCmds.Contains(BitConverter.ToInt32(data, 0));
                if (!sourcePlayer.IsHost && hostOnly)
                    return;
            }

            byte[] toSave = new ScheduledCommand(cmd, server.gameTimer, factionId, mapId, data).Serialize();

            // todo cull target players if not global
            server.mapCmds.GetOrAddNew(mapId).Add(toSave);
            server.tmpMapCmds?.GetOrAddNew(mapId).Add(toSave);

            byte[] toSend = toSave.Append(new byte[] { 0 });
            byte[] toSendSource = toSave.Append(new byte[] { 1 });

            foreach (var player in server.PlayingPlayers)
            {
                player.conn.Send(
                    Packets.Server_Command,
                    sourcePlayer == player ? toSendSource : toSend
                );
            }

            NextCmdId++;
        }
    }
}
