using Multiplayer.Common;

namespace Multiplayer.Client
{
    public class LoggingByteReader : ByteReader
    {
        public readonly SyncLogger log = new SyncLogger();

        public LoggingByteReader(byte[] array) : base(array)
        {

        }

        public override bool ReadBool()
        {
            return log.NodePassthrough("bool: ", base.ReadBool());
        }

        public override byte ReadByte()
        {
            return log.NodePassthrough("byte: ", base.ReadByte());
        }

        public override double ReadDouble()
        {
            return log.NodePassthrough("double: ", base.ReadDouble());
        }

        public override float ReadFloat()
        {
            return log.NodePassthrough("float: ", base.ReadFloat());
        }

        public override int ReadInt32()
        {
            return log.NodePassthrough("int: ", base.ReadInt32());
        }

        public override long ReadLong()
        {
            return log.NodePassthrough("long: ", base.ReadLong());
        }

        public override sbyte ReadSByte()
        {
            return log.NodePassthrough("sbyte: ", base.ReadSByte());
        }

        public override short ReadShort()
        {
            return log.NodePassthrough("short: ", base.ReadShort());
        }

        public override string ReadString(int maxLen = 32767)
        {
            return log.NodePassthrough("string: ", base.ReadString(maxLen));
        }

        public override uint ReadUInt32()
        {
            return log.NodePassthrough("uint: ", base.ReadUInt32());
        }

        public override ulong ReadULong()
        {
            return log.NodePassthrough("ulong: ", base.ReadULong());
        }

        public override ushort ReadUShort()
        {
            return log.NodePassthrough("ushort: ", base.ReadUShort());
        }

        public override byte[] ReadPrefixedBytes(int maxLen = int.MaxValue)
        {
            log.Pause();
            byte[] array = base.ReadPrefixedBytes();
            log.Resume();
            log.Node($"byte[{array.Length}]");
            return array;
        }
    }

}
