using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Verse;

namespace ServerMod
{
    public class Server
    {
        public delegate void NewConnection(Connection connection);

        private Socket socket;
        private List<Connection> connections = new List<Connection>();
        private NewConnection newConnection;

        public Server(IPAddress address, int port, NewConnection newConnection = null)
        {
            this.socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            this.socket.Bind(new IPEndPoint(address, port));
            this.socket.Listen(5);
            this.newConnection = newConnection;
            BeginAccept();
        }

        private void BeginAccept()
        {
            this.socket.BeginAccept(OnConnectRequest, null);
        }

        private void OnConnectRequest(IAsyncResult result)
        {
            Socket client = this.socket.EndAccept(result);

            Connection connection = new Connection(client);
            connection.connectionClosed += delegate ()
            {
                lock (connections)
                    connections.Remove(connection);
            };

            lock (connections)
                connections.Add(connection);

            this.newConnection?.Invoke(connection);

            connection.BeginReceive();

            BeginAccept();
        }

        public void Close()
        {
            this.socket.Close();

            lock (connections)
                foreach (Connection conn in connections)
                    conn.Close();
        }

        public List<Connection> GetConnections()
        {
            return connections;
        }

        public void SendToAll(int id, byte[] data = null, params Connection[] except)
        {
            lock (connections)
                foreach (Connection conn in connections)
                    if (!except.Contains(conn))
                        conn.Send(id, data);
        }

        public void SendToAll(int id, object[] data, params Connection[] except)
        {
            SendToAll(id, GetBytes(data), except);
        }

        public Connection GetByUsername(string username)
        {
            lock (connections)
            {
                return connections.FirstOrDefault(c => c.username == username);
            }
        }

        public static byte[] GetBytes(object[] data)
        {
            using (var stream = new MemoryStream())
            {
                foreach (object obj in data)
                {
                    if (obj == null) continue;

                    if (obj is string)
                        stream.Write(((string)obj).GetBytes());
                    else if (obj is byte[])
                        stream.Write((byte[])obj);
                    else if (obj is int)
                        stream.Write(BitConverter.GetBytes((int)obj));
                    else if (obj is Enum)
                        stream.Write(BitConverter.GetBytes((int)obj));
                }

                return stream.ToArray();
            }
        }
    }

    public abstract class ConnectionState
    {
        public Connection Connection
        {
            get;
            private set;
        }

        public ConnectionState(Connection connection)
        {
            this.Connection = connection;
        }

        public abstract void Message(int id, byte[] data);
        public abstract void Disconnect();
    }

    public class Connection
    {
        public delegate void ConnectionClosed();

        public EndPoint RemoteEndPoint
        {
            get
            {
                return socket.Connected ? socket.RemoteEndPoint : null;
            }
        }

        public ConnectionState State
        {
            get;
            set;
        }

        public string username;
        public readonly Guid connId = Guid.NewGuid();

        private Socket socket;

        private byte[] buffer = new byte[8192];

        private byte[] prefix = new byte[sizeof(int) * 2];
        private byte[] msg;
        private int fullPos;

        public ConnectionClosed connectionClosed;

        public Connection(Socket socket)
        {
            this.socket = socket;
        }

        public void BeginReceive()
        {
            this.socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, OnReceive, null);
        }

        private void OnReceive(IAsyncResult result)
        {
            try
            {
                int recvBytes = this.socket.EndReceive(result);
                if (recvBytes <= 0)
                {
                    connectionClosed();
                    Close();
                    return;
                }

                int toRead = recvBytes;
                while (toRead > 0)
                {
                    int bufferPos = (recvBytes - toRead);

                    if (fullPos < prefix.Length)
                    {
                        int copy = Math.Min(prefix.Length - fullPos, toRead);
                        Array.Copy(buffer, bufferPos, prefix, fullPos, copy);

                        toRead -= copy;
                        fullPos += copy;
                    }
                    else if (msg == null || fullPos - prefix.Length < msg.Length)
                    {
                        if (msg == null)
                            msg = new byte[BitConverter.ToInt32(prefix, 0)];

                        int copy = Math.Min(msg.Length - (fullPos - prefix.Length), toRead);
                        Array.Copy(buffer, bufferPos, msg, fullPos - prefix.Length, copy);

                        toRead -= copy;
                        fullPos += copy;

                        if (fullPos - prefix.Length == msg.Length)
                        {
                            State?.Message(BitConverter.ToInt32(prefix, 4), msg);
                            msg = null;
                            fullPos = 0;
                        }
                    }
                }

                BeginReceive();
            }
            catch (SocketException e)
            {
                connectionClosed();
                Close();
            }
            catch (ObjectDisposedException e)
            {
            }
        }

        public virtual void Close()
        {
            this.socket.Shutdown(SocketShutdown.Both);
            this.socket.Close();
        }

        public virtual void Send(int id, byte[] message = null)
        {
            message = message ?? new byte[] { 0 };

            byte[] full = new byte[message.Length + prefix.Length];

            Array.Copy(BitConverter.GetBytes(message.Length), 0, full, 0, 4);
            Array.Copy(BitConverter.GetBytes(id), 0, full, 4, 4);
            Array.Copy(message, 0, full, prefix.Length, message.Length);

            this.socket.BeginSend(full, 0, full.Length, SocketFlags.None, result => this.socket.EndSend(result), null);
        }

        public virtual void Send(int id, object[] message)
        {
            Send(id, Server.GetBytes(message));
        }

        public override string ToString()
        {
            return username;
        }
    }

    public static class Client
    {
        public delegate void ConnectEvent(Connection connection, Exception e);

        public static void TryConnect(IPAddress address, int port, ConnectEvent connectEvent = null)
        {
            Socket socket = null;
            try
            {
                socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.BeginConnect(new IPEndPoint(address, port), result =>
                {
                    try
                    {
                        socket.EndConnect(result);
                        Connection connection = new Connection(socket);
                        connectEvent?.Invoke(connection, null);
                        connection.BeginReceive();
                    }
                    catch (SocketException e)
                    {
                        socket.Close();
                        connectEvent?.Invoke(null, e);
                    }
                }, null);
            }
            catch (SocketException e)
            {
                socket?.Close();
                connectEvent?.Invoke(null, e);
            }
        }
    }
}
