using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Verse;

namespace Multiplayer
{
    public class Server
    {
        public delegate void NewConnection(Connection connection);

        private Socket socket;
        private List<Connection> connections = new List<Connection>();
        private NewConnection newConnection;

        public Server(IPAddress address, int port, NewConnection newConnection = null)
        {
            this.newConnection = newConnection;

            socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(address, port));
            socket.Listen(5);
            socket.NoDelay = true;

            BeginAccept();
        }

        private void BeginAccept()
        {
            this.socket.BeginAccept(OnConnectRequest, null);
        }

        private void OnConnectRequest(IAsyncResult result)
        {
            Socket client = socket.EndAccept(result);
            client.NoDelay = true;

            Connection connection = new Connection(client);
            connection.closedCallback += () =>
            {
                lock (connections)
                    connections.Remove(connection);
            };

            lock (connections)
                connections.Add(connection);

            newConnection?.Invoke(connection);

            connection.BeginReceive();

            BeginAccept();
        }

        public void Close()
        {
            socket.Close();

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

        public static byte[] GetBytes(params object[] data)
        {
            using (var stream = new MemoryStream())
            {
                foreach (object o in data)
                    stream.WriteObject(o);
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

        public abstract void Message(int id, ByteReader data);
        public abstract void Disconnected();
    }

    public class Connection
    {
        private const int HEADER_LEN = sizeof(int) * 2;
        private const int MAX_PACKET_SIZE = 4 * 1024 * 1024;

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
            get
            {
                lock (state_lock)
                    return state;
            }

            set
            {
                lock (state_lock)
                    state = value;
            }
        }

        public string username;
        public readonly Guid connId = Guid.NewGuid();

        private Socket socket;

        private ConnectionState state;
        private readonly object state_lock = new object();

        private byte[] recv = new byte[8192];
        private byte[] msg = new byte[HEADER_LEN];
        private int msgId;
        private bool hasHeader;
        private int msgPos;

        public ConnectionClosed closedCallback;

        public Connection(Socket socket)
        {
            this.socket = socket;
        }

        public void BeginReceive()
        {
            this.socket.BeginReceive(recv, 0, recv.Length, SocketFlags.None, OnReceive, null);
        }

        private void OnReceive(IAsyncResult result)
        {
            try
            {
                int recvTotal = socket.EndReceive(result);
                if (recvTotal <= 0)
                {
                    Close();
                    return;
                }

                int recvPos = 0;

                while (recvPos < recvTotal)
                {
                    // populate current msg part
                    if (msgPos < msg.Length)
                    {
                        int copy = Math.Min(msg.Length - msgPos, recvTotal - recvPos);
                        Array.Copy(recv, recvPos, msg, msgPos, copy);

                        recvPos += copy;
                        msgPos += copy;
                    }

                    // handle header
                    if (!hasHeader && msgPos == msg.Length)
                    {
                        msgId = BitConverter.ToInt32(msg, 4);
                        uint size = BitConverter.ToUInt32(msg, 0);
                        if (size > MAX_PACKET_SIZE)
                        {
                            Close();
                            return;
                        }

                        msg = new byte[size];
                        hasHeader = true;
                        msgPos = 0;
                    }

                    // handle body
                    // repeat msg pos check in case of an empty body
                    if (hasHeader && msgPos == msg.Length)
                    {
                        State?.Message(msgId, new ByteReader(msg));
                        msg = new byte[HEADER_LEN];
                        hasHeader = false;
                        msgPos = 0;
                    }
                }

                BeginReceive();
            }
            catch (SocketException)
            {
                Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public virtual void Close()
        {
            closedCallback();
            socket.Shutdown(SocketShutdown.Both);
            socket.Close();
        }

        public virtual void Send(int id)
        {
            Send(id, new byte[0]);
        }

        public virtual void Send(int id, byte[] message)
        {
            byte[] full = new byte[HEADER_LEN + message.Length];
            uint size = (uint)message.Length;

            BitConverter.GetBytes(size).CopyTo(full, 0);
            BitConverter.GetBytes(id).CopyTo(full, 4);
            message.CopyTo(full, HEADER_LEN);

            socket.BeginSend(full, 0, full.Length, SocketFlags.None, result =>
            {
                int sent = socket.EndSend(result);
                OnMainThread.Enqueue(() => Log.Message("packet sent " + id + " " + sent + "/" + full.Length));
            }, null);
        }

        public virtual void Send(int id, params object[] message)
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
                socket.NoDelay = true;
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

    public class LocalClientConnection : Connection
    {
        public LocalServerConnection server;

        public LocalClientConnection() : base(null)
        {
        }

        public override void Send(int id, byte[] msg = null)
        {
            msg = msg ?? new byte[] { 0 };
            server.State?.Message(id, new ByteReader(msg));

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
    }

    public class LocalServerConnection : Connection
    {
        public LocalClientConnection client;

        public LocalServerConnection() : base(null)
        {
        }

        public override void Send(int id, byte[] msg = null)
        {
            msg = msg ?? new byte[] { 0 };
            client.State?.Message(id, new ByteReader(msg));
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
    }

    public class ByteReader
    {
        private readonly byte[] array;

        private int index;

        public ByteReader(byte[] array)
        {
            this.array = array;
        }

        public int ReadInt()
        {
            return BitConverter.ToInt32(array, IncrementIndex(4));
        }

        public float ReadFloat()
        {
            return BitConverter.ToSingle(array, IncrementIndex(4));
        }

        public double ReadDouble()
        {
            return BitConverter.ToDouble(array, IncrementIndex(8));
        }

        public int ReadByte()
        {
            return array[IncrementIndex(1)];
        }

        public bool ReadBool()
        {
            return BitConverter.ToBoolean(array, IncrementIndex(1));
        }

        public string ReadString()
        {
            return Encoding.UTF8.GetString(ReadPrefixedBytes());
        }

        public byte[] ReadPrefixedBytes()
        {
            int len = ReadInt();
            return array.SubArray(IncrementIndex(len), len);
        }

        public int[] ReadPrefixedInts()
        {
            int len = ReadInt();
            int[] result = new int[len];
            for (int i = 0; i < len; i++)
                result[i] = ReadInt();
            return result;
        }

        public string[] ReadPrefixedStrings()
        {
            int len = ReadInt();
            string[] result = new string[len];
            for (int i = 0; i < len; i++)
                result[i] = ReadString();
            return result;
        }

        public int IncrementIndex(int val)
        {
            int i = index;
            index += val;
            if (index > array.Length)
                throw new IndexOutOfRangeException();
            return i;
        }

        public byte[] GetBytes()
        {
            return array;
        }
    }

}
