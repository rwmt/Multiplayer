using HarmonyLib;
using Multiplayer.API;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Multiplayer.Client;

namespace Multiplayer.Common
{
    public class SyncSerialization(SyncTypeHelper typeHelper)
    {
        public delegate bool CanHandleHook(SyncType syncType);

        public delegate bool SyncTypeMatcher(SyncType syncType);
        public delegate object? SerializationReader(ByteReader data, SyncType syncType);
        public delegate void SerializationWriter(ByteWriter data, object? obj, SyncType syncType);

        private List<CanHandleHook> canHandleHooks = [];
        private List<(SyncTypeMatcher, SerializationReader, SerializationWriter)> serializationHooks = [];
        public HashSet<Type> explicitImplTypes = [];
        public SyncTypeHelper TypeHelper { get; } = typeHelper;

        public Action<string> errorLogger = msg => Console.WriteLine($"Sync Error: {msg}");

        public void AddCanHandleHook(CanHandleHook hook)
        {
            canHandleHooks.Add(hook);
        }

        public void AddSerializationHook(SyncTypeMatcher matcher, SerializationReader reader, SerializationWriter writer)
        {
            serializationHooks.Add((matcher, reader, writer));
        }

        public void AddExplicitImplType(Type type)
        {
            explicitImplTypes.Add(type);
        }

        public SyncWorkerDictionaryTree? syncTree;

        private static Type[] supportedSystemGtds =
            [typeof(List<>), typeof(IEnumerable<>), typeof(Nullable<>), typeof(Dictionary<,>), typeof(HashSet<>)];

        public bool CanHandle(SyncType syncType)
        {
            var type = syncType.type;

            if (type == typeof(object))
                return true;
            if (type.IsByRef)
                return true;
            if (SyncDictPrimitives.syncWorkers.TryGetValue(type, out _))
                return true;
            if (typeof(ISynchronizable).IsAssignableFrom(type))
                return true;
            if (type.IsEnum)
                return CanHandle(Enum.GetUnderlyingType(type));
            if (type.IsArray)
                return type.GetArrayRank() == 1 && CanHandle(type.GetElementType());

            if (type.IsGenericType &&
                type.GetGenericTypeDefinition() is { } gtd&&
                (supportedSystemGtds.Contains(gtd) || typeof(ITuple).IsAssignableFrom(gtd)))
                return CanHandleGenericArgs(type);

            if (Enumerable.Any(canHandleHooks, hook => hook(syncType)))
                return true;

            if (explicitImplTypes.Contains(syncType.type))
                return true;

            return syncTree != null && syncTree.TryGetValue(type, out _);
        }

        public bool CanHandleGenericArgs(Type genericType)
        {
            return genericType.GetGenericArguments().All(arg => CanHandle(arg));
        }

        public T? ReadSync<T>(ByteReader data)
        {
            return (T?)ReadSyncObject(data, typeof(T));
        }

        // Serialization order:
        // object is null
        // byref is null
        // Primitives (bool, numerics, string)
        // Enums
        // Array (single-dimensional)
        // Generic types:
        //      List<T>, IEnumerable<T> (synced as List<T>), Nullable<T>, Dictionary<K, V>, HashSet<T>
        //      ValueTuple<T1, T2>
        //      Implementations of System.Runtime.CompilerServices.ITuple (System.Tuples and System.ValueTuples)
        // For each serializationHooks:
        //      if matcher: serialize; break
        // Implementations of Multiplayer.API.ISynchronizable
        // Run all typeChangerHooks
        // syncTree.TryGetValue(type)

        public object? ReadSyncObject(ByteReader data, SyncType syncType)
        {
            var log = (data as LoggingByteReader)?.Log;
            Type type = syncType.type;

            log?.Enter(type.FullName);

            try
            {
                object? val = ReadSyncObjectInternal(data, syncType);
                log?.AppendToCurrentName($": {val}");
                return val;
            }
            finally
            {
                log?.Exit();
            }
        }

        private object? ReadSyncObjectInternal(ByteReader data, SyncType syncType)
        {
            Type type = syncType.type;

            try
            {
                if (typeof(object) == type)
                {
                    return null;
                }

                if (type.IsByRef)
                {
                    return null;
                }

                if (SyncDictPrimitives.syncWorkers.TryGetValue(type, out SyncWorkerEntry syncWorkerEntryEarly)) {
                    object? res = null;

                    if (syncWorkerEntryEarly.shouldConstruct || type.IsValueType)
                        res = Activator.CreateInstance(type);

                    syncWorkerEntryEarly.Invoke(new ReadingSyncWorker(data, this), ref res);

                    return res;
                }

                if (type.IsEnum) {
                    Type underlyingType = Enum.GetUnderlyingType(type);

                    return Enum.ToObject(type, ReadSyncObject(data, underlyingType)!);
                }

                if (type.IsArray)
                {
                    if (type.GetArrayRank() != 1)
                        throw new SerializationException("Multi dimensional arrays aren't supported.");

                    ushort length = data.ReadUShort();
                    if (length == ushort.MaxValue)
                        return null;

                    Type elementType = type.GetElementType()!;
                    Array arr = Array.CreateInstance(elementType, length);

                    for (int i = 0; i < length; i++)
                        arr.SetValue(ReadSyncObject(data, elementType), i);

                    return arr;
                }

                if (type.IsGenericType)
                {
                    var genericTypeDefinition = type.GetGenericTypeDefinition();

                    if (genericTypeDefinition == typeof(List<>))
                    {
                        ushort size = data.ReadUShort();
                        if (size == ushort.MaxValue)
                            return null;

                        Type listObjType = type.GetGenericArguments()[0];
                        IList list = (IList)Activator.CreateInstance(type, size);

                        for (int j = 0; j < size; j++)
                            list.Add(ReadSyncObject(data, listObjType));

                        return list;
                    }

                    if (genericTypeDefinition == typeof(IEnumerable<>))
                    {
                        Type element = type.GetGenericArguments()[0];
                        return ReadSyncObject(data, typeof(List<>).MakeGenericType(element));
                    }

                    if (genericTypeDefinition == typeof(Nullable<>))
                    {
                        bool isNull = data.ReadBool();
                        if (isNull) return null;

                        return Activator.CreateInstance(type, ReadSyncObject(data, Nullable.GetUnderlyingType(type)));
                    }

                    if (genericTypeDefinition == typeof(Dictionary<,>))
                    {
                        Type[] arguments = type.GetGenericArguments();

                        Array? keys = (Array?)ReadSyncObject(data, arguments[0].MakeArrayType());
                        if (keys == null)
                            return null;

                        Array values = (Array)ReadSyncObject(data, arguments[1].MakeArrayType())!;

                        IDictionary dictionary = (IDictionary)Activator.CreateInstance(type);
                        for (int i = 0; i < keys.Length; i++)
                        {
                            var key = keys.GetValue(i);
                            if (key != null)
                                dictionary.Add(key, values.GetValue(i));
                        }

                        return dictionary;
                    }

                    if (genericTypeDefinition == typeof(HashSet<>))
                    {
                        Type element = type.GetGenericArguments()[0];
                        object? list = ReadSyncObject(data, typeof(List<>).MakeGenericType(element));
                        return list == null ? null : Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(element), list);
                    }

                    if (genericTypeDefinition == typeof(ValueTuple<,>)) // Binary ValueTuple
                    {
                        Type[] arguments = type.GetGenericArguments();
                        object?[] parameters =
                        [
                            ReadSyncObject(data, arguments[0]),
                            ReadSyncObject(data, arguments[1])
                        ];

                        return type.GetConstructors().First().Invoke(parameters);
                    }

                    // todo handle null non-value Tuples?
                    if (typeof(ITuple).IsAssignableFrom(genericTypeDefinition)) // ValueTuple or Tuple
                    {
                        Type[] arguments = type.GetGenericArguments();

                        int size = data.ReadInt32();
                        object?[] values = new object?[size];

                        for (int i = 0; i < size; i++)
                            values[i] = ReadSyncObject(data, arguments[i]);

                        return type.GetConstructors().First().Invoke(values);
                    }
                }

                foreach (var hook in serializationHooks)
                    if (hook.Item1(syncType))
                        return hook.Item2(data, type);

                if (typeof(ISynchronizable).IsAssignableFrom(type))
                {
                    var obj = Activator.CreateInstance(type);

                    ((ISynchronizable) obj).Sync(new ReadingSyncWorker(data, this));
                    return obj;
                }

                if (explicitImplTypes.Contains(type))
                {
                    ushort impl = data.ReadUShort();
                    return impl == ushort.MaxValue ? null :
                        ReadSyncObject(data, TypeHelper.GetImplementationByIndex(type, impl));
                }

                // Where the magic happens
                if (syncTree != null && syncTree.TryGetValue(type, out var syncWorkerEntry))
                {
                    object? res = null;

                    if (syncWorkerEntry.shouldConstruct || type.IsValueType)
                        res = Activator.CreateInstance(type);

                    syncWorkerEntry.Invoke(new ReadingSyncWorker(data, this), ref res);

                    return res;
                }

                throw new SerializationException("No reader for type " + type);
            }
            catch
            {
                errorLogger($"Multiplayer: Error reading type: {type}");
                throw;
            }
        }

        public void WriteSync<T>(ByteWriter data, T? obj)
        {
            WriteSyncObject(data, obj, typeof(T));
        }

        public void WriteSyncObject(ByteWriter data, object? obj, SyncType syncType)
        {
            Type type = syncType.type;
            var log = (data as LoggingByteWriter)?.Log;
            log?.Enter($"{type.FullName}: {obj ?? "null"}");

            if (obj != null && !type.IsInstanceOfType(obj))
                throw new SerializationException($"Serializing with type {type} but got object of type {obj.GetType()}");

            try
            {
                if (typeof(object) == type)
                {
                    return;
                }

                if (type.IsByRef)
                {
                    return;
                }

                if (SyncDictPrimitives.syncWorkers.TryGetValue(type, out var syncWorkerEntryEarly)) {
                    syncWorkerEntryEarly.Invoke(new WritingSyncWorker(data, this), ref obj);

                    return;
                }

                if (type.IsEnum)
                {
                    Type enumType = Enum.GetUnderlyingType(type);
                    WriteSyncObject(data, Convert.ChangeType(obj, enumType), enumType);
                    return;
                }

                if (type.IsArray)
                {
                    if (type.GetArrayRank() != 1)
                        throw new SerializationException("Multi dimensional arrays aren't supported.");

                    Type elementType = type.GetElementType()!;
                    Array? arr = (Array?)obj;

                    if (arr == null)
                    {
                        data.WriteUShort(ushort.MaxValue);
                        return;
                    }

                    if (arr.Length >= ushort.MaxValue)
                        throw new SerializationException($"Tried to serialize a {elementType}[] with too many ({arr.Length}) items.");

                    data.WriteUShort((ushort)arr.Length);
                    foreach (object e in arr)
                        WriteSyncObject(data, e, elementType);

                    return;
                }

                if (type.IsGenericType) {
                    Type genericTypeDefinition = type.GetGenericTypeDefinition();

                    if (genericTypeDefinition == typeof(List<>))
                    {
                        IList? list = (IList?)obj;
                        Type listObjType = type.GetGenericArguments()[0];

                        if (list == null)
                        {
                            data.WriteUShort(ushort.MaxValue);
                            return;
                        }

                        if (list.Count >= ushort.MaxValue)
                            throw new SerializationException($"Tried to serialize a {type} with too many ({list.Count}) items.");

                        data.WriteUShort((ushort)list.Count);
                        foreach (object e in list)
                            WriteSyncObject(data, e, listObjType);

                        return;
                    }

                    if (genericTypeDefinition == typeof(IEnumerable<>) || genericTypeDefinition == typeof(HashSet<>))
                    {
                        IEnumerable? e = (IEnumerable?)obj;
                        Type elementType = type.GetGenericArguments()[0];
                        var listType = typeof(List<>).MakeGenericType(elementType);

                        if (e == null)
                        {
                            WriteSyncObject(data, null, listType);
                            return;
                        }

                        IList list = (IList)Activator.CreateInstance(listType);

                        foreach (var o in e)
                            list.Add(o);

                        WriteSyncObject(data, list, listType);

                        return;
                    }

                    if (genericTypeDefinition == typeof(Nullable<>))
                    {
                        bool isNull = obj == null;
                        data.WriteBool(isNull);
                        if (isNull) return;

                        WriteSyncObject(data, obj, Nullable.GetUnderlyingType(type));

                        return;
                    }

                    if (genericTypeDefinition == typeof(Dictionary<,>))
                    {
                        IDictionary? dictionary = (IDictionary?)obj;
                        Type[] arguments = type.GetGenericArguments();

                        if (dictionary == null)
                        {
                            WriteSyncObject(data, null, arguments[0].MakeArrayType());
                            return;
                        }

                        Array keyArray = Array.CreateInstance(arguments[0], dictionary.Count);
                        dictionary.Keys.CopyTo(keyArray, 0);

                        Array valueArray = Array.CreateInstance(arguments[1], dictionary.Count);
                        dictionary.Values.CopyTo(valueArray, 0);

                        WriteSyncObject(data, keyArray, keyArray.GetType());
                        WriteSyncObject(data, valueArray, valueArray.GetType());

                        return;
                    }

                    if (genericTypeDefinition == typeof(ValueTuple<,>))
                    {
                        Type[] arguments = type.GetGenericArguments();

                        WriteSyncObject(data, AccessTools.DeclaredField(type, "Item1").GetValue(obj), arguments[0]);
                        WriteSyncObject(data, AccessTools.DeclaredField(type, "Item2").GetValue(obj), arguments[1]);

                        return;
                    }

                    if (typeof(ITuple).IsAssignableFrom(genericTypeDefinition)) // ValueTuple or Tuple
                    {
                        Type[] arguments = type.GetGenericArguments();
                        ITuple tuple = (ITuple)obj!;

                        data.WriteInt32(tuple.Length);

                        for (int i = 0; i < tuple.Length; i++)
                            WriteSyncObject(data, tuple[i], arguments[i]);

                        return;
                    }
                }

                foreach (var hook in serializationHooks)
                {
                    if (hook.Item1(syncType))
                    {
                        hook.Item3(data, obj, syncType);
                        return;
                    }
                }

                if (typeof(ISynchronizable).IsAssignableFrom(type))
                {
                    ((ISynchronizable) obj!).Sync(new WritingSyncWorker(data, this));
                    return;
                }

                if (explicitImplTypes.Contains(type))
                {
                    if (obj == null)
                    {
                        data.WriteUShort(ushort.MaxValue);
                        return;
                    }

                    ushort implIndex = TypeHelper!.GetIndexFromImplementation(type, obj.GetType());
                    data.WriteUShort(implIndex);
                    WriteSyncObject(data, obj, obj.GetType());
                    return;
                }

                // Where the magic happens
                if (syncTree != null && syncTree.TryGetValue(type, out var syncWorkerEntry))
                {
                    syncWorkerEntry.Invoke(new WritingSyncWorker(data, this), ref obj);
                    return;
                }

                log?.Node("No writer for " + type);
                throw new SerializationException("No writer for type " + type);
            }
            catch
            {
                errorLogger($"Multiplayer: Error writing type: {type}, obj: {obj}, obj type: {obj?.GetType()}");
                throw;
            }
            finally
            {
                log?.Exit();
            }
        }
    }
}
