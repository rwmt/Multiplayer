using LiteNetLib;
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
    public abstract class MultiplayerConnectionState
    {
        public IConnection Connection
        {
            get;
            private set;
        }

        public MultiplayerConnectionState(IConnection connection)
        {
            Connection = connection;
        }

        public abstract void Disconnected(string reason);

        public static Dictionary<Type, Dictionary<int, PacketHandler>> statePacketHandlers = new Dictionary<Type, Dictionary<int, PacketHandler>>();

        public static void RegisterState(Type type)
        {
            if (!(type.IsSubclassOf(typeof(MultiplayerConnectionState)))) return;

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

                statePacketHandlers.AddOrGet(type, new Dictionary<int, PacketHandler>())[(int)attr.packet] = new PacketHandler(method);
            }
        }

        public class PacketHandler
        {
            public readonly MethodBase handler;

            public PacketHandler(MethodBase handler)
            {
                this.handler = handler;
            }
        }
    }

    public abstract class IConnection
    {
        public abstract string Username { get; set; }
        public abstract int Latency { get; set; }
        public abstract MultiplayerConnectionState State { get; set; }

        public abstract void Send(Enum id);

        public abstract void Send(Enum id, params object[] msg);

        public abstract void Send(Enum id, byte[] message);

        public abstract void HandleReceive(byte[] data);

        public abstract void Close(string reason);

        public bool HandleMsg(byte[] rawData)
        {
            int msgId = BitConverter.ToInt32(rawData, 0);
            byte[] msg = rawData.SubArray(4, rawData.Length - 4);

            if (!MultiplayerConnectionState.statePacketHandlers.TryGetValue(State.GetType(), out Dictionary<int, MultiplayerConnectionState.PacketHandler> packets))
            {
                MpLog.Log("Unregistered network state!");
                return false;
            }

            if (!packets.TryGetValue(msgId, out MultiplayerConnectionState.PacketHandler handler))
            {
                MpLog.Log("Packet has no handler! ({0})", msgId);
                return true;
            }

            ByteReader reader = new ByteReader(msg);

            try
            {
                handler.handler.Invoke(State, new object[] { reader });
            }
            catch (Exception e)
            {
                MpLog.Log("Exception while handling message {0} by {1}\n{2}", msgId, handler.handler, e.ToString());
            }

            return true;
        }
    }

    public class MultiplayerConnection : IConnection
    {
        public override string Username { get; set; }
        public override int Latency { get; set; }
        public override MultiplayerConnectionState State { get; set; }

        private readonly NetPeer peer;

        public MultiplayerConnection(NetPeer peer)
        {
            this.peer = peer;
        }

        public override void HandleReceive(byte[] data)
        {
            HandleMsg(data);
        }

        public override void Send(Enum id, byte[] message)
        {
            byte[] full = new byte[4 + message.Length];
            BitConverter.GetBytes(Convert.ToInt32(id)).CopyTo(full, 0);
            message.CopyTo(full, 4);

            peer.Send(full, SendOptions.ReliableOrdered);
        }

        public override void Send(Enum id)
        {
            Send(id, new byte[0]);
        }

        public override void Send(Enum id, object[] msg)
        {
            Send(id, ByteWriter.GetBytes(msg));
        }

        public override void Close(string reason)
        {
            peer.NetManager.DisconnectPeer(peer, ByteWriter.GetBytes(reason));
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
