using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common;

public class ServerLoadingState : AsyncConnectionState
{
    public ServerLoadingState(ConnectionBase connection) : base(connection)
    {
    }

    [TypedPacketHandler]
    public void HandleClientKeepAlive(ClientKeepAlivePacket packet)
    {
        Player.ticksBehind = packet.ticksBehind;
        Player.ticksBehindReceivedAt = Server.gameTimer;
        Player.simulating = packet.simulating;
        Player.keepAliveAt = Server.NetTimer;

        if (Player.IsHost)
            Server.workTicks = packet.workTicks;

        var idMatched = Player.keepAliveId == packet.id;
        connection.OnKeepAliveArrived(idMatched);
        if (idMatched)
            Player.keepAliveId++;
    }

    protected override async Task RunState()
    {
        await Server.worldData.WaitJoinPoint();
        await EndIfDead();

        if (!SendWorldData())
            return;

        Player.SendPlayerList();
        connection.ChangeState(ConnectionStateEnum.ServerPlaying);
    }

    public bool SendWorldData()
    {
        ByteWriter writer = new ByteWriter();

        writer.WriteInt32(Player.FactionId);
        writer.WriteInt32(Server.gameTimer);
        writer.WriteInt32(Server.commands.SentCmds);
        writer.WriteBool(Server.freezeManager.Frozen);
        writer.WritePrefixedBytes(Server.worldData.savedGame);
        writer.WritePrefixedBytes(Server.worldData.sessionData);

        // All cmds in one list, in send order, so the joiner executes them exactly like live clients did
        var cmds = Server.worldData.cmds;
        writer.WriteInt32(cmds.Count);
        foreach (var cmd in cmds)
            writer.WritePrefixedBytes(cmd);

        writer.WriteInt32(Server.worldData.mapData.Count);

        foreach (var kv in Server.worldData.mapData)
        {
            int mapId = kv.Key;
            byte[] mapData = kv.Value;

            writer.WriteInt32(mapId);
            writer.WritePrefixedBytes(mapData);
        }

        // The world and maps usually came in one upload with the same limit, but the command log since the join point
        // and the sync opinions come on top. The opinions only let the joiner check its catch-up, they get what's left.
        var syncInfoSpace = ConnectionBase.MaxFragmentedMessageLength - writer.Position - sizeof(int);
        if (syncInfoSpace < 0)
        {
            RejectOversizeWorldData(writer.Position);
            return false;
        }

        var syncInfos = Server.worldData.NewestSyncInfosFitting(syncInfoSpace);
        if (syncInfos.Count < Server.worldData.syncInfos.Count)
            ServerLog.Detail($"World data for {Player.Username}: only {syncInfos.Count} of " +
                             $"{Server.worldData.syncInfos.Count} sync opinions fit");

        writer.WriteInt32(syncInfos.Count);
        foreach (var syncInfo in syncInfos)
            writer.WritePrefixedBytes(syncInfo);

        byte[] packetData = writer.ToArray();
        connection.Send(Packets.Server_WorldDataStart);
        connection.SendFragmented(Packets.Server_WorldData, packetData);

        ServerLog.Log("World response sent: " + packetData.Length);
        return true;
    }

    // SendFragmented would throw, which disconnects with a generic state exception and repeats for every joiner
    private void RejectOversizeWorldData(int length)
    {
        const double MiB = 1024 * 1024;
        var worldData = Server.worldData;
        var saveLength = (worldData.savedGame?.Length ?? 0) + (worldData.sessionData?.Length ?? 0) +
                         worldData.mapData.Values.Sum(map => (long)map.Length);
        var cmdsLength = worldData.cmds.Sum(cmd => (long)cmd.Length);

        var details = $"{length / MiB:0.0} MiB > {ConnectionBase.MaxFragmentedMessageLength / MiB:0.0} MiB; " +
                      $"save {saveLength / MiB:0.0} MiB, command log since the last join point " +
                      $"{cmdsLength / MiB:0.0} MiB ({worldData.cmds.Count} commands)";
        ServerLog.Error($"Can't send the world data to {Player.Username}, it's too big ({details}). " +
                        "A new join point (e.g. saving the game) clears the command log.");
        Server.SendChat($"{Player.Username} can't join: the world data is too big ({details})");
        Player.Disconnect($"The server's world data is too big to send ({details}).");
    }
}
