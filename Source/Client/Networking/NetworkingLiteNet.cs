using LiteNetLib;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Multiplayer.Client.Networking
{
    public class MpClientNetListener : INetEventListener
    {
        public void OnPeerConnected(NetPeer peer)
        {
            IConnection conn = new MpNetConnection(peer);
            conn.username = Multiplayer.username;
            conn.State = ConnectionStateEnum.ClientJoining;

            Multiplayer.session.client = conn;
            Multiplayer.session.ReapplyPrefs();

            MpLog.Log("Net client connected");
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError error)
        {
            Log.Warning($"Net client error {error}");
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
                reason = MpDisconnectReason.Failed;
                data = new byte[0];
            }
            else
            {
                var reader = new ByteReader(info.AdditionalData.GetRemainingBytes());
                reason = (MpDisconnectReason)reader.ReadByte();
                data = reader.ReadPrefixedBytes();
            }

            Multiplayer.session.ProcessDisconnectPacket(reason, data);

            ConnectionStatusListeners.TryNotifyAll_Disconnected();

            OnMainThread.StopMultiplayer();
            MpLog.Log("Net client disconnected");
        }

        public void OnConnectionRequest(ConnectionRequest request) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    }
}
