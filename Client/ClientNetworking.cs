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
                IConnection conn = new MpNetConnection(peer);
                conn.username = Multiplayer.username;
                conn.State = ConnectionStateEnum.ClientJoining;
                Multiplayer.session.client = conn;

                ConnectionStatusListeners.All.Do(a => a.Connected());

                MpLog.Log("Client connected");
            };

            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                string reason = DisconnectReasonString(info.Reason);
                if (info.SocketErrorCode != SocketError.Success)
                    reason += ": " + info.SocketErrorCode;

                Multiplayer.session.disconnectNetReason = reason;

                ConnectionStatusListeners.All.Do(a => a.Disconnected());

                OnMainThread.StopMultiplayer();
                MpLog.Log("Client disconnected");
            };

            listener.NetworkReceiveEvent += (peer, reader, method) =>
            {
                byte[] data = reader.GetRemainingBytes();
                Multiplayer.HandleReceive(data);
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
            this.username = username;
        }

        public override void SendRaw(byte[] raw)
        {
            OnMainThread.Enqueue(() =>
            {
                try
                {
                    server.HandleReceive(raw);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception handling packet by {this}: {e}");
                }
            });
        }

        public override void Close()
        {
        }

        public override string ToString()
        {
            return "LocalClientConn";
        }
    }

    public class LocalServerConnection : IConnection
    {
        public LocalClientConnection client;

        public LocalServerConnection(string username)
        {
            this.username = username;
        }

        public override void SendRaw(byte[] raw)
        {
            OnMainThread.Enqueue(() =>
            {
                try
                {
                    client.HandleReceive(raw);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception handling packet by {this}: {e}");
                }
            });
        }

        public override void Close()
        {
        }

        public override string ToString()
        {
            return "LocalServerConn";
        }
    }

    public class SteamConnection : IConnection
    {
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
            return $"SteamP2P ({remoteId}) ({username})";
        }
    }

}
