using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Multiplayer.Common;
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

        public override void Close(MpDisconnectReason reason, byte[] data = null)
        {
            if (State != ConnectionStateEnum.ClientSteam)
                Send(Packets.Special_Steam_Disconnect, GetDisconnectBytes(reason, data));
        }

        public abstract void OnError(EP2PSessionError error);

        public override string ToString()
        {
            return $"SteamP2P ({remoteId}:{username})";
        }
    }

    public class SteamClientConn(CSteamID remoteId) : SteamBaseConn(remoteId, RandomChannelId(), 0), ITickableConnection
    {
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

        protected override void HandleReceiveMsg(int msgId, int fragState, ByteReader reader, bool reliable)
        {
            if (msgId == (int)Packets.Special_Steam_Disconnect)
            {
                var info = SessionDisconnectInfo.From(reader.ReadEnum<MpDisconnectReason>(), reader);
                OnDisconnect(info);
                return;
            }

            base.HandleReceiveMsg(msgId, fragState, reader, reliable);
        }

        public override void OnError(EP2PSessionError error)
        {
            var title = error == EP2PSessionError.k_EP2PSessionErrorTimeout
                ? "MpSteamTimedOut".Translate()
                : "MpSteamGenericError".Translate();

            OnDisconnect(new SessionDisconnectInfo { titleTranslated = title });
        }

        private void OnDisconnect(SessionDisconnectInfo info)
        {
            ConnectionStatusListeners.TryNotifyAll_Disconnected(info);
            Multiplayer.StopMultiplayer();
        }
    }

    public class SteamServerConn(CSteamID remoteId, ushort clientChannel) : SteamBaseConn(remoteId, 0, clientChannel)
    {
        private readonly Stopwatch keepAliveTimer = new();

        public override void Send(Packets id, byte[] message, bool reliable = true)
        {
            if (id == Packets.Server_KeepAlive) keepAliveTimer.Restart();
            base.Send(id, message, reliable);
        }

        protected override void HandleReceiveMsg(int msgId, int fragState, ByteReader reader, bool reliable)
        {
            if (msgId == (int)Packets.Special_Steam_Disconnect)
            {
                OnDisconnect();
                return;
            }

            base.HandleReceiveMsg(msgId, fragState, reader, reliable);
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

                if (packet.joinPacket && player == null)
                {
                    ConnectionBase conn = new SteamServerConn(packet.remote, packet.channel);

                    var preConnect = playerManager.OnPreConnect(packet.remote);
                    if (preConnect != null)
                    {
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

                if (!packet.joinPacket && player != null)
                {
                    player.HandleReceive(packet.data, packet.reliable);
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
                var session = Multiplayer.session;
                if (session == null) return;

                var remoteId = fail.m_steamIDRemote;
                var error = (EP2PSessionError)fail.m_eP2PSessionError;

                if (Multiplayer.Client is SteamBaseConn clientConn && clientConn.remoteId == remoteId)
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
