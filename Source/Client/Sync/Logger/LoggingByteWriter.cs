using Multiplayer.Common;

namespace Multiplayer.Client
{
    public class LoggingByteWriter : ByteWriter
    {
        public SyncLogger log = new SyncLogger();

        public override void WriteInt32(int val)
		{
			log.Node("int: " + val.ToString());
			base.WriteInt32(val);
		}

		public override void WriteBool(bool val)
		{
			log.Node("bool: " + val.ToString());
			base.WriteBool(val);
		}

		public override void WriteDouble(double val)
		{
			log.Node("double: " + val.ToString());
			base.WriteDouble(val);
		}

		public override void WriteUShort(ushort val)
		{
			log.Node("ushort: " + val.ToString());
			base.WriteUShort(val);
		}

		public override void WriteShort(short val)
		{
			log.Node("short: " + val.ToString());
			base.WriteShort(val);
		}

		public override void WriteFloat(float val)
		{
			log.Node("float: " + val.ToString());
			base.WriteFloat(val);
		}

		public override void WriteLong(long val)
		{
			log.Node("long: " + val.ToString());
			base.WriteLong(val);
		}

		public override void WritePrefixedBytes(byte[] bytes)
		{
			log.Enter("byte[]");
			base.WritePrefixedBytes(bytes);
			log.Exit();
		}

		public override ByteWriter WriteString(string s)
		{
			log.Enter("string: " + s);
			base.WriteString(s);
			log.Exit();
			return this;
		}
    }

}
