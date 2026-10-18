using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        public override string ToString() => $"SteamP2P ({remoteId}:{username})";
    }

    public class SteamClientConn : SteamBaseConn, ITickableConnection
    {
        public SteamClientConn(CSteamID remoteId, string username) : base(remoteId, RandomChannelId(), 0)
        {
            // A server hosted here before may still be about to close its session with this remote
            SteamP2PIntegration.KeepSession(remoteId);
            ChangeState(new ClientSteamState(this, username));
        }

        // The client never sends a goodbye
        protected override void OnClose(ServerDisconnectPacket? goodbye) => SteamP2PIntegration.CloseSession(remoteId);

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

        protected override void OnClose(ServerDisconnectPacket? goodbye)
        {
            if (goodbye.HasValue)
            {
                Send(goodbye.Value);
                SteamP2PIntegration.CloseSessionAfterFlush(remoteId);
            }
            // A disconnected connection may have been replaced by a new one from the same remote, which uses the
            // same session (see SteamP2PNetManager.Tick)
            else if (State != ConnectionStateEnum.Disconnected)
                SteamP2PIntegration.CloseSession(remoteId);
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
            // The session failed, free it so that a reconnect asks for a new one
            SteamP2PIntegration.CloseSession(remoteId);
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
            var playerManager = server.playerManager;

            // Closed here rather than on a timer so that it can't race a reconnect handled below
            SteamP2PIntegration.CloseDueSessions(remote => FindPlayer(remote) != null);

            foreach (var packet in SteamP2PIntegration.ReadPackets(0))
            {
                var player = FindPlayer(packet.remote);

                if (packet.joinPacket)
                {
                    ConnectionBase conn = new SteamServerConn(packet.remote, packet.channel);

                    // The join packet arrived on the session of the remote's earlier connection: a goodbye sent on
                    // it is out by now and closing would cut off this one
                    SteamP2PIntegration.KeepSession(packet.remote);

                    // A client sends the join packet once per connection, so the remote's connection we still
                    // have is stale (e.g. they restarted the game before it timed out). Without replacing it the
                    // join would be dropped and the client would wait for the host forever (#843).
                    if (player != null)
                        playerManager.ReplaceStale(player, conn);

                    var preConnect = playerManager.OnPreConnect(packet.remote);
                    if (preConnect != null)
                    {
                        ServerLog.Log($"Rejected incoming connection from {packet.remote}: {preConnect}");
                        // Close doesn't send a goodbye before the connection has a server state
                        conn.Send(new ServerDisconnectPacket { reason = preConnect.Value, data = [] });
                        SteamP2PIntegration.CloseSessionAfterFlush(packet.remote);
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
                else if (player != null)
                {
                    player.HandleReceive(packet.data, packet.reliable);
                }
                else
                {
                    // Packets still in flight when the server dropped the connection
                    ServerLog.Verbose($"Received a data packet from {packet.remote}, who has no connection");
                }
            }
        }

        private ServerPlayer FindPlayer(CSteamID remote) =>
            server.playerManager.Players.FirstOrDefault(p => p.conn is SteamBaseConn conn && conn.remoteId == remote);

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

        // Closing a P2P session discards the packets Steam hasn't sent yet, a goodbye gets this long to go out
        private const int GoodbyeFlushMs = 3000;
        private static readonly PendingSessionCloses<CSteamID> pendingCloses = new();

        /// Frees the P2P session with the remote. Their next connection then needs a new session, which shows the
        /// host's accept prompt again, instead of silently reusing the old one (#843).
        public static void CloseSession(CSteamID remote)
        {
            pendingCloses.Cancel(remote);
            ServerLog.Log($"Closing Steam P2P session with {remote}");
            SteamNetworking.CloseP2PSessionWithUser(remote);
        }

        public static void CloseSessionAfterFlush(CSteamID remote) => pendingCloses.Schedule(remote, GoodbyeFlushMs);

        /// The remote's session is in use by a new connection
        public static void KeepSession(CSteamID remote)
        {
            if (pendingCloses.Cancel(remote))
                ServerLog.Verbose($"Keeping Steam P2P session with {remote} for a new connection");
        }

        /// Runs on the thread of the server handling Steam joins, or on the main thread when no such server runs.
        /// <param name="inUse">The remote reconnected in the meantime and their connection uses the session</param>
        public static void CloseDueSessions(Func<CSteamID, bool> inUse = null)
        {
            foreach (var remote in pendingCloses.TakeDue())
            {
                if (inUse?.Invoke(remote) == true) continue;

                ServerLog.Log($"Closing Steam P2P session with {remote} after its goodbye");
                SteamNetworking.CloseP2PSessionWithUser(remote);
            }
        }

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
