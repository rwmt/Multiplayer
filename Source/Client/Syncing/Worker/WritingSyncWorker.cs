using System;

using Multiplayer.API;
using Multiplayer.Common;

namespace Multiplayer.Client
{
    public class WritingSyncWorker : SyncWorker
    {
        internal readonly ByteWriter writer;
        readonly int initialPos;

        internal ByteWriter Writer { get; }

        public WritingSyncWorker(ByteWriter writer) : base(true)
        {
            this.writer = writer;
            initialPos = writer.Position;
        }

        public override void Bind<T>(ref T obj, SyncType type)
        {
            SyncSerialization.WriteSyncObject(writer, obj, type);
        }

        public override void Bind<T>(ref T obj)
        {
            SyncSerialization.WriteSyncObject(writer, obj, typeof(T));
        }

        public override void Bind(object obj, string name)
        {
            object value = MpReflection.GetValue(obj, name);
            Type type = value.GetType();

            SyncSerialization.WriteSyncObject(writer, value, type);
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
            writer.WriteUShort(TypeRWHelper.GetTypeIndex(type, typeof(T)));
        }

        internal void Reset()
        {
            writer.SetLength(initialPos);
        }
    }
}
