using LiteNetLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Multiplayer.Common
{
    public abstract class MpConnectionState
    {
        public IConnection Connection
        {
            get;
            private set;
        }

        public MpConnectionState(IConnection connection)
        {
            Connection = connection;
        }

        public static Dictionary<Type, Dictionary<Packets, PacketHandler>> statePacketHandlers = new Dictionary<Type, Dictionary<Packets, PacketHandler>>();

        public static void RegisterState(Type type)
        {
            if (!type.IsSubclassOf(typeof(MpConnectionState))) return;

            foreach (MethodInfo method in type.GetMethods())
            {
                PacketHandlerAttribute attr = (PacketHandlerAttribute)Attribute.GetCustomAttribute(method, typeof(PacketHandlerAttribute));
                if (attr == null)
                    continue;

                Type actionType = typeof(Action<,>).MakeGenericType(type, typeof(ByteReader));
                if (Delegate.CreateDelegate(actionType, null, method, false) == null)
                {
                    MpLog.Log("Wrong method signature! " + method.Name);
                    continue;
                }

                statePacketHandlers.AddOrGet(type, new Dictionary<Packets, PacketHandler>())[attr.packet] = new PacketHandler(method);
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

    public class PacketHandlerAttribute : Attribute
    {
        public readonly Packets packet;

        public PacketHandlerAttribute(Packets packet)
        {
            this.packet = packet;
        }
    }

    public abstract class IConnection
    {
        public abstract string Username { get; set; }
        public abstract int Latency { get; set; }
        public abstract MpConnectionState State { get; set; }

        public virtual void Send(Packets id)
        {
            Send(id, new byte[0]);
        }

        public virtual void Send(Packets id, params object[] msg)
        {
            Send(id, ByteWriter.GetBytes(msg));
        }

        public virtual void Send(Packets id, byte[] message)
        {
            byte[] full = new byte[1 + message.Length];
            full[0] = Convert.ToByte(id);
            message.CopyTo(full, 1);

            SendRaw(full);
        }

        public abstract void SendRaw(byte[] raw);

        public virtual void HandleReceive(byte[] data)
        {
            HandleRaw(data);
        }

        public abstract void Close();

        public bool HandleRaw(byte[] rawData)
        {
            if (!MpConnectionState.statePacketHandlers.TryGetValue(State.GetType(), out var packets))
            {
                MpLog.Log("Unregistered network state!");
                return false;
            }

            ByteReader reader = new ByteReader(rawData);
            Packets msgId = (Packets)reader.ReadByte();

            if (!packets.TryGetValue(msgId, out MpConnectionState.PacketHandler handler))
            {
                MpLog.Log("Packet has no handler! ({0})", msgId);
                return true;
            }

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

    public class MpNetConnection : IConnection
    {
        public override string Username { get; set; }
        public override int Latency { get; set; }
        public override MpConnectionState State { get; set; }

        private readonly NetPeer peer;

        public MpNetConnection(NetPeer peer)
        {
            this.peer = peer;
        }

        public override void SendRaw(byte[] raw)
        {
            peer.Send(raw, SendOptions.ReliableOrdered);
        }

        public override void Close()
        {
            peer.NetManager.DisconnectPeer(peer);
        }

        public override string ToString()
        {
            return "NetConnection " + peer.EndPoint;
        }
    }

    public class ByteWriter
    {
        private MemoryStream stream = new MemoryStream();

        public object context;

        public virtual void WriteInt32(int val)
        {
            stream.Write(BitConverter.GetBytes(val));
        }

        public virtual void WriteUInt16(ushort val)
        {
            stream.Write(BitConverter.GetBytes(val));
        }

        public virtual void WriteFloat(float val)
        {
            stream.Write(BitConverter.GetBytes(val));
        }

        public virtual void WriteDouble(double val)
        {
            stream.Write(BitConverter.GetBytes(val));
        }

        public virtual void WriteLong(long val)
        {
            stream.Write(BitConverter.GetBytes(val));
        }

        public virtual void WriteBool(bool val)
        {
            stream.WriteByte(val ? (byte)1 : (byte)0);
        }

        public virtual void WriteByte(byte val)
        {
            stream.WriteByte(val);
        }

        public virtual void WritePrefixedBytes(byte[] bytes)
        {
            WriteInt32(bytes.Length);
            stream.Write(bytes);
        }

        public virtual void WriteByteArrayList(List<byte[]> list)
        {
            WriteInt32(list.Count);
            foreach (byte[] arr in list)
                WritePrefixedBytes(arr);
        }

        public virtual ByteWriter WriteString(string s)
        {
            WritePrefixedBytes(Encoding.UTF8.GetBytes(s));
            return this;
        }

        private void Write(object obj)
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
            else if (obj is long @long)
            {
                WriteLong(@long);
            }
            else if (obj is byte @byte)
            {
                WriteByte(@byte);
            }
            else if (obj is float @float)
            {
                WriteFloat(@float);
            }
            else if (obj is double @double)
            {
                WriteDouble(@double);
            }
            else if (obj is byte[] bytes)
            {
                WritePrefixedBytes(bytes);
            }
            else if (obj is Enum)
            {
                Write(Convert.ToInt32(obj));
            }
            else if (obj is string @string)
            {
                WriteString(@string);
            }
            else if (obj is Array arr)
            {
                Write(arr.Length);
                foreach (object o in arr)
                    Write(o);
            }
            else if (obj is IList list)
            {
                Write(list.Count);
                foreach (object o in list)
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

        public long ReadLong()
        {
            return BitConverter.ToInt64(array, IncrementIndex(8));
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
            int bytes = ReadInt32();
            string result = Encoding.UTF8.GetString(array, index, bytes);
            index += bytes;
            return result;
        }

        public byte[] ReadPrefixedBytes()
        {
            int len = ReadInt32();
            return array.SubArray(IncrementIndex(len), len);
        }

        public int[] ReadPrefixedInts()
        {
            int len = ReadInt32();
            int[] result = new int[len];
            for (int i = 0; i < len; i++)
                result[i] = ReadInt32();
            return result;
        }

        public string[] ReadPrefixedStrings()
        {
            int len = ReadInt32();
            string[] result = new string[len];
            for (int i = 0; i < len; i++)
                result[i] = ReadString();
            return result;
        }

        private int IncrementIndex(int size)
        {
            int i = index;
            index += size;
            return i;
        }

        public byte[] GetBytes()
        {
            return array;
        }
    }

}
