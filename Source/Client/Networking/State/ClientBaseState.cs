using System;
using System.Linq;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using Verse;

namespace Multiplayer.Client;

public abstract class ClientBaseState(ConnectionBase connection) : MpConnectionState(connection)
{
    protected MultiplayerSession Session => Multiplayer.session;

    // PlayerList, chat and notifications live here rather than in
    // ClientPlayingState: the server flips a joiner to ServerPlaying as soon
    // as the world data is queued (ServerLoadingState.RunState), so these
    // broadcasts can legally reach a client still in ClientLoading - and
    // Steam P2P delivers reliable packets out of order besides (observed
    // Server_PlayerList ahead of Server_WorldDataStart, 2026-07-28). An
    // unhandled reliable packet kills the session, so the loading state must
    // handle them. All three touch session/UI state only; the full List sent
    // after the world data reconciles anything applied during the window.
    [TypedPacketHandler]
    public void HandlePlayerList(ServerPlayerListPacket packet)
    {
        if (packet.action == PlayerListAction.Add)
        {
            foreach (var info in packet.players)
            {
                if (!Multiplayer.session.players.Any(p => p.id == info.id || p.username == info.username))
                {
                    ServerLog.Log($"PlayerList: Adding player {info.id}:{info.username}");
                    Multiplayer.session.players.Add(PlayerInfo.FromNet(info));
                }
                else
                {
                    ServerLog.Error($"PlayerList: Adding player {info.id}:{info.username} - player already exists");
                }
            }
        }
        else if (packet.action == PlayerListAction.Remove)
        {
            ServerLog.Log($"PlayerList: Removing player with id {packet.playerId}");
            var matches = Multiplayer.session.players.RemoveAll(p => p.id == packet.playerId);
            if (matches > 1)
            {
                ServerLog.Error($"PlayerList: Removing player with id {packet.playerId} -- occurred {matches} times. This should not happen");
            }
        }
        else if (packet.action == PlayerListAction.List)
        {
            ServerLog.Log($"PlayerList: Received player list with {packet.players.Length} entries");

            Multiplayer.session.players.Clear();
            foreach (var info in packet.players)
            {
                ServerLog.Log($"PlayerList: Adding player from list {info.id}:{info.username}");
                Multiplayer.session.players.Add(PlayerInfo.FromNet(info));
            }
        }
        else if (packet.action == PlayerListAction.Latencies)
        {
            foreach (var latency in packet.latencies)
            {
                var player = Multiplayer.session.GetPlayerInfo(latency.playerId);
                if (player == null)
                {
                    ServerLog.Log($"PlayerList: Received latency info for unknown player with id {latency.playerId}");
                    continue;
                }
                player.latency = latency.latency;
                player.ticksBehind = latency.ticksBehind;
                player.simulating = latency.simulating;
                player.frameTime = latency.frameTime;
            }
        }
        else if (packet.action == PlayerListAction.Status)
        {
            var player = Multiplayer.session.GetPlayerInfo(packet.playerId);
            if (player == null)
            {
                ServerLog.Log($"PlayerList: Received player status ({packet.status}) for unknown player with id {packet.playerId}");
            }
            else
            {
                player.status = packet.status;
            }
        }
    }

    [TypedPacketHandler]
    public void HandleChat(ServerChatPacket packet) => Multiplayer.session.AddMsg(packet.msg, rawMessage: packet.rawMessage);

    [TypedPacketHandler]
    public void HandleNotification(ServerNotificationPacket packet)
    {
        var namedArgs = Array.ConvertAll(packet.args, s => (NamedArgument)s);
        var msg = packet.key.Translate(namedArgs);
        Messages.Message(msg, MessageTypeDefOf.SilentInput, false);
        ServerLog.Log($"Notification: {msg} ({packet.key}, {packet.args.Join(", ")})");
    }

    [TypedPacketHandler]
    public void HandleKeepAlive(ServerKeepAlivePacket packet)
    {
        int ticksBehind = TickPatch.tickUntil - TickPatch.Timer;

        connection.Send(new ClientKeepAlivePacket(packet.id, ticksBehind, TickPatch.Simulating, TickPatch.workTicks),
            false);
    }

    [TypedPacketHandler]
    public void HandleTimeControl(ServerTimeControlPacket packet)
    {
        if (Multiplayer.session.remoteTickUntil >= packet.tickUntil) return;

        TickPatch.serverTimePerTick = packet.serverTimePerTick;
        Multiplayer.session.remoteTickUntil = packet.tickUntil;
        Multiplayer.session.remoteSentCmds = packet.sentCmds;
        Multiplayer.session.ProcessTimeControl();
    }

    // Currently handles disconnection only for Steam connections. See comment in ConnectionBase.Close for more info.
    [TypedPacketHandler]
    public virtual void HandleDisconnected(ServerDisconnectPacket packet)
    {
        ConnectionStatusListeners.TryNotifyAll_Disconnected(SessionDisconnectInfo.From(packet));
        Multiplayer.StopMultiplayer();
    }
}
