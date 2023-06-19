using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace Multiplayer.Common
{
    public class MpServerNetListener : INetEventListener
    {
        private MultiplayerServer server;
        private bool arbiter;

        public MpServerNetListener(MultiplayerServer server, bool arbiter)
        {
            this.server = server;
            this.arbiter = arbiter;
        }

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
            ConnectionBase conn = new LiteNetConnection(peer);
            conn.ChangeState(ConnectionStateEnum.ServerJoining);
            peer.Tag = conn;

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
            server.playerManager.SetDisconnected(conn, MpDisconnectReason.ClientLeft);
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            peer.GetConnection().Latency = latency;
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod method)
        {
            byte[] data = reader.GetRemainingBytes();
            peer.GetConnection().serverPlayer.HandleReceive(new ByteReader(data), method == DeliveryMethod.ReliableOrdered);
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    }
}
