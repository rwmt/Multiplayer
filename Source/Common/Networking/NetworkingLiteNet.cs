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
            conn.ChangeState(ConnectionStateEnum.ServerJoining);
            peer.SetConnection(conn);

            var player = server.playerManager.OnConnected(conn);
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
            peer.GetConnection().Latency = latency;
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod method)
        {
            byte[] data = reader.GetRemainingBytes();
            peer.GetConnection().serverPlayer.HandleReceive(new ByteReader(data), method == DeliveryMethod.ReliableOrdered);
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    }
}
