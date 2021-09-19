using Multiplayer.Common;

namespace Multiplayer.Client
{
    public class LoggingByteReader : ByteReader, IHasLogger
    {
        public SyncLogger Log { get; } = new SyncLogger();

        public LoggingByteReader(byte[] array) : base(array)
        {

        }

        public override bool ReadBool()
        {
            return Log.NodePassthrough("bool: ", base.ReadBool());
        }

        public override byte ReadByte()
        {
            return Log.NodePassthrough("byte: ", base.ReadByte());
        }

        public override double ReadDouble()
        {
            return Log.NodePassthrough("double: ", base.ReadDouble());
        }

        public override float ReadFloat()
        {
            return Log.NodePassthrough("float: ", base.ReadFloat());
        }

        public override int ReadInt32()
        {
            return Log.NodePassthrough("int: ", base.ReadInt32());
        }

        public override long ReadLong()
        {
            return Log.NodePassthrough("long: ", base.ReadLong());
        }

        public override sbyte ReadSByte()
        {
            return Log.NodePassthrough("sbyte: ", base.ReadSByte());
        }

        public override short ReadShort()
        {
            return Log.NodePassthrough("short: ", base.ReadShort());
        }

        public override string ReadString(int maxLen = 32767)
        {
            return Log.NodePassthrough("string: ", base.ReadString(maxLen));
        }

        public override uint ReadUInt32()
        {
            return Log.NodePassthrough("uint: ", base.ReadUInt32());
        }

        public override ulong ReadULong()
        {
            return Log.NodePassthrough("ulong: ", base.ReadULong());
        }

        public override ushort ReadUShort()
        {
            return Log.NodePassthrough("ushort: ", base.ReadUShort());
        }

        public override byte[] ReadPrefixedBytes(int maxLen = int.MaxValue)
        {
            Log.Pause();
            byte[] array = base.ReadPrefixedBytes();
            Log.Resume();
            Log.Node($"byte[{array.Length}]");
            return array;
        }
    }

}
