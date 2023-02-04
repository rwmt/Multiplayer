using LiteNetLib;
using Multiplayer.Common;
using System.Net;
using System.Net.Sockets;
using Multiplayer.Client.Util;

namespace Multiplayer.Client.Networking
{

    public class MpClientNetListener : INetEventListener
    {
        public void OnPeerConnected(NetPeer peer)
        {
            ConnectionBase conn = new LiteNetConnection(peer);
            conn.username = Multiplayer.username;
            conn.State = ConnectionStateEnum.ClientJoining;
            conn.StateObj.StartState();

            Multiplayer.session.client = conn;
            Multiplayer.session.ReapplyPrefs();

            MpLog.Log("Net client connected");
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError error)
        {
            MpLog.Warn($"Net client error {error}");
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod method)
        {
            byte[] data = reader.GetRemainingBytes();
            ClientUtil.HandleReceive(new ByteReader(data), method == DeliveryMethod.ReliableOrdered);
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            MpDisconnectReason reason;
            byte[] data;

            if (info.AdditionalData.IsNull)
            {
                if (info.Reason is DisconnectReason.DisconnectPeerCalled or DisconnectReason.RemoteConnectionClose)
                    reason = MpDisconnectReason.Generic;
                else if (Multiplayer.Client == null)
                    reason = MpDisconnectReason.ConnectingFailed;
                else
                    reason = MpDisconnectReason.NetFailed;

                data = new [] { (byte)info.Reason };
            }
            else
            {
                var reader = new ByteReader(info.AdditionalData.GetRemainingBytes());
                reason = (MpDisconnectReason)reader.ReadByte();
                data = reader.ReadPrefixedBytes();
            }

            Multiplayer.session.ProcessDisconnectPacket(reason, data);
            ConnectionStatusListeners.TryNotifyAll_Disconnected();

            Multiplayer.StopMultiplayer();
            MpLog.Log($"Net client disconnected {info.Reason}");
        }

        public void OnConnectionRequest(ConnectionRequest request) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    }
}
