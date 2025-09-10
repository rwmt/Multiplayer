using System;
using LiteNetLib;
using Multiplayer.Common;
using System.Net;
using System.Net.Sockets;
using Multiplayer.Client.Util;
using Verse;

namespace Multiplayer.Client.Networking
{
    public class ClientLiteNetConnection : LiteNetConnection, ITickableConnection
    {
        private readonly NetManager netManager;

        private ClientLiteNetConnection(NetPeer peer, NetManager netManager) : base(peer) =>
            this.netManager = netManager;

        ~ClientLiteNetConnection()
        {
            if (netManager.IsRunning)
            {
                Log.Error("[ClientLiteNetConnection] NetManager did not get stopped");
                netManager.Stop();
            }
        }

        public static ClientLiteNetConnection Connect(string address, int port)
        {
            var netClient = new NetManager(new NetListener())
            {
                EnableStatistics = true,
                IPv6Enabled = MpUtil.SupportsIPv6() ? IPv6Mode.SeparateSocket : IPv6Mode.Disabled,
                ReconnectDelay = 300,
                MaxConnectAttempts = 8
            };
            netClient.Start();
            var peer = netClient.Connect(address, port, "");
            var conn = new ClientLiteNetConnection(peer, netClient);
            peer.SetConnection(conn);
            return conn;
        }

        public void Tick() => netManager.PollEvents();

        public void OnDisconnect(MpDisconnectReason reason, ByteReader data)
        {
            Multiplayer.session.ProcessDisconnectPacket(reason, data);
            ConnectionStatusListeners.TryNotifyAll_Disconnected();
            Multiplayer.StopMultiplayer();
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
            base.Close(reason, data);
            netManager.Stop();
        }

        private class NetListener : INetEventListener
        {
            private ClientLiteNetConnection GetConnection(NetPeer peer) =>
                peer.GetConnection() as ClientLiteNetConnection ?? throw new Exception("Can't get connection");

            public void OnPeerConnected(NetPeer peer)
            {
                GetConnection(peer).ChangeState(ConnectionStateEnum.ClientJoining);
                MpLog.Log("Net client connected");
            }

            public void OnNetworkError(IPEndPoint endPoint, SocketError error)
            {
                MpLog.Warn($"Net client error {error}");
            }

            public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod method)
            {
                byte[] data = reader.GetRemainingBytes();
                GetConnection(peer).HandleReceiveRaw(new ByteReader(data), method == DeliveryMethod.ReliableOrdered);
            }

            public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
            {
                MpDisconnectReason reason;
                ByteReader reader;

                if (info.AdditionalData.IsNull)
                {
                    if (info.Reason is DisconnectReason.DisconnectPeerCalled or DisconnectReason.RemoteConnectionClose)
                        reason = MpDisconnectReason.Generic;
                    else if (Multiplayer.Client == null)
                        reason = MpDisconnectReason.ConnectingFailed;
                    else
                        reason = MpDisconnectReason.NetFailed;

                    reader = new ByteReader(ByteWriter.GetBytes(info.Reason));
                }
                else
                {
                    reader = new ByteReader(info.AdditionalData.GetRemainingBytes());
                    reason = reader.ReadEnum<MpDisconnectReason>();
                }

                GetConnection(peer).OnDisconnect(reason, reader);
                MpLog.Log($"Net client disconnected {info.Reason}");
            }

            public void OnConnectionRequest(ConnectionRequest request)
            {
            }

            public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
            {
            }

            public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader,
                UnconnectedMessageType messageType)
            {
            }
        }
    }

    public class LiteNetLogger : INetLogger
    {
        public static void Install() => NetDebug.Logger = new LiteNetLogger();

        public void WriteNet(NetLogLevel level, string str, params object[] args)
        {
            if (level == NetLogLevel.Error)
                ServerLog.Error(string.Format(str, args));
            else
                ServerLog.Log(string.Format(str, args));
        }
    }
}
