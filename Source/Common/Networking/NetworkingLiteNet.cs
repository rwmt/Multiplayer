using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace Multiplayer.Common
{
    public class MpServerNetListener(MultiplayerServer server, bool arbiter) : INetEventListener
    {
        public void OnConnectionRequest(ConnectionRequest req)
        {
            var result = server.playerManager.OnPreConnect(req.RemoteEndPoint.Address);
            if (result != null)
            {
                req.Reject(ConnectionBase.GetDisconnectBytes(result.Value));
                return;
            }

            req.Accept();
        }

        public void OnPeerConnected(NetPeer peer)
        {
            var conn = new LiteNetConnection(peer);

            // The connection state constructors (and StartState) often rely on connection.serverPlayer / Player.id.
            // Ensure the ServerPlayer is created before we enter any server state.
            var player = server.playerManager.OnConnected(conn);

            // Always start with the standard joining handshake (protocol/username/join-data).
            // ServerJoiningState already sends ServerBootstrapPacket early when BootstrapMode is enabled,
            // so a configurator client can switch UI flows without us skipping the handshake.
            conn.ChangeState(ConnectionStateEnum.ServerJoining);
            peer.SetConnection(conn);
            if (arbiter)
            {
                player.type = PlayerType.Arbiter;
                player.color = new ColorRGB(128, 128, 128);
            }
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            ConnectionBase conn = peer.GetConnection();
            var reason = disconnectInfo.Reason switch
            {
                // we (the server) closed the connection
                DisconnectReason.DisconnectPeerCalled => MpDisconnectReason.ClientLeft,
                // the client closed the connection
                DisconnectReason.RemoteConnectionClose => MpDisconnectReason.ClientLeft,
                _ => MpDisconnectReason.NetFailed
            };
            if (reason != MpDisconnectReason.ClientLeft)
                ServerLog.Log($"Peer {conn} disconnected unexpectedly: " +
                              $"{disconnectInfo.Reason}/{disconnectInfo.SocketErrorCode}");
            server.playerManager.SetDisconnected(conn, reason);
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            // LiteNetLib can emit latency updates very early or during shutdown.
            // At that time the NetPeer might not yet have our ConnectionBase attached.
            var conn = peer.GetConnection();
            if (conn == null)
                return;

            conn.Latency = latency;
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod method)
        {
            byte[] data = reader.GetRemainingBytes();

            var conn = peer.GetConnection();
            var player = conn?.serverPlayer;
            if (player == null)
            {
                // Shouldn't normally happen because we create the ServerPlayer before changing state,
                // but guard anyway to avoid taking down the server tick.
                ServerLog.Error($"Received packet from peer without a bound ServerPlayer ({peer}). Dropping {data.Length} bytes");
                return;
            }

            player.HandleReceive(new ByteReader(data), method == DeliveryMethod.ReliableOrdered);
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    }
}
