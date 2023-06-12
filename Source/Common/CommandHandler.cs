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
            if (server.freezeManager.Frozen)
                return;

            if (sourcePlayer != null)
            {
                bool debugCmd =
                    cmd == CommandType.DebugTools ||
                    cmd == CommandType.Sync && server.initData!.DebugOnlySyncCmds.Contains(BitConverter.ToInt32(data, 0));
                if (debugCmd && !CanUseDevMode(sourcePlayer))
                    return;

                bool hostOnly = cmd == CommandType.Sync && server.initData!.HostOnlySyncCmds.Contains(BitConverter.ToInt32(data, 0));
                if (hostOnly && !sourcePlayer.IsHost)
                    return;

                if (cmd is CommandType.MapTimeSpeed or CommandType.GlobalTimeSpeed &&
                    server.settings.timeControl == TimeControl.HostOnly && !sourcePlayer.IsHost)
                    return;
            }

            byte[] toSave = ScheduledCommand.Serialize(
                new ScheduledCommand(
                    cmd,
                    server.gameTimer + 1,
                    factionId,
                    mapId,
                    sourcePlayer?.id ?? fauxSource?.id ?? ScheduledCommand.NoPlayer,
                    data));

            // todo cull target players if not global
            server.worldData.mapCmds.GetOrAddNew(mapId).Add(toSave);
            server.worldData.tmpMapCmds?.GetOrAddNew(mapId).Add(toSave);

            byte[] toSend = toSave.Append(new byte[] { 0 });
            byte[] toSendSource = toSave.Append(new byte[] { 1 });

            foreach (var player in server.PlayingPlayers)
            {
                player.conn.Send(
                    Packets.Server_Command,
                    sourcePlayer == player ? toSendSource : toSend
                );
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
