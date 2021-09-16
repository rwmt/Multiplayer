using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Multiplayer.Common
{
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

        public virtual void WriteShort(short val) => stream.WriteBytes(BitConverter.GetBytes(val));

        public virtual void WriteUShort(ushort val) => stream.WriteBytes(BitConverter.GetBytes(val));

        public virtual void WriteInt32(int val) => stream.WriteBytes(BitConverter.GetBytes(val));

        public virtual void WriteUInt32(uint val) => stream.WriteBytes(BitConverter.GetBytes(val));

        public virtual void WriteLong(long val) => stream.WriteBytes(BitConverter.GetBytes(val));

        public virtual void WriteULong(ulong val) => stream.WriteBytes(BitConverter.GetBytes(val));

        public virtual void WriteFloat(float val) => stream.WriteBytes(BitConverter.GetBytes(val));

        public virtual void WriteDouble(double val) => stream.WriteBytes(BitConverter.GetBytes(val));

        public virtual void WriteBool(bool val) => stream.WriteByte(val ? (byte)1 : (byte)0);

        public virtual void WritePrefixedBytes(byte[] bytes)
        {
            if (bytes == null)
            {
                WriteInt32(-1);
                return;
            }

            WriteInt32(bytes.Length);
            WriteRaw(bytes);
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
            stream.Write(bytes, 0, bytes.Length);
        }

        public virtual void WriteFrom(byte[] buffer, int offset, int length)
        {
            stream.Write(buffer, offset, length);
        }

        public virtual ByteWriter WriteString(string s)
        {
            WritePrefixedBytes(s == null ? null : Encoding.UTF8.GetBytes(s));
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
            else
            {
                ServerLog.Error($"MP ByteWriter.Write: Unknown type {obj.GetType()}");
            }
        }

        public byte[] ToArray()
        {
            return stream.ToArray();
        }

        /// <summary>
        /// Writes all objects in the order given and returns the resulting bytes.
        /// </summary>
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

    public class WriterException : Exception
    {
        public WriterException(string msg) : base(msg)
        {
        }
    }

}
