using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace Multiplayer.Common
{
    public class NetworkServer
    {
        public delegate void NewConnection(Connection connection);

        private Socket socket;
        private List<Connection> connections = new List<Connection>();
        private NewConnection newConnection;

        public NetworkServer(IPAddress address, int port, NewConnection newConnection = null)
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

        public void SendToAll(Enum id, byte[] data = null, params Connection[] except)
        {
            lock (connections)
                foreach (Connection conn in connections)
                    if (!except.Contains(conn))
                        conn.Send(id, data);
        }

        public void SendToAll(Enum id, object[] data, params Connection[] except)
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

        public abstract void Disconnected();
    }

    public class Connection
    {
        private const int HEADER_LEN = sizeof(int) * 2;
        private const int MAX_PACKET_SIZE = 4 * 1024 * 1024;

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

        public Action<Action> onMainThread;
        public Action closedCallback;

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
                        if (!HandleMsg(msgId, msg))
                        {
                            Close();
                            return;
                        }

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

        public virtual void Send(Enum id)
        {
            Send(id, new byte[0]);
        }

        public virtual void Send(Enum id, byte[] message)
        {
            byte[] full = new byte[HEADER_LEN + message.Length];
            uint size = (uint)message.Length;

            BitConverter.GetBytes(size).CopyTo(full, 0);
            BitConverter.GetBytes(Convert.ToInt32(id)).CopyTo(full, 4);
            message.CopyTo(full, HEADER_LEN);

            socket.BeginSend(full, 0, full.Length, SocketFlags.None, result =>
            {
                socket.EndSend(result);
            }, null);
        }

        public virtual void Send(Enum id, params object[] message)
        {
            Send(id, NetworkServer.GetBytes(message));
        }

        public override string ToString()
        {
            return username;
        }

        public bool HandleMsg(int msgId, byte[] msg)
        {
            if (!statePacketHandlers.TryGetValue(State.GetType(), out Dictionary<int, List<PacketHandler>> packets))
                return false;

            if (!packets.TryGetValue(msgId, out List<PacketHandler> handlers))
                return false;

            ByteReader reader = new ByteReader(msg);
            foreach (PacketHandler handler in handlers)
            {
                if (handler.mainThread)
                {
                    onMainThread(() => handler.handler.Invoke(State, new object[] { reader }));
                }
                else
                {
                    handler.handler.Invoke(State, new object[] { reader });
                }

                reader.Reset();
            }

            return true;
        }

        public static Dictionary<Type, Dictionary<int, List<PacketHandler>>> statePacketHandlers = new Dictionary<Type, Dictionary<int, List<PacketHandler>>>();

        public static void RegisterState(Type type)
        {
            if (!(type.IsSubclassOf(typeof(ConnectionState)))) return;

            foreach (MethodInfo method in type.GetMethods())
            {
                PacketHandlerAttribute attr = (PacketHandlerAttribute)Attribute.GetCustomAttribute(method, typeof(PacketHandlerAttribute));
                if (attr == null)
                    continue;

                if (!(attr.packet is Enum))
                {
                    MpLog.Log("Packet not enum!");
                    continue;
                }

                Type actionType = typeof(Action<,>).MakeGenericType(type, typeof(ByteReader));
                if (Delegate.CreateDelegate(actionType, null, method, false) == null)
                {
                    MpLog.Log("Wrong method signature! " + method.Name);
                    continue;
                }

                bool mainThread = Attribute.GetCustomAttribute(method, typeof(HandleImmediatelyAttribute)) == null;

                statePacketHandlers.AddOrGet(type, new Dictionary<int, List<PacketHandler>>()).AddOrGet((int)attr.packet, new List<PacketHandler>()).Add(new PacketHandler(method, mainThread));
            }
        }

        public class PacketHandler
        {
            public readonly MethodBase handler;
            public readonly bool mainThread;

            public PacketHandler(MethodBase handler, bool mainThread)
            {
                this.handler = handler;
                this.mainThread = mainThread;
            }
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

        public void Reset()
        {
            index = 0;
        }
    }

}
