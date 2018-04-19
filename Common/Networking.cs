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

    public class ByteWriter
    {
        private MemoryStream stream = new MemoryStream();
        public object context;

        public void WriteInt32(int val)
        {
            stream.Write(BitConverter.GetBytes(val));
        }

        public void WriteUInt16(ushort val)
        {
            stream.Write(BitConverter.GetBytes(val));
        }

        public void WriteBool(bool val)
        {
            stream.WriteByte(val ? (byte)1 : (byte)0);
        }

        public void WriteList<T>(List<T> list)
        {
            WriteInt32(list.Count);
            foreach (T t in list)
                Write(t);
        }

        public void Write(object obj)
        {
            if (obj is int @int)
            {
                WriteInt32(@int);
            }
            else if (obj is ushort @ushort)
            {
                WriteUInt16(@ushort);
            }
            else if (obj is bool @bool)
            {
                WriteBool(@bool);
            }
            else if (obj is byte @byte)
            {
                stream.WriteByte(@byte);
            }
            else if (obj is float @float)
            {
                stream.Write(BitConverter.GetBytes(@float));
            }
            else if (obj is double @double)
            {
                stream.Write(BitConverter.GetBytes(@double));
            }
            else if (obj is byte[] bytearr)
            {
                WriteInt32(bytearr.Length);
                stream.Write(bytearr);
            }
            else if (obj is Enum)
            {
                Write(Convert.ToInt32(obj));
            }
            else if (obj is string @string)
            {
                Write(Encoding.UTF8.GetBytes(@string));
            }
            else if (obj is Array arr)
            {
                Write(arr.Length);
                foreach (object o in arr)
                    Write(o);
            }
        }

        public byte[] GetArray()
        {
            return stream.ToArray();
        }

        public static byte[] GetBytes(params object[] data)
        {
            var writer = new ByteWriter();
            foreach (object o in data)
                writer.Write(o);
            return writer.GetArray();
        }
    }

    public class ByteReader
    {
        private readonly byte[] array;
        private int index;
        public object context;

        public ByteReader(byte[] array)
        {
            this.array = array;
        }

        public int ReadInt32()
        {
            return BitConverter.ToInt32(array, IncrementIndex(4));
        }

        public ushort ReadUInt16()
        {
            return BitConverter.ToUInt16(array, IncrementIndex(2));
        }

        public float ReadFloat()
        {
            return BitConverter.ToSingle(array, IncrementIndex(4));
        }

        public double ReadDouble()
        {
            return BitConverter.ToDouble(array, IncrementIndex(8));
        }

        public byte ReadByte()
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
            int len = ReadInt32();
            if (len < 0)
                throw new IOException("Byte array length less than 0");
            HasSizeLeft(len);
            return array.SubArray(IncrementIndex(len), len);
        }

        public int[] ReadPrefixedInts()
        {
            int len = ReadInt32();
            if (len < 0)
                throw new IOException("Int array length less than 0");
            HasSizeLeft(len * 4);
            int[] result = new int[len];
            for (int i = 0; i < len; i++)
                result[i] = ReadInt32();
            return result;
        }

        public string[] ReadPrefixedStrings()
        {
            int len = ReadInt32();
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
