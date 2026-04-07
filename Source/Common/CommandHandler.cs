using System;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common
{
    public class CommandHandler
    {
        private MultiplayerServer server;

        public int SentCmds { get; private set; }

        public CommandHandler(MultiplayerServer server)
        {
            this.server = server;
        }

        public void Send(CommandType cmdType, int factionId, int mapId, byte[] data, ServerPlayer? sourcePlayer = null, ServerPlayer? fauxSource = null)
        {
            // policy
            if (sourcePlayer != null)
            {
                bool debugCmd =
                    cmdType == CommandType.DebugTools ||
                    cmdType == CommandType.Sync && server.InitData!.DebugOnlySyncCmds.Contains(BitConverter.ToInt32(data, 0));
                if (debugCmd && !CanUseDevMode(sourcePlayer))
                    return;

                bool hostOnly = cmdType == CommandType.Sync && server.InitData!.HostOnlySyncCmds.Contains(BitConverter.ToInt32(data, 0));
                if (hostOnly && !sourcePlayer.IsHost)
                    return;

                if (cmdType is CommandType.MapTimeSpeed or CommandType.GlobalTimeSpeed &&
                    server.settings.timeControl == TimeControl.HostOnly && !sourcePlayer.IsHost)
                    return;
            }

            var cmd = new ScheduledCommand(
                cmdType,
                server.gameTimer,
                factionId,
                mapId,
                sourcePlayer?.id ?? fauxSource?.id ?? ScheduledCommand.NoPlayer,
                data);
            byte[] toSave = ScheduledCommand.Serialize(cmd);

            // todo cull target players if not global
            server.worldData.mapCmds.GetOrAddNew(mapId).Add(toSave);
            server.worldData.tmpMapCmds?.GetOrAddNew(mapId).Add(toSave);

            if (server.CanUseStandaloneMapStreaming(mapId))
            {
                var serialized = ServerCommandPacket.From(cmd).Serialize();
                foreach (var player in server.PlayingPlayers)
                {
                    if (!player.hasReportedCurrentMap || player.currentMapId < 0 || player.currentMapId == mapId)
                        player.conn.Send(serialized, true);
                }
            }
            else
            {
                server.SendToPlaying(ServerCommandPacket.From(cmd));
            }

            SentCmds++;
        }

        public void PauseAll()
        {
            if (server.settings.timeControl == TimeControl.LowestWins)
                Send(
                    CommandType.TimeSpeedVote,
                    ScheduledCommand.NoFaction,
                    ScheduledCommand.Global,
                    ByteWriter.GetBytes(TimeVote.ResetGlobal, -1)
                );
            else
                Send(
                    CommandType.PauseAll,
                    ScheduledCommand.NoFaction,
                    ScheduledCommand.Global,
                    Array.Empty<byte>()
                );
        }

        public bool CanUseDevMode(ServerPlayer player) =>
            server.settings.debugMode && server.settings.devModeScope switch
            {
                DevModeScope.Everyone => true,
                DevModeScope.HostOnly => player.IsHost
            };
    }
}
