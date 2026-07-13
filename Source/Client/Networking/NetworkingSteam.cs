using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using Steamworks;
using Verse;
using Verse.Steam;

namespace Multiplayer.Client.Networking
{
    public abstract class SteamBaseConn(CSteamID remoteId, ushort recvChannel, ushort sendChannel) : ConnectionBase
    {
        public readonly CSteamID remoteId = remoteId;

        public readonly ushort recvChannel = recvChannel; // currently only for client
        public readonly ushort sendChannel = sendChannel; // currently only for server

        // Time given to Steam to flush a queued goodbye packet before the P2P session is freed (#843).
        private static readonly TimeSpan SteamGoodbyeFlushDelay = TimeSpan.FromSeconds(3);

        protected override void SendRaw(byte[] raw, bool reliable = true)
        {
            byte[] full = new byte[1 + raw.Length];
            full[0] = reliable ? (byte)2 : (byte)0;
            raw.CopyTo(full, 1);

            SendRawSteam(full, reliable);
        }

        public void SendRawSteam(byte[] raw, bool reliable)
        {
            var sent = SteamNetworking.SendP2PPacket(
                remoteId,
                raw,
                (uint)raw.Length,
                reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliable,
                sendChannel
            );
            if (sent) return;
            var hex = raw.SubArray(0, Math.Min(raw.Length, 128)).ToHexString();
            ServerLog.Error($"Failed to send packet (len: {raw.Length}): {hex}");
        }

        public abstract void OnError(EP2PSessionError error);

        // A goodbye is only ever non-null server-side, and CloseP2PSessionWithUser discards queued unsent
        // packets. Closing immediately would drop the just-queued goodbye (e.g. a wrong-password or kick
        // reason) before Steam flushes it, so defer the close to let the reliable packet reach the client.
        protected override void OnClose(ServerDisconnectPacket? goodbye)
        {
            if (!goodbye.HasValue)
            {
                CloseSteamSession();
                return;
            }

            Send(goodbye.Value);

            var server = serverPlayer.Server;
            var id = remoteId;

            Task.Delay(SteamGoodbyeFlushDelay).ContinueWith(_ => server.Enqueue(() =>
            {
                // A fast reconnect (SteamP2PNetManager.Tick) reuses this same remoteId on a fresh
                // connection during the delay window. Closing then would tear that new session down, so
                // skip the close if another connection already replaced this one (#843).
                if (server.playerManager.Players.Any(p => p.conn is SteamBaseConn c && c != this && c.remoteId == id))
                {
                    ServerLog.Log($"Skipping delayed Steam P2P session close with {id}; a reconnect replaced it");
                    return;
                }

                CloseSteamSession();
            }));
        }

        // Frees the underlying Steam P2P session. This is required so that a later reconnect from
        // the same user produces a fresh P2PSessionRequest_t (and, on the host, a new accept prompt)
        // instead of Steam silently reusing the still-open session, which left the peer stuck and the
        // host without a prompt (#843).
        //
        // Called immediately when no goodbye is queued; server-initiated disconnects that queue a goodbye
        // defer it (see OnClose) so SendP2PPacket can flush the reason before the session is torn down.
        protected void CloseSteamSession()
        {
            ServerLog.Log($"Closing Steam P2P session with {remoteId}");
            SteamNetworking.CloseP2PSessionWithUser(remoteId);
        }

        public override string ToString() => $"SteamP2P ({remoteId}:{username})";
    }

    public class SteamClientConn : SteamBaseConn, ITickableConnection
    {
        public SteamClientConn(CSteamID remoteId, string username) : base(remoteId, RandomChannelId(), 0)
        {
            ChangeState(new ClientSteamState(this, username));
        }

        static ushort RandomChannelId() => (ushort)new Random().Next();

        public void Tick()
        {
            foreach (var packet in SteamP2PIntegration.ReadPackets(recvChannel))
            {
                // Note: receive can lead to disconnection
                if (State == ConnectionStateEnum.Disconnected) return;
                if (packet.remote == remoteId) HandleReceiveRaw(packet.data, packet.reliable);
            }
        }

        public override void OnError(EP2PSessionError error)
        {
            var title = error == EP2PSessionError.k_EP2PSessionErrorTimeout
                ? "MpSteamTimedOut".Translate()
                : "MpSteamGenericError".Translate();
            ConnectionStatusListeners.TryNotifyAll_Disconnected(new SessionDisconnectInfo { titleTranslated = title });
            Multiplayer.StopMultiplayer();
        }
    }

    public class SteamServerConn(CSteamID remoteId, ushort clientChannel) : SteamBaseConn(remoteId, 0, clientChannel)
    {
        private readonly Stopwatch keepAliveTimer = new();

        protected override void Send(Packets id, byte[] message, bool reliable = true)
        {
            if (id == Packets.Server_KeepAlive) keepAliveTimer.Restart();
            base.Send(id, message, reliable);
        }

        public override void OnError(EP2PSessionError error)
        {
            OnDisconnect();
        }

        public override void OnKeepAliveArrived(bool idMatched)
        {
            if (!idMatched) return;
            // We are ticking network logic every ~30ms, which means that effectively the lowest ping achievable is
            // ~15ms.
            Latency = (Latency * 4 + (int)keepAliveTimer.ElapsedMilliseconds / 2) / 5;
            keepAliveTimer.Reset();
        }

        private void OnDisconnect()
        {
            // The P2P timeout/error path does not go through OnClose, so close the Steam session here
            // too. Otherwise the host keeps a half-open session with the departed client and their
            // reconnect reuses it without firing a new accept prompt (#843).
            CloseSteamSession();
            serverPlayer.Server.playerManager.SetDisconnected(this, MpDisconnectReason.ClientLeft);
        }
    }

    public class SteamP2PNetManager : INetManager
    {
        private readonly MultiplayerServer server;

        private SteamP2PNetManager(MultiplayerServer server) => this.server = server;

        public static SteamP2PNetManager Create(MultiplayerServer server)
        {
            if (!SteamManager.Initialized) return null;
            return new SteamP2PNetManager(server);
        }

        public void Tick()
        {
            foreach (var packet in SteamP2PIntegration.ReadPackets(0))
            {
                var playerManager = server.playerManager;
                var player = playerManager.Players
                    .FirstOrDefault(p => p.conn is SteamBaseConn conn && conn.remoteId == packet.remote);

                // A join packet from a remote we still consider connected means their previous
                // session died and they are reconnecting on a fresh one (e.g. a quick rejoin before
                // the old connection timed out). Drop the stale player so the join is accepted below
                // instead of being discarded, which would otherwise leave them stuck on "waiting for
                // host to accept" (#843).
                //
                // Use SetDisconnected, not Close/Disconnect: the new join packet just arrived on this
                // same Steam P2P session, and Close -> OnClose -> CloseSteamSession would tear that
                // shared session down and break the very connection we're about to accept.
                if (packet.joinPacket && player != null)
                {
                    ServerLog.Log($"Reconnect from {packet.remote}; replacing stale connection {player.conn}");
                    playerManager.SetDisconnected(player.conn, MpDisconnectReason.ClientLeft);
                    player = null;
                }

                if (packet.joinPacket && player == null)
                {
                    ConnectionBase conn = new SteamServerConn(packet.remote, packet.channel);

                    var preConnect = playerManager.OnPreConnect(packet.remote);
                    if (preConnect != null)
                    {
                        ServerLog.Log($"Rejected incoming connection from {packet.remote}: {preConnect}");
                        conn.Close(preConnect.Value);
                        continue;
                    }

                    conn.ChangeState(ConnectionStateEnum.ServerJoining);
                    player = playerManager.OnConnected(conn);
                    player.type = PlayerType.Steam;

                    player.steamId = (ulong)packet.remote;
                    player.steamPersonaName = SteamFriends.GetFriendPersonaName(packet.remote);
                    if (player.steamPersonaName.Length == 0)
                        player.steamPersonaName = "[unknown]";

                    conn.Send(Packets.Server_SteamAccept);
                }
                else if (!packet.joinPacket && player != null)
                {
                    player.HandleReceive(packet.data, packet.reliable);
                }
                else
                {
                    ServerLog.Error(
                        $"Received a join packet: {packet.joinPacket} for player: {player} (player should only be null when joinPacket is true)");
                }
            }
        }

        public void Stop()
        {
            // Managed externally by Steamworks
        }

        public string GetDiagnosticsName() => "SteamP2P";

        public string GetDiagnosticsInfo() => null;
    }

    public static class SteamP2PIntegration
    {
        private static Callback<P2PSessionConnectFail_t> p2pFail;

        public static void InitCallbacks()
        {
            p2pFail = Callback<P2PSessionConnectFail_t>.Create(fail =>
            {
                var remoteId = fail.m_steamIDRemote;
                var error = (EP2PSessionError)fail.m_eP2PSessionError;
                ServerLog.Error($"Steam P2P session fail for {remoteId}: {error}");

                var session = Multiplayer.session;
                if (session == null) return;

                if (session.client is SteamBaseConn clientConn && clientConn.remoteId == remoteId)
                    clientConn.OnError(error);

                var server = Multiplayer.LocalServer;
                if (server == null) return;

                server.Enqueue(() =>
                {
                    var conn = server.playerManager.Players.Select(p => p.conn).OfType<SteamBaseConn>()
                        .FirstOrDefault(c => c.remoteId == remoteId);
                    conn?.OnError(error);
                });
            });
        }

        internal struct SteamPacket
        {
            public CSteamID remote;
            public ByteReader data;
            public bool joinPacket;
            public bool reliable;
            public ushort channel;
        }

        internal static IEnumerable<SteamPacket> ReadPackets(int recvChannel)
        {
            while (SteamNetworking.IsP2PPacketAvailable(out uint size, recvChannel))
            {
                byte[] data = new byte[size];

                if (!SteamNetworking.ReadP2PPacket(data, size, out uint sizeRead, out CSteamID remote, recvChannel)) continue;
                if (data.Length <= 0) continue;

                var reader = new ByteReader(data);
                byte flags = reader.ReadByte();
                bool joinPacket = (flags & 1) > 0;
                bool reliable = (flags & 2) > 0;
                bool hasChannel = (flags & 4) > 0;
                ushort channel = hasChannel ? reader.ReadUShort() : (ushort)0;

                yield return new SteamPacket
                {
                    remote = remote, data = reader, joinPacket = joinPacket, reliable = reliable, channel = channel
                };
            }
        }
    }
}
