using LiteNetLib;
using Multiplayer.Common;
using Steamworks;
using System;
using System.Net;
using Verse;

namespace Multiplayer.Client
{
    public static class ClientUtil
    {
        public static void TryConnect(IPAddress address, int port, Action<IConnection> connectEvent = null, Action<string> failEvent = null)
        {
            EventBasedNetListener listener = new EventBasedNetListener();

            Multiplayer.session = new MultiplayerSession();
            NetManager netClient = new NetManager(listener, "");
            netClient.Start();
            netClient.ReconnectDelay = 300;
            netClient.MaxConnectAttempts = 8;

            connectEvent += conn =>
            {
                conn.Username = Multiplayer.username;
                conn.State = new ClientJoiningState(conn);
                Multiplayer.session.client = conn;
            };

            failEvent += reason =>
            {
                OnMainThread.StopMultiplayer();
            };

            listener.PeerConnectedEvent += peer =>
            {
                peer.Tag = new MpNetConnection(peer);
                OnMainThread.Enqueue(() => connectEvent(peer.GetConnection()));
            };

            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                string reason = "Disconnected";

                if (info.Reason == DisconnectReason.SocketSendError || 
                    info.Reason == DisconnectReason.SocketReceiveError ||
                    info.Reason == DisconnectReason.ConnectionFailed
                )
                    reason = info.Reason + ": " + info.SocketErrorCode;

                OnMainThread.Enqueue(() => failEvent(reason));
            };

            listener.NetworkReceiveEvent += (peer, reader) =>
            {
                byte[] data = reader.Data;
                OnMainThread.Enqueue(() => peer.GetConnection().HandleReceive(data));
            };

            Multiplayer.session.netClient = netClient;
            netClient.Connect(address.ToString(), port);
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
