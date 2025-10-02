using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Multiplayer.Common.Networking.Packet;

[AttributeUsage(AttributeTargets.Struct)]
public class PacketDefinitionAttribute(Packets packet, bool allowFragmented = false) : Attribute
{
    public readonly Packets packet = packet;
    public readonly bool allowFragmented = allowFragmented;
}

/// Must have a [PacketDefinitionAttribute] attribute
public interface IPacket : IPacketBufferable;

public struct SerializedPacket(Packets id, byte[] data)
{
    public Packets id = id;
    public byte[] data = data;

    public static SerializedPacket From<T>(T packet) where T : IPacket
    {
        var writer = new ByteWriter();
        packet.Bind(new PacketWriter(writer));
        return new SerializedPacket(packet.GetId(), writer.ToArray());
    }
}

[SuppressMessage("ReSharper", "StaticMemberInGenericType")]
public static class PacketTypeInfo<T> where T : IPacket
{
    private static readonly PacketDefinitionAttribute attr = typeof(T).GetAttribute<PacketDefinitionAttribute>() ??
                                                             throw new InvalidOperationException();

    public static readonly Packets Id = attr.packet;
    public static readonly bool AllowFragmented = attr.allowFragmented;
}

public static class PacketExt
{
    public static Packets GetId<T>(this T _) where T : IPacket => PacketTypeInfo<T>.Id;

    public static SerializedPacket Serialize<T>(this T packet) where T : IPacket => SerializedPacket.From(packet);
}

public interface IPacketBufferable
{
    void Bind(PacketBuffer buf);
}

public delegate void Binder<T>(PacketBuffer buf, ref T obj);

public static class BinderOf
{
    public static Binder<T> Identity<T>() where T : struct, IPacketBufferable =>
        (PacketBuffer buf, ref T obj) => { buf.Bind(ref obj); };
}

public static class BinderExtensions
{
    public static byte[] Serialize<T>(this Binder<T> binder, T value)
    {
        var writer = new ByteWriter();
        binder(new PacketWriter(writer), ref value);
        return writer.ToArray();
    }

    public static T Deserialize<T>(this Binder<T> binder, byte[] src)
    {
        var obj = default(T);
        binder(new PacketReader(new ByteReader(src)), ref obj);
        return obj;
    }
}

public abstract class PacketBuffer(bool isWriting)
{
    public const int DefaultMaxLength = 32767;
    public bool isWriting = isWriting;

    public virtual ByteReader Reader => throw new Exception();
    public virtual ByteWriter Writer => throw new Exception();

    public abstract bool DataRemaining { get; }

    public abstract void Bind(ref byte obj);

    public abstract void Bind(ref sbyte obj);

    public abstract void Bind(ref short obj);

    public abstract void Bind(ref ushort obj);

    public abstract void Bind(ref int obj);

    public abstract void Bind(ref uint obj);

    public abstract void Bind(ref long obj);

    public abstract void Bind(ref ulong obj);

    public abstract void Bind(ref float obj);

    public abstract void Bind(ref double obj);

    public abstract void Bind(ref string obj, int maxLength = DefaultMaxLength);

    public abstract void Bind(ref bool obj);

    public abstract void BindEnum<T>(ref T obj) where T : Enum;

    public void Bind<T>(ref T obj) where T : struct, IPacketBufferable => obj.Bind(this);

    public void BindWith<T>(ref T obj, Binder<T> bind) => bind(this, ref obj);

    public abstract void BindWith<T>(ref T obj, Action<T> write, Func<T> read);

    public abstract void BindBytes(ref byte[] obj, int maxLength = DefaultMaxLength);

    public abstract void Bind<T>(ref T[] obj, Binder<T> bind, int maxLength = DefaultMaxLength);

    /// There can only be one remaining bind for a single packet, and it must be the last one. The advantage of this
    /// compared to BindBytes is that this method does not serialize the buffer's length and instead just binds all
    /// data that hasn't been read yet.
    public abstract void BindRemaining(ref byte[] obj, int maxLength = DefaultMaxLength);

    public abstract void Bind<T>(ref List<T> obj, Binder<T> bind, int maxLength = DefaultMaxLength);

    public abstract void Bind<K, V>(ref Dictionary<K, V> obj, Binder<K> bindKey, Binder<V> bindValue,
        int maxLength = DefaultMaxLength);
}

public sealed class PacketReader(ByteReader reader) : PacketBuffer(false)
{
    public override ByteReader Reader => reader;
    public override bool DataRemaining => reader.Left > 0;

    public override void Bind(ref byte obj) => obj = reader.ReadByte();

    public override void Bind(ref sbyte obj) => obj = reader.ReadSByte();

    public override void Bind(ref short obj) => obj = reader.ReadShort();

    public override void Bind(ref ushort obj) => obj = reader.ReadUShort();

    public override void Bind(ref int obj) => obj = reader.ReadInt32();

    public override void Bind(ref uint obj) => obj = reader.ReadUInt32();

    public override void Bind(ref long obj) => obj = reader.ReadLong();

    public override void Bind(ref ulong obj) => obj = reader.ReadULong();

    public override void Bind(ref float obj) => obj = reader.ReadFloat();

    public override void Bind(ref double obj) => obj = reader.ReadDouble();

    public override void Bind(ref bool obj) => obj = reader.ReadBool();

    public override void BindEnum<T>(ref T obj) => obj = reader.ReadEnum<T>();

    public override void BindWith<T>(ref T obj, Action<T> write, Func<T> read) => obj = read();

    public override void Bind(ref string obj, int maxLength = DefaultMaxLength) => obj = reader.ReadString(maxLength);

    public override void BindBytes(ref byte[] obj, int maxLength = DefaultMaxLength)
    {
        int len = reader.ReadInt32();
        obj = reader.ReadRaw(len);
    }

    public override void Bind<T>(ref T[] obj, Binder<T> bind, int maxLength = DefaultMaxLength)
    {
        int len = reader.ReadInt32();
        if (len > maxLength && maxLength != -1) throw new ReaderException("Object too big");

        obj = new T[len];
        for (var i = 0; i < len; i++)
        {
            var item = default(T);
            bind(this, ref item);
            obj[i] = item;
        }
    }

    public override void BindRemaining(ref byte[] obj, int maxLength = DefaultMaxLength)
    {
        if (reader.Left > maxLength) throw new ReaderException("Object too big");
        obj = reader.ReadRaw(reader.Left);
    }

    public override void Bind<T>(ref List<T> obj, Binder<T> bind, int maxLength = DefaultMaxLength)
    {
        int len = reader.ReadInt32();
        if (len > maxLength) throw new ReaderException("Object too big");

        obj = new List<T>(len);
        for (var i = 0; i < len; i++)
        {
            var item = default(T);
            bind(this, ref item);
            obj.Add(item);
        }
    }

    public override void Bind<K, V>(ref Dictionary<K, V> obj, Binder<K> bindKey, Binder<V> bindValue,
        int maxLength = DefaultMaxLength)
    {
        int len = reader.ReadInt32();
        if (len > maxLength && maxLength != -1) throw new ReaderException("Object too big");

        obj = new Dictionary<K, V>(len);
        for (int i = 0; i < len; i++)
        {
            var key = default(K);
            bindKey(this, ref key);

            var value = default(V);
            bindValue(this, ref value);
            obj.Add(key, value);
        }
    }
}

public sealed class PacketWriter(ByteWriter writer) : PacketBuffer(true)
{
    public override ByteWriter Writer => writer;
    public override bool DataRemaining => false;

    public override void Bind(ref byte obj) => writer.WriteByte(obj);

    public override void Bind(ref sbyte obj) => writer.WriteSByte(obj);

    public override void Bind(ref short obj) => writer.WriteShort(obj);

    public override void Bind(ref ushort obj) => writer.WriteUShort(obj);

    public override void Bind(ref int obj) => writer.WriteInt32(obj);

    public override void Bind(ref uint obj) => writer.WriteUInt32(obj);

    public override void Bind(ref long obj) => writer.WriteLong(obj);

    public override void Bind(ref ulong obj) => writer.WriteULong(obj);

    public override void Bind(ref float obj) => writer.WriteFloat(obj);

    public override void Bind(ref double obj) => writer.WriteDouble(obj);

    public override void Bind(ref bool obj) => writer.WriteBool(obj);

    public override void BindEnum<T>(ref T obj) => writer.WriteEnum(obj);

    public override void Bind(ref string obj, int maxLength = DefaultMaxLength)
    {
        if (obj != null && obj.Length > maxLength) throw new WriterException("Too long string");
        writer.WriteString(obj);
    }

    public override void BindWith<T>(ref T obj, Action<T> write, Func<T> read) => write(obj);

    public override void BindBytes(ref byte[] obj, int maxLength = DefaultMaxLength)
    {
        writer.WriteInt32(obj.Length);
        writer.WriteRaw(obj);
    }

    public override void Bind<T>(ref T[] obj, Binder<T> bind, int maxLength = DefaultMaxLength)
    {
        if (obj.Length > maxLength) throw new WriterException("Object too big");
        writer.WriteInt32(obj.Length);
        for (var i = 0; i < obj.Length; i++)
        {
            bind(this, ref obj[i]);
        }
    }

    public override void BindRemaining(ref byte[] obj, int maxLength = DefaultMaxLength)
    {
        if (obj.Length > maxLength) throw new WriterException("Object too big");
        writer.WriteRaw(obj);
    }

    public override void Bind<T>(ref List<T> obj, Binder<T> bind, int maxLength = DefaultMaxLength)
    {
        if (obj.Count > maxLength) throw new WriterException("Object too big");
        writer.WriteInt32(obj.Count);
        for (var i = 0; i < obj.Count; i++)
        {
            var item = obj[i];
            bind(this, ref item);
        }
    }

    public override void Bind<K, V>(ref Dictionary<K, V> obj, Binder<K> bindKey, Binder<V> bindValue,
        int maxLength = DefaultMaxLength)
    {
        if (obj.Count > maxLength) throw new WriterException("Object too big");
        writer.WriteInt32(obj.Count);
        foreach (var (key, value) in obj)
        {
            var k = key;
            bindKey(this, ref k);
            var v = value;
            bindValue(this, ref v);
        }
    }
}
