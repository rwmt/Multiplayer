using System;

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

        public void Send(CommandType cmd, int factionId, int mapId, byte[] data, ServerPlayer? sourcePlayer = null, ServerPlayer? fauxSource = null)
        {
            // policy
            if (sourcePlayer != null)
            {
                bool debugCmd =
                    cmd == CommandType.DebugTools ||
                    cmd == CommandType.Sync && server.InitData!.DebugOnlySyncCmds.Contains(BitConverter.ToInt32(data, 0));
                if (debugCmd && !CanUseDevMode(sourcePlayer))
                    return;

                bool hostOnly = cmd == CommandType.Sync && server.InitData!.HostOnlySyncCmds.Contains(BitConverter.ToInt32(data, 0));
                if (hostOnly && !sourcePlayer.IsHost)
                    return;

                if (cmd is CommandType.MapTimeSpeed or CommandType.GlobalTimeSpeed &&
                    server.settings.timeControl == TimeControl.HostOnly && !sourcePlayer.IsHost)
                    return;
            }

            byte[] serialized = ScheduledCommand.Serialize(
                new ScheduledCommand(
                    cmd,
                    server.gameTimer,
                    factionId,
                    mapId,
                    sourcePlayer?.id ?? fauxSource?.id ?? ScheduledCommand.NoPlayer,
                    data));

            // todo cull target players if not global
            server.worldData.mapCmds.GetOrAddNew(mapId).Add(serialized);
            server.worldData.tmpMapCmds?.GetOrAddNew(mapId).Add(serialized);
            server.SendToPlaying(Packets.Server_Command, serialized);

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
