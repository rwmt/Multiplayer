using LiteNetLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Multiplayer.Common
{
    public abstract class MpConnectionState
    {
        public readonly IConnection connection;

        public MpConnectionState(IConnection connection)
        {
            this.connection = connection;
        }

        public static Type[] connectionImpls = new Type[(int)ConnectionStateEnum.Count];
        public static MethodBase[,] packetHandlers = new MethodBase[(int)ConnectionStateEnum.Count, (int)Packets.Count];

        public static void SetImplementation(ConnectionStateEnum state, Type type)
        {
            if (!type.IsSubclassOf(typeof(MpConnectionState))) return;

            connectionImpls[(int)state] = type;

            foreach (MethodBase method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                PacketHandlerAttribute attr = (PacketHandlerAttribute)Attribute.GetCustomAttribute(method, typeof(PacketHandlerAttribute));
                if (attr == null)
                    continue;

                if (method.GetParameters().Length != 1 || method.GetParameters()[0].ParameterType != typeof(ByteReader))
                    continue;

                packetHandlers[(int)state, (int)attr.packet] = method;
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
        public string username;
        public ServerPlayer serverPlayer;

        public virtual int Latency { get; set; }

        public ConnectionStateEnum State
        {
            get => state;

            set
            {
                state = value;

                if (state == ConnectionStateEnum.Disconnected)
                {
                    stateObj = null;
                }
                else
                {
                    stateObj = (MpConnectionState)Activator.CreateInstance(MpConnectionState.connectionImpls[(int)value], this);
                }
            }
        }

        public MpConnectionState StateObj => stateObj;

        private ConnectionStateEnum state;
        private MpConnectionState stateObj;

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

        public virtual void HandleReceive(byte[] rawData)
        {
            if (state == ConnectionStateEnum.Disconnected)
                return;

            if (rawData.Length == 0)
                throw new Exception("No packet id");

            ByteReader reader = new ByteReader(rawData);
            byte msgId = reader.ReadByte();

            if (msgId < 0 || msgId >= MpConnectionState.packetHandlers.Length)
                throw new Exception($"Bad packet id {msgId}");

            Packets packetType = (Packets)msgId;
            MethodBase handler = MpConnectionState.packetHandlers[(int)state, msgId];
            if (handler == null)
                throw new Exception($"No handler for packet {packetType} in state {state}");

            handler.Invoke(stateObj, new object[] { reader });
        }

        public abstract void Close();
    }

    public class MpNetConnection : IConnection
    {
        public readonly NetPeer peer;

        public MpNetConnection(NetPeer peer)
        {
            this.peer = peer;
        }

        public override void SendRaw(byte[] raw)
        {
            peer.Send(raw, DeliveryMethod.ReliableOrdered);
        }

        public override void Close()
        {
            peer.NetManager.DisconnectPeer(peer);
        }

        public override string ToString()
        {
            return $"NetConnection ({peer.EndPoint}) ({username})";
        }
    }

    public class ByteWriter
    {
        private MemoryStream stream = new MemoryStream();

        public object context;

        public virtual void WriteByte(byte val) => stream.WriteByte(val);

        public virtual void WriteSByte(sbyte val) => stream.WriteByte((byte)val);

        public virtual void WriteShort(short val) => stream.Write(BitConverter.GetBytes(val));

        public virtual void WriteUShort(ushort val) => stream.Write(BitConverter.GetBytes(val));

        public virtual void WriteInt32(int val) => stream.Write(BitConverter.GetBytes(val));

        public virtual void WriteUInt32(uint val) => stream.Write(BitConverter.GetBytes(val));

        public virtual void WriteLong(long val) => stream.Write(BitConverter.GetBytes(val));

        public virtual void WriteULong(ulong val) => stream.Write(BitConverter.GetBytes(val));

        public virtual void WriteFloat(float val) => stream.Write(BitConverter.GetBytes(val));

        public virtual void WriteDouble(double val) => stream.Write(BitConverter.GetBytes(val));

        public virtual void WriteBool(bool val) => stream.WriteByte(val ? (byte)1 : (byte)0);

        public virtual void WritePrefixedBytes(byte[] bytes)
        {
            WriteInt32(bytes.Length);
            stream.Write(bytes);
        }

        public virtual void WriteRaw(byte[] bytes)
        {
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
                WriteUShort(@ushort);
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

        public byte ReadByte() => array[IncrementIndex(1)];

        public sbyte ReadSByte() => (sbyte)array[IncrementIndex(1)];

        public short ReadShort() => BitConverter.ToInt16(array, IncrementIndex(2));

        public ushort ReadUShort() => BitConverter.ToUInt16(array, IncrementIndex(2));

        public int ReadInt32() => BitConverter.ToInt32(array, IncrementIndex(4));

        public uint ReadUInt32() => BitConverter.ToUInt32(array, IncrementIndex(4));

        public long ReadLong() => BitConverter.ToInt64(array, IncrementIndex(8));

        public ulong ReadULong() => BitConverter.ToUInt64(array, IncrementIndex(8));

        public float ReadFloat() => BitConverter.ToSingle(array, IncrementIndex(4));

        public double ReadDouble() => BitConverter.ToDouble(array, IncrementIndex(8));

        public bool ReadBool() => BitConverter.ToBoolean(array, IncrementIndex(1));

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
