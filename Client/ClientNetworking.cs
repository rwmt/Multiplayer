using Harmony;
using LiteNetLib;
using Multiplayer.Common;
using Steamworks;
using System;
using System.Net;
using System.Net.Sockets;
using Verse;

namespace Multiplayer.Client
{
    public static class ClientUtil
    {
        public static void TryConnect(IPAddress address, int port)
        {
            EventBasedNetListener listener = new EventBasedNetListener();

            Multiplayer.session = new MultiplayerSession();
            NetManager netClient = new NetManager(listener);
            netClient.Start();
            netClient.ReconnectDelay = 300;
            netClient.MaxConnectAttempts = 8;

            listener.PeerConnectedEvent += peer =>
            {
                OnMainThread.Enqueue(() =>
                {
                    IConnection conn = new MpNetConnection(peer);
                    peer.Tag = conn;

                    conn.Username = Multiplayer.username;
                    conn.State = new ClientJoiningState(conn);
                    Multiplayer.session.client = conn;

                    ConnectionStatusListeners.All.Do(a => a.Connected());

                    MpLog.Log("Client connected");
                });
            };

            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                OnMainThread.Enqueue(() =>
                {
                    string reason = DisconnectReasonString(info.Reason);
                    if (info.SocketErrorCode != SocketError.Success)
                        reason += ": " + info.SocketErrorCode;

                    Multiplayer.session.disconnectNetReason = reason;

                    ConnectionStatusListeners.All.Do(a => a.Disconnected());

                    OnMainThread.StopMultiplayer();
                    MpLog.Log("Client disconnected");
                });
            };

            listener.NetworkReceiveEvent += (peer, reader, method) =>
            {
                byte[] data = reader.GetRemainingBytes();
                OnMainThread.Enqueue(() => peer.GetConnection().HandleReceive(data));
            };

            Multiplayer.session.netClient = netClient;
            netClient.Connect(address.ToString(), port, "");
        }

        private static string DisconnectReasonString(DisconnectReason reason)
        {
            switch (reason)
            {
                case DisconnectReason.ConnectionFailed: return "Connection failed";
                case DisconnectReason.ConnectionRejected: return "Connection rejected";
                case DisconnectReason.Timeout: return "Timed out";
                case DisconnectReason.SocketSendError: return "Socket send error";
                case DisconnectReason.SocketReceiveError: return "Socket receive error";
                default: return "Disconnected";
            }
        }
    }

    public class LocalClientConnection : IConnection
    {
        public LocalServerConnection server;

        public LocalClientConnection(string username)
        {
            Username = username;
        }

        public override string Username { get; set; }
        public override int Latency { get => 0; set { } }
        public override MpConnectionState State { get; set; }

        public override void SendRaw(byte[] raw)
        {
            void run() => server.HandleReceive(raw);

            if (UnityData.IsInMainThread)
                run();
            else
                OnMainThread.Enqueue(run);
        }

        public override void Close()
        {
        }
    }

    public class LocalServerConnection : IConnection
    {
        public LocalClientConnection client;

        public LocalServerConnection(string username)
        {
            Username = username;
        }

        public override string Username { get; set; }
        public override int Latency { get => 0; set { } }
        public override MpConnectionState State { get; set; }

        public override void SendRaw(byte[] raw)
        {
            void run() => client.HandleReceive(raw);

            if (UnityData.IsInMainThread)
                run();
            else
                OnMainThread.Enqueue(run);
        }

        public override void Close()
        {
        }
    }

    public class SteamConnection : IConnection
    {
        public override string Username { get; set; }
        public override int Latency { get; set; }
        public override MpConnectionState State { get; set; }

        public readonly CSteamID remoteId;

        public SteamConnection(CSteamID remoteId)
        {
            this.remoteId = remoteId;
        }

        public override void SendRaw(byte[] raw)
        {
            SteamNetworking.SendP2PPacket(remoteId, raw, (uint)raw.Length, EP2PSend.k_EP2PSendReliable);
        }

        public override void Close()
        {
            SteamNetworking.CloseP2PSessionWithUser(remoteId);
        }

        public override string ToString()
        {
            return "SteamP2P to " + remoteId;
        }
    }

}
