using System;

using Multiplayer.API;
using Multiplayer.Common;

namespace Multiplayer.Client
{
    public class WritingSyncWorker(ByteWriter writer, SyncSerialization serialization) : SyncWorker(true)
    {
        internal readonly ByteWriter writer = writer;
        readonly int initialPos = writer.Position;

        public ByteWriter Writer => writer;

        public override void Bind<T>(ref T obj, SyncType type)
        {
            serialization.WriteSyncObject(writer, obj, type);
        }

        public override void Bind<T>(ref T obj)
        {
            serialization.WriteSyncObject(writer, obj, typeof(T));
        }

        public override void Bind(object obj, string name)
        {
            object value = MpReflection.GetValue(obj, name);
            Type type = value.GetType();

            serialization.WriteSyncObject(writer, value, type);
        }

        public override void Bind(ref byte obj)
        {
            writer.WriteByte(obj);
        }

        public override void Bind(ref sbyte obj)
        {
            writer.WriteSByte(obj);
        }

        public override void Bind(ref short obj)
        {
            writer.WriteShort(obj);
        }

        public override void Bind(ref ushort obj)
        {
            writer.WriteUShort(obj);
        }

        public override void Bind(ref int obj)
        {
            writer.WriteInt32(obj);
        }

        public override void Bind(ref uint obj)
        {
            writer.WriteUInt32(obj);
        }

        public override void Bind(ref long obj)
        {
            writer.WriteLong(obj);
        }

        public override void Bind(ref ulong obj)
        {
            writer.WriteULong(obj);
        }

        public override void Bind(ref float obj)
        {
            writer.WriteFloat(obj);
        }

        public override void Bind(ref double obj)
        {
            writer.WriteDouble(obj);
        }

        public override void Bind(ref bool obj)
        {
            writer.WriteBool(obj);
        }

        public override void Bind(ref string obj)
        {
            writer.WriteString(obj);
        }

        public override void BindType<T>(ref Type type)
        {
            writer.WriteUShort(serialization.TypeHelper.GetIndexFromImplementation(typeof(T), type));
        }

        internal void Reset()
        {
            writer.SetLength(initialPos);
        }
    }
}
