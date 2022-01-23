using Multiplayer.Common;

namespace Multiplayer.Client
{
    public class LoggingByteWriter : ByteWriter, IHasLogger
    {
        public SyncLogger Log { get; } = new SyncLogger();

        public override void WriteInt32(int val)
		{
			Log.Node("int: " + val.ToString());
			base.WriteInt32(val);
		}

		public override void WriteBool(bool val)
		{
			Log.Node("bool: " + val.ToString());
			base.WriteBool(val);
		}

		public override void WriteDouble(double val)
		{
			Log.Node("double: " + val.ToString());
			base.WriteDouble(val);
		}

		public override void WriteUShort(ushort val)
		{
			Log.Node("ushort: " + val.ToString());
			base.WriteUShort(val);
		}

		public override void WriteShort(short val)
		{
			Log.Node("short: " + val.ToString());
			base.WriteShort(val);
		}

		public override void WriteFloat(float val)
		{
			Log.Node("float: " + val.ToString());
			base.WriteFloat(val);
		}

		public override void WriteLong(long val)
		{
			Log.Node("long: " + val.ToString());
			base.WriteLong(val);
		}

		public override void WritePrefixedBytes(byte[] bytes)
		{
			Log.Enter("byte[]");
			base.WritePrefixedBytes(bytes);
			Log.Exit();
		}

		public override ByteWriter WriteString(string s)
		{
			Log.Enter("string: " + s);
			base.WriteString(s);
			Log.Exit();
			return this;
		}
    }

}
