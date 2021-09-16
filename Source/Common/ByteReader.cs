using System;
using System.Text;

namespace Multiplayer.Common
{
    public class ByteReader
    {
        const int DefaultMaxStringLen = 32767;

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

        public virtual byte PeekByte() => array[index];

        public virtual byte ReadByte() => array[IncrementIndex(1)];

        public virtual sbyte ReadSByte() => (sbyte)array[IncrementIndex(1)];

        public virtual short ReadShort() => BitConverter.ToInt16(array, IncrementIndex(2));

        public virtual ushort ReadUShort() => BitConverter.ToUInt16(array, IncrementIndex(2));

        public virtual int ReadInt32() => BitConverter.ToInt32(array, IncrementIndex(4));

        public virtual uint ReadUInt32() => BitConverter.ToUInt32(array, IncrementIndex(4));

        public virtual long ReadLong() => BitConverter.ToInt64(array, IncrementIndex(8));

        public virtual ulong ReadULong() => BitConverter.ToUInt64(array, IncrementIndex(8));

        public virtual float ReadFloat() => BitConverter.ToSingle(array, IncrementIndex(4));

        public virtual double ReadDouble() => BitConverter.ToDouble(array, IncrementIndex(8));

        public virtual bool ReadBool() => BitConverter.ToBoolean(array, IncrementIndex(1));

        public virtual string ReadString(int maxLen = DefaultMaxStringLen)
        {
            int bytes = ReadInt32();
            if (bytes == -1) return null;

            if (bytes < -1)
                throw new ReaderException($"String byte length ({bytes}<-1)");
            if (bytes > maxLen)
                throw new ReaderException($"String too long ({bytes}>{maxLen})");

            string result = Encoding.UTF8.GetString(array, index, bytes);
            index += bytes;

            return result;
        }

        public virtual byte[] ReadRaw(int len)
        {
            return array.SubArray(IncrementIndex(len), len);
        }

        public virtual byte[] ReadPrefixedBytes(int maxLen = int.MaxValue)
        {
            int len = ReadInt32();
            if (len == -1) return null;

            if (len < -1)
                throw new ReaderException($"Byte array length ({len}<-1)");
            if (len >= maxLen)
                throw new ReaderException($"Byte array too long ({len}>{maxLen})");

            return ReadRaw(len);
        }

        public virtual int[] ReadPrefixedInts(int maxLen = int.MaxValue)
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

        public virtual uint[] ReadPrefixedUInts()
        {
            int len = ReadInt32();
            uint[] result = new uint[len];
            for (int i = 0; i < len; i++)
                result[i] = ReadUInt32();
            return result;
        }

        public virtual ulong[] ReadPrefixedULongs()
        {
            int len = ReadInt32();
            ulong[] result = new ulong[len];
            for (int i = 0; i < len; i++) {
                result[i] = ReadULong();
            }
            return result;
        }

        public virtual string[] ReadPrefixedStrings()
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

}
