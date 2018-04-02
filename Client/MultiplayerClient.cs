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
        public static void TryConnect(IPAddress address, int port, Action<Connection> connectEvent = null, Action<Exception> failEvent = null)
        {
            Socket socket = null;
            try
            {
                socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.NoDelay = true;
                socket.BeginConnect(new IPEndPoint(address, port), result =>
                {
                    try
                    {
                        socket.EndConnect(result);

                        Connection connection = new Connection(socket);
                        connection.onMainThread = OnMainThread.Enqueue;

                        OnMainThread.Enqueue(() =>
                        {
                            connectEvent?.Invoke(connection);
                            connection.BeginReceive();
                        });
                    }
                    catch (SocketException e)
                    {
                        socket.Close();

                        OnMainThread.Enqueue(() =>
                        {
                            failEvent?.Invoke(e);
                        });
                    }
                }, null);
            }
            catch (SocketException e)
            {
                socket?.Close();
                failEvent?.Invoke(e);
            }
        }
    }

    public class LocalClientConnection : Connection
    {
        public LocalServerConnection server;

        public LocalClientConnection() : base(null)
        {
        }

        public override void Send(Enum id, byte[] msg, Action onSent = null)
        {
            msg = msg ?? new byte[] { 0 };
            server.HandleMsg(Convert.ToInt32(id), msg);

        }

        public override void Close()
        {
            closedCallback();
            server.closedCallback();
        }

        public override string ToString()
        {
            return "Local";
        }

        public override bool IsConnected()
        {
            return true;
        }
    }

    public class LocalServerConnection : Connection
    {
        public LocalClientConnection client;

        public LocalServerConnection() : base(null)
        {
        }

        public override void Send(Enum id, byte[] msg, Action onSent = null)
        {
            msg = msg ?? new byte[] { 0 };
            client.HandleMsg(Convert.ToInt32(id), msg);
        }

        public override void Close()
        {
            closedCallback();
            client.closedCallback();
        }

        public override string ToString()
        {
            return "Local";
        }

        public override bool IsConnected()
        {
            return true;
        }
    }
}
