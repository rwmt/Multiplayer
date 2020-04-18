using LiteNetLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Verse;

namespace Multiplayer.Common
{
    public abstract class MpConnectionState
    {
        public readonly IConnection connection;

        protected ServerPlayer Player => connection.serverPlayer;
        protected MultiplayerServer Server => MultiplayerServer.instance;

        public MpConnectionState(IConnection connection)
        {
            this.connection = connection;
        }

        public static Type[] connectionImpls = new Type[(int)ConnectionStateEnum.Count];
        public static PacketHandlerInfo[,] packetHandlers = new PacketHandlerInfo[(int)ConnectionStateEnum.Count, (int)Packets.Count];

        public static void SetImplementation(ConnectionStateEnum state, Type type)
        {
            if (!type.IsSubclassOf(typeof(MpConnectionState))) return;

            connectionImpls[(int)state] = type;

            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                var attr = method.GetAttribute<PacketHandlerAttribute>();
                if (attr == null)
                    continue;

                if (method.GetParameters().Length != 1 || method.GetParameters()[0].ParameterType != typeof(ByteReader))
                    continue;

                bool fragment = method.GetAttribute<IsFragmentedAttribute>() != null;
                packetHandlers[(int)state, (int)attr.packet] = new PacketHandlerInfo(method, fragment);
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

    public class IsFragmentedAttribute : Attribute
    {
    }

    public class PacketHandlerInfo
    {
        public readonly MethodInfo method;
        public readonly bool fragment;

        public PacketHandlerInfo(MethodInfo method, bool fragment)
        {
            this.method = method;
            this.fragment = fragment;
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
                    stateObj = null;
                else
                    stateObj = (MpConnectionState)Activator.CreateInstance(MpConnectionState.connectionImpls[(int)value], this);
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

        public virtual void Send(Packets id, byte[] message, bool reliable = true)
        {
            if (state == ConnectionStateEnum.Disconnected)
                return;

            if (message.Length > FragmentSize)
                throw new PacketSendException($"Packet {id} too big for sending ({message.Length}>{FragmentSize})");

            byte[] full = new byte[1 + message.Length];
            full[0] = (byte)(Convert.ToByte(id) & 0x3F);
            message.CopyTo(full, 1);

            SendRaw(full, reliable);
        }

        // Steam doesn't like messages bigger than a megabyte
        public const int FragmentSize = 50_000;
        public const int MaxPacketSize = 33_554_432;

        private const int FRAG_NONE = 0x0;
        private const int FRAG_MORE = 0x40;
        private const int FRAG_END = 0x80;

        // All fragmented packets need to be sent from the same thread
        public virtual void SendFragmented(Packets id, byte[] message)
        {
            if (state == ConnectionStateEnum.Disconnected)
                return;

            int read = 0;
            while (read < message.Length)
            {
                int len = Math.Min(FragmentSize, message.Length - read);
                int fragState = (read + len >= message.Length) ? FRAG_END : FRAG_MORE;
                byte state = (byte)((Convert.ToByte(id) & 0x3F) | fragState);

                var writer = new ByteWriter(1 + 4 + len);

                // Write the packet id and fragment state: MORE or END
                writer.WriteByte(state);

                // Send the message length with the first packet
                if (read == 0) writer.WriteInt32(message.Length);

                // Copy the message fragment
                writer.WriteFrom(message, read, len);

                SendRaw(writer.ToArray());

                read += len;
            }
        }

        protected abstract void SendRaw(byte[] raw, bool reliable = true);

        public virtual void HandleReceive(ByteReader data, bool reliable)
        {
            if (state == ConnectionStateEnum.Disconnected)
                return;

            if (data.Left == 0)
                throw new PacketReadException("No packet id");

            byte info = data.ReadByte();
            byte msgId = (byte)(info & 0x3F);
            byte fragState = (byte)(info & 0xC0);

            HandleReceive(msgId, fragState, data, reliable);
        }

        private ByteWriter fragmented;
        private int fullSize; // For information, doesn't affect anything

        public int FragmentProgress => (fragmented?.Position * 100 / fullSize) ?? 0;

        protected virtual void HandleReceive(int msgId, int fragState, ByteReader reader, bool reliable)
        {
            if (msgId < 0 || msgId >= MpConnectionState.packetHandlers.Length)
                throw new PacketReadException($"Bad packet id {msgId}");

            Packets packetType = (Packets)msgId;

            var handler = MpConnectionState.packetHandlers[(int)state, (int)packetType];
            if (handler == null)
            {
                if (reliable)
                    throw new PacketReadException($"No handler for packet {packetType} in state {state}");
                else
                    return;
            }

            if (fragState != FRAG_NONE && fragmented == null)
                fullSize = reader.ReadInt32();

            if (reader.Left > FragmentSize)
                throw new PacketReadException($"Packet {packetType} too big {reader.Left}>{FragmentSize}");

            if (fragState == FRAG_NONE)
            {
                handler.method.Invoke(stateObj, new object[] { reader });
            }
            else if (!handler.fragment)
            {
                throw new PacketReadException($"Packet {packetType} can't be fragmented");
            }
            else
            {
                if (fragmented == null)
                    fragmented = new ByteWriter(reader.Left);

                fragmented.WriteRaw(reader.ReadRaw(reader.Left));

                if (fragmented.Position > MaxPacketSize)
                    throw new PacketReadException($"Full packet {packetType} too big {fragmented.Position}>{MaxPacketSize}");

                if (fragState == FRAG_END)
                {
                    handler.method.Invoke(stateObj, new object[] { new ByteReader(fragmented.ToArray()) });
                    fragmented = null;
                }
            }
        }

        public abstract void Close(MpDisconnectReason reason = MpDisconnectReason.Generic, byte[] data = null);

        public static byte[] GetDisconnectBytes(MpDisconnectReason reason, byte[] data = null)
        {
            var writer = new ByteWriter();
            writer.WriteByte((byte)reason);
            writer.WritePrefixedBytes(data ?? new byte[0]);
            return writer.ToArray();
        }
    }

    public enum MpDisconnectReason : byte
    {
        Generic,
        Protocol,
        Defs,
        UsernameLength,
        UsernameChars,
        UsernameAlreadyOnline,
        ServerClosed,
        ServerFull,
        Kick,
        ClientLeft,
    }

    public class PacketReadException : Exception
    {
        public PacketReadException(string msg) : base(msg)
        {
        }
    }

    public class PacketSendException : Exception
    {
        public PacketSendException(string msg) : base(msg)
        {
        }
    }

    public class MpNetConnection : IConnection
    {
        public readonly NetPeer peer;

        public MpNetConnection(NetPeer peer)
        {
            this.peer = peer;
        }

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            peer.Send(raw, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
            peer.Flush();
            peer.NetManager.DisconnectPeer(peer, GetDisconnectBytes(reason, data));
        }

        public override string ToString()
        {
            return $"NetConnection ({peer.EndPoint}) ({username})";
        }
    }

    public class ByteWriter
    {
        private MemoryStream stream;
        public object context;

        public int Position => (int)stream.Position;

        public ByteWriter(int capacity = 0)
        {
            stream = new MemoryStream(capacity);
        }

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

        public virtual void WritePrefixedInts(IList<int> ints)
        {
            WriteInt32(ints.Count);
            foreach (var @int in ints)
                WriteInt32(@int);
        }

        public virtual void WritePrefixedUInts(IList<uint> ints)
        {
            WriteInt32(ints.Count);
            foreach (var @int in ints)
                WriteUInt32(@int);
        }

        public virtual void WriteRaw(byte[] bytes)
        {
            stream.Write(bytes);
        }

        public virtual void WriteFrom(byte[] buffer, int offset, int length)
        {
            stream.Write(buffer, offset, length);
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
            else if (obj is short @short)
            {
                WriteShort(@short);
            }
            else if (obj is bool @bool)
            {
                WriteBool(@bool);
            }
            else if (obj is long @long)
            {
                WriteLong(@long);
            }
            else if (obj is ulong @ulong)
            {
                WriteULong(@ulong);
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
            else {
                Log.Error($"MP ByteWriter.Write: Unknown type {obj.GetType().ToString()}");
            }
        }

        public byte[] ToArray()
        {
            return stream.ToArray();
        }

        public static byte[] GetBytes(params object[] data)
        {
            var writer = new ByteWriter();
            foreach (object o in data)
                writer.Write(o);
            return writer.ToArray();
        }

        internal void SetLength(long value)
        {
            stream.SetLength(value);
        }
    }

    public class ByteReader
    {
        private readonly byte[] array;
        private int index;
        public object context;

        public int Length => array.Length;
        public int Position => index;
        public int Left => Length - Position;

        public ByteReader(byte[] array)
        {
            this.array = array;
        }

        public byte PeekByte() => array[index];

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

        public string ReadString(int maxLen = 32767)
        {
            int bytes = ReadInt32();

            if (bytes < 0)
                throw new ReaderException($"String byte length ({bytes}<0)");
            if (bytes > maxLen)
                throw new ReaderException($"String too long ({bytes}>{maxLen})");

            string result = Encoding.UTF8.GetString(array, index, bytes);
            index += bytes;
            return result;
        }

        public byte[] ReadRaw(int len)
        {
            return array.SubArray(IncrementIndex(len), len);
        }

        public byte[] ReadPrefixedBytes(int maxLen = int.MaxValue)
        {
            int len = ReadInt32();

            if (len < 0)
                throw new ReaderException($"Byte array length ({len}<0)");
            if (len >= maxLen)
                throw new ReaderException($"Byte array too long ({len}>{maxLen})");

            return ReadRaw(len);
        }

        public int[] ReadPrefixedInts(int maxLen = int.MaxValue)
        {
            int len = ReadInt32();

            if (len < 0)
                throw new ReaderException($"Int array length ({len}<0)");
            if (len > maxLen)
                throw new ReaderException($"Int array too long ({len}>{maxLen})");

            int[] result = new int[len];
            for (int i = 0; i < len; i++)
                result[i] = ReadInt32();

            return result;
        }

        public uint[] ReadPrefixedUInts()
        {
            int len = ReadInt32();
            uint[] result = new uint[len];
            for (int i = 0; i < len; i++)
                result[i] = ReadUInt32();
            return result;
        }

        public ulong[] ReadPrefixedULongs()
        {
            int len = ReadInt32();
            ulong[] result = new ulong[len];
            for (int i = 0; i < len; i++) {
                result[i] = ReadULong();
            }
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

        public void Seek(int position)
        {
            index = position;
        }
    }

    public class ReaderException : Exception
    {
        public ReaderException(string msg) : base(msg)
        {
        }
    }

    public class WriterException : Exception
    {
        public WriterException(string msg) : base(msg)
        {
        }
    }

}
