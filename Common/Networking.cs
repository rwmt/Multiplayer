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
        private Socket socket;
        private List<Connection> connections = new List<Connection>();
        private Action<Connection> newConnection;

        public NetworkServer(IPAddress address, int port, Action<Connection> newConnection = null)
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
                connection.State = null;

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

        public int GetConnectionCount()
        {
            lock (connections)
                return connections.Count;
        }

        public void SendToAll(Enum id, byte[] data, params Connection[] except)
        {
            lock (connections)
                foreach (Connection conn in connections)
                    if (conn.IsConnected() && !except.Contains(conn))
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

        public virtual bool IsConnected()
        {
            return socket.Connected;
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
            closedCallback?.Invoke();
            socket.Shutdown(SocketShutdown.Both);
            socket.Close();
        }

        public virtual void Send(Enum id)
        {
            Send(id, new byte[0]);
        }

        public virtual void Send(Enum id, byte[] message, Action onSent = null)
        {
            byte[] full = new byte[HEADER_LEN + message.Length];
            uint size = (uint)message.Length;

            BitConverter.GetBytes(size).CopyTo(full, 0);
            BitConverter.GetBytes(Convert.ToInt32(id)).CopyTo(full, 4);
            message.CopyTo(full, HEADER_LEN);

            socket.BeginSend(full, 0, full.Length, SocketFlags.None, result =>
            {
                socket.EndSend(result);
                onSent?.Invoke();
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
            if (State == null)
            {
                MpLog.Log("Null connection state for connection " + username);
                return true;
            }

            if (!statePacketHandlers.TryGetValue(State.GetType(), out Dictionary<int, PacketHandler> packets))
            {
                MpLog.Log("Unregistered network state!");
                return false;
            }

            if (!packets.TryGetValue(msgId, out PacketHandler handler))
            {
                MpLog.Log("Packet has no handler! ({0})", msgId);
                return true;
            }

            ByteReader reader = new ByteReader(msg);
            ConnectionState state = State;

            Action action = () =>
            {
                if (state == null) return;

                try
                {
                    handler.handler.Invoke(state, new object[] { reader });
                }
                catch (Exception e)
                {
                    onMainThread(() => MpLog.Log("Exception while handling message {0} by {1}\n{2}", msgId, handler.handler, e.ToString()));
                }
            };

            if (handler.mainThread)
                onMainThread(action);
            else
                action();

            return true;
        }

        public static Dictionary<Type, Dictionary<int, PacketHandler>> statePacketHandlers = new Dictionary<Type, Dictionary<int, PacketHandler>>();

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

                statePacketHandlers.AddOrGet(type, new Dictionary<int, PacketHandler>())[(int)attr.packet] = new PacketHandler(method, mainThread);
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
            if (len < 0)
                throw new IOException("Byte array length less than 0");
            HasSizeLeft(len);
            return array.SubArray(IncrementIndex(len), len);
        }

        public int[] ReadPrefixedInts()
        {
            int len = ReadInt();
            if (len < 0)
                throw new IOException("Int array length less than 0");
            HasSizeLeft(len * 4);
            int[] result = new int[len];
            for (int i = 0; i < len; i++)
                result[i] = ReadInt();
            return result;
        }

        public string[] ReadPrefixedStrings()
        {
            int len = ReadInt();
            if (len < 0)
                throw new IOException("String array length less than 0");
            string[] result = new string[len];
            for (int i = 0; i < len; i++)
                result[i] = ReadString();
            return result;
        }

        private int IncrementIndex(int size)
        {
            HasSizeLeft(size);
            int i = index;
            index += size;
            return i;
        }

        public void HasSizeLeft(int size)
        {
            if (index + size > array.Length)
                throw new IndexOutOfRangeException();
        }

        public byte[] GetBytes()
        {
            return array;
        }
    }

}
