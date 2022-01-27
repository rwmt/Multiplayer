using System;

using Multiplayer.API;
using Multiplayer.Common;

namespace Multiplayer.Client
{
    public class ReadingSyncWorker : SyncWorker
    {
        internal readonly ByteReader reader;
        readonly int initialPos;

        internal ByteReader Reader => reader;

        public ReadingSyncWorker(ByteReader reader) : base(false)
        {
            this.reader = reader;
            initialPos = reader.Position;
        }

        public override void Bind<T>(ref T obj, SyncType type)
        {
            obj = (T)SyncSerialization.ReadSyncObject(reader, type);
        }

        public override void Bind<T>(ref T obj)
        {
            obj = (T) SyncSerialization.ReadSyncObject(reader, typeof(T));
        }

        public override void Bind(object obj, string name)
        {
            object value = MpReflection.GetValue(obj, name);

            Type type = value.GetType();

            var res = SyncSerialization.ReadSyncObject(reader, type);

            MpReflection.SetValue(obj, name, res);
        }

        public override void Bind(ref byte obj)
        {
            obj = reader.ReadByte();
        }

        public override void Bind(ref sbyte obj)
        {
            obj = reader.ReadSByte();
        }

        public override void Bind(ref short obj)
        {
            obj = reader.ReadShort();
        }

        public override void Bind(ref ushort obj)
        {
            obj = reader.ReadUShort();
        }

        public override void Bind(ref int obj)
        {
            obj = reader.ReadInt32();
        }

        public override void Bind(ref uint obj)
        {
            obj = reader.ReadUInt32();
        }

        public override void Bind(ref long obj)
        {
            obj = reader.ReadLong();
        }

        public override void Bind(ref ulong obj)
        {
            obj = reader.ReadULong();
        }

        public override void Bind(ref float obj)
        {
            obj = reader.ReadFloat();
        }

        public override void Bind(ref double obj)
        {
            obj = reader.ReadDouble();
        }

        public override void Bind(ref bool obj)
        {
            obj = reader.ReadBool();
        }

        public override void Bind(ref string obj)
        {
            obj = reader.ReadString();
        }

        public override void BindType<T>(ref Type type)
        {
            type = TypeRWHelper.GetType(reader.ReadUShort(), typeof(T));
        }

        internal void Reset()
        {
            reader.Seek(initialPos);
        }
    }
}
