using LiteNetLib;
using Multiplayer;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Multiplayer.Client
{
    public static class Client
    {
        public static void TryConnect(IPAddress address, int port, Action<IConnection> connectEvent = null, Action<string> failEvent = null)
        {
            EventBasedNetListener listener = new EventBasedNetListener();
            Multiplayer.netClient = new NetManager(listener, "");
            Multiplayer.netClient.Start();

            Multiplayer.netClient.ReconnectDelay = 300;
            Multiplayer.netClient.MaxConnectAttempts = 8;

            listener.PeerConnectedEvent += peer =>
            {
                peer.Tag = new MultiplayerConnection(peer);
                OnMainThread.Enqueue(() => connectEvent(peer.GetConnection()));
            };

            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                string reason;
                if (info.Reason == DisconnectReason.RemoteConnectionClose || info.Reason == DisconnectReason.DisconnectPeerCalled)
                {
                    reason = info.AdditionalData.GetString();
                }
                else
                {
                    reason = info.Reason + ": " + info.SocketErrorCode;
                }

                Multiplayer.netClient.Stop();
                Multiplayer.netClient = null;

                OnMainThread.Enqueue(() => failEvent(reason));
            };

            listener.NetworkReceiveEvent += (peer, reader) =>
            {
                byte[] data = reader.Data;
                OnMainThread.Enqueue(() => peer.GetConnection().HandleReceive(data));
            };

            Multiplayer.netClient.Connect(address.ToString(), port);
        }
    }

    public class LocalClientConnection : IConnection
    {
        public LocalServerConnection server;

        public override string Username { get; set; }
        public override int Latency { get; set; }
        public override MultiplayerConnectionState State { get; set; }

        public override void Send(Enum id)
        {
            Send(id, new byte[0]);
        }

        public override void Send(Enum id, params object[] msg)
        {
            Send(id, ByteWriter.GetBytes(msg));
        }

        public override void Send(Enum id, byte[] message)
        {
            byte[] full = new byte[4 + message.Length];
            BitConverter.GetBytes(Convert.ToInt32(id)).CopyTo(full, 0);
            message.CopyTo(full, 4);

            server.HandleReceive(full);
        }

        public override void HandleReceive(byte[] data)
        {
            HandleMsg(data);
        }

        public override void Close(string reason)
        {
        }
    }

    public class LocalServerConnection : IConnection
    {
        public LocalClientConnection client;

        public override string Username { get; set; }
        public override int Latency { get; set; }
        public override MultiplayerConnectionState State { get; set; }

        public override void Send(Enum id)
        {
            Send(id, new byte[0]);
        }

        public override void Send(Enum id, params object[] msg)
        {
            Send(id, ByteWriter.GetBytes(msg));
        }

        public override void Send(Enum id, byte[] message)
        {
            byte[] full = new byte[4 + message.Length];
            BitConverter.GetBytes(Convert.ToInt32(id)).CopyTo(full, 0);
            message.CopyTo(full, 4);

            client.HandleReceive(full);
        }

        public override void HandleReceive(byte[] data)
        {
            HandleMsg(data);
        }

        public override void Close(string reason)
        {
        }
    }
}
