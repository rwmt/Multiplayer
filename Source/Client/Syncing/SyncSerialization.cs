using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using Verse;

namespace Multiplayer.Client
{
    // Syncs a type with all its declared fields
    public interface ISyncSimple { }

    public static class SyncSerialization
    {
        public static void Init()
        {
            ImplSerialization.Init();
            DefSerialization.Init();
        }

        public static bool CanHandle(SyncType syncType)
        {
            var type = syncType.type;

            if (type == typeof(object))
                return true;
            if (type.IsByRef)
                return true;
            if (SyncDictFast.syncWorkers.TryGetValue(type, out _))
                return true;
            if (syncType.expose)
                return typeof(IExposable).IsAssignableFrom(type);
            if (typeof(ISynchronizable).IsAssignableFrom(type))
                return true;
            if (type.IsEnum)
                return CanHandle(Enum.GetUnderlyingType(type));
            if (type.IsArray)
                return type.GetArrayRank() == 1 && CanHandle(type.GetElementType());
            if (type.IsGenericType && type.GetGenericTypeDefinition() is { } gtd)
                return
                    (false
                    || gtd == typeof(List<>)
                    || gtd == typeof(IEnumerable<>)
                    || gtd == typeof(Nullable<>)
                    || gtd == typeof(Dictionary<,>)
                    || gtd == typeof(Pair<,>)
                    || typeof(ITuple).IsAssignableFrom(gtd))
                    && CanHandleGenericArgs(type);
            if (typeof(ISyncSimple).IsAssignableFrom(type))
                return AccessTools.GetDeclaredFields(type).All(f => CanHandle(f.FieldType));
            if (typeof(Def).IsAssignableFrom(type))
                return true;
            if (typeof(Designator).IsAssignableFrom(type))
                return true;

            return SyncDict.syncWorkers.TryGetValue(type, out _);
        }

        private static bool CanHandleGenericArgs(Type genericType)
        {
            return genericType.GetGenericArguments().All(arg => CanHandle(arg));
        }

        public static T ReadSync<T>(ByteReader data)
        {
            return (T)ReadSyncObject(data, typeof(T));
        }

        public static object ReadSyncObject(ByteReader data, SyncType syncType)
        {
            var log = (data as LoggingByteReader)?.Log;
            Type type = syncType.type;

            log?.Enter(type.FullName);

            try
            {
                object val = ReadSyncObjectInternal(data, syncType);
                log?.AppendToCurrentName($": {val}");
                return val;
            }
            finally
            {
                log?.Exit();
            }
        }

        private static object ReadSyncObjectInternal(ByteReader data, SyncType syncType)
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

                if (SyncDictFast.syncWorkers.TryGetValue(type, out SyncWorkerEntry syncWorkerEntryEarly)) {
                    object res = null;

                    if (syncWorkerEntryEarly.shouldConstruct || type.IsValueType)
                        res = Activator.CreateInstance(type);

                    syncWorkerEntryEarly.Invoke(new ReadingSyncWorker(data), ref res);

                    return res;
                }

                if (syncType.expose)
                {
                    if (!typeof(IExposable).IsAssignableFrom(type))
                        throw new SerializationException($"Type {type} can't be exposed because it isn't IExposable");

                    byte[] exposableData = data.ReadPrefixedBytes();
                    return ExposableSerialization.ReadExposable(type, exposableData);
                }

                if (typeof(ISynchronizable).IsAssignableFrom(type))
                {
                    var obj = Activator.CreateInstance(type);

                    ((ISynchronizable) obj).Sync(new ReadingSyncWorker(data));
                    return obj;
                }

                if (type.IsEnum) {
                    Type underlyingType = Enum.GetUnderlyingType(type);

                    return Enum.ToObject(type, ReadSyncObject(data, underlyingType));
                }

                if (type.IsArray)
                {
                    if (type.GetArrayRank() != 1)
                        throw new SerializationException("Multi dimensional arrays aren't supported.");

                    ushort length = data.ReadUShort();
                    if (length == ushort.MaxValue)
                        return null;

                    Type elementType = type.GetElementType();
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

                        Array keys = (Array)ReadSyncObject(data, arguments[0].MakeArrayType());
                        if (keys == null)
                            return null;

                        Array values = (Array)ReadSyncObject(data, arguments[1].MakeArrayType());

                        IDictionary dictionary = (IDictionary)Activator.CreateInstance(type);
                        for (int i = 0; i < keys.Length; i++)
                        {
                            var key = keys.GetValue(i);
                            if (key != null)
                                dictionary.Add(key, values.GetValue(i));
                        }

                        return dictionary;
                    }

                    if (genericTypeDefinition == typeof(Pair<,>))
                    {
                        Type[] arguments = type.GetGenericArguments();
                        object[] parameters =
                        {
                            ReadSyncObject(data, arguments[0]),
                            ReadSyncObject(data, arguments[1]),
                        };

                        return type.GetConstructors().First().Invoke(parameters);
                    }

                    if (typeof(ITuple).IsAssignableFrom(genericTypeDefinition)) // ValueTuple or Tuple
                    {
                        Type[] arguments = type.GetGenericArguments();

                        int size = data.ReadInt32();
                        object[] values = new object[size];

                        for (int i = 0; i < size; i++)
                            values[i] = ReadSyncObject(data, arguments[i]);

                        return type.GetConstructors().First().Invoke(values);
                    }
                }

                if (typeof(ISyncSimple).IsAssignableFrom(type))
                {
                    var obj = MpUtil.NewObjectNoCtor(type);
                    foreach (var field in AccessTools.GetDeclaredFields(type))
                        field.SetValue(obj, ReadSyncObject(data, field.FieldType));
                    return obj;
                }

                // Def is a special case until the workers can read their own type
                if (typeof(Def).IsAssignableFrom(type))
                {
                    ushort defTypeIndex = data.ReadUShort();
                    if (defTypeIndex == ushort.MaxValue)
                        return null;

                    ushort shortHash = data.ReadUShort();

                    var defType = DefSerialization.DefTypes[defTypeIndex];
                    var def = DefSerialization.GetDef(defType, shortHash);

                    if (def == null)
                        throw new SerializationException($"Couldn't find {defType} with short hash {shortHash}");

                    return def;
                }

                // Designators can't be handled by SyncWorkers due to the type change
                if (typeof(Designator).IsAssignableFrom(type))
                {
                    ushort desId = ReadSync<ushort>(data);
                    type = ImplSerialization.designatorTypes[desId]; // Replaces the type!
                }

                // Where the magic happens
                if (SyncDict.syncWorkers.TryGetValue(type, out var syncWorkerEntry))
                {
                    object res = null;

                    if (syncWorkerEntry.shouldConstruct || type.IsValueType)
                        res = Activator.CreateInstance(type);

                    syncWorkerEntry.Invoke(new ReadingSyncWorker(data), ref res);

                    return res;
                }

                throw new SerializationException("No reader for type " + type);
            }
            catch
            {
                Log.Error($"Multiplayer: Error reading type: {type}");
                throw;
            }
        }

        public static void WriteSync<T>(ByteWriter data, T obj)
        {
            WriteSyncObject(data, obj, typeof(T));
        }

        public static void WriteSyncObject(ByteWriter data, object obj, SyncType syncType)
        {
            MpContext context = data.MpContext();
            Type type = syncType.type;

            var log = (data as LoggingByteWriter)?.Log;
            log?.Enter($"{type.FullName}: {obj ?? "null"}");

            if (obj != null && !type.IsAssignableFrom(obj.GetType()))
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

                if (SyncDictFast.syncWorkers.TryGetValue(type, out var syncWorkerEntryEarly)) {
                    syncWorkerEntryEarly.Invoke(new WritingSyncWorker(data), ref obj);

                    return;
                }

                if (syncType.expose)
                {
                    if (!typeof(IExposable).IsAssignableFrom(type))
                        throw new SerializationException($"Type {type} can't be exposed because it isn't IExposable");

                    IExposable exposable = obj as IExposable;
                    byte[] xmlData = ScribeUtil.WriteExposable(exposable);
                    LogXML(log, xmlData);
                    data.WritePrefixedBytes(xmlData);

                    return;
                }

                if (typeof(ISynchronizable).IsAssignableFrom(type))
                {
                    ((ISynchronizable) obj).Sync(new WritingSyncWorker(data));
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

                    Type elementType = type.GetElementType();
                    Array arr = (Array)obj;

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
                        IList list = (IList)obj;
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

                    if (genericTypeDefinition == typeof(IEnumerable<>))
                    {
                        IEnumerable e = (IEnumerable)obj;
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
                        IDictionary dictionary = (IDictionary)obj;
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

                    if (genericTypeDefinition == typeof(Pair<,>))
                    {
                        Type[] arguments = type.GetGenericArguments();

                        WriteSyncObject(data, AccessTools.DeclaredField(type, "first").GetValue(obj), arguments[0]);
                        WriteSyncObject(data, AccessTools.DeclaredField(type, "second").GetValue(obj), arguments[1]);

                        return;
                    }

                    if (typeof(ITuple).IsAssignableFrom(genericTypeDefinition)) // ValueTuple or Tuple
                    {
                        Type[] arguments = type.GetGenericArguments();
                        ITuple tuple = (ITuple)obj;

                        data.WriteInt32(tuple.Length);

                        for (int i = 0; i < tuple.Length; i++)
                            WriteSyncObject(data, tuple[i], arguments[i]);

                        return;
                    }
                }

                if (typeof(ISyncSimple).IsAssignableFrom(type))
                {
                    foreach (var field in AccessTools.GetDeclaredFields(type))
                        WriteSyncObject(data, field.GetValue(obj), field.FieldType);
                    return;
                }

                // Special case
                if (typeof(Def).IsAssignableFrom(type))
                {
                    if (obj is not Def def)
                    {
                        data.WriteUShort(ushort.MaxValue);
                        return;
                    }

                    var defTypeIndex = Array.IndexOf(DefSerialization.DefTypes, def.GetType());
                    if (defTypeIndex == -1)
                        throw new SerializationException($"Unknown def type {def.GetType()}");

                    data.WriteUShort((ushort)defTypeIndex);
                    data.WriteUShort(def.shortHash);

                    return;
                }

                // Special case for Designators to change the type
                if (typeof(Designator).IsAssignableFrom(type))
                {
                    data.WriteUShort((ushort) Array.IndexOf(ImplSerialization.designatorTypes, obj.GetType()));
                }

                // Where the magic happens
                if (SyncDict.syncWorkers.TryGetValue(type, out var syncWorkerEntry))
                {
                    syncWorkerEntry.Invoke(new WritingSyncWorker(data), ref obj);

                    return;
                }

                log?.Node("No writer for " + type);
                throw new SerializationException("No writer for type " + type);
            }
            catch
            {
                Log.Error($"Multiplayer: Error writing type: {type}, obj: {obj}, obj type: {obj?.GetType()}");
                throw;
            }
            finally
            {
                log?.Exit();
            }
        }

        internal static T GetAnyParent<T>(Thing thing) where T : class
        {
            if (thing is T t)
                return t;

            for (var parentHolder = thing.ParentHolder; parentHolder != null; parentHolder = parentHolder.ParentHolder)
                if (parentHolder is T t2)
                    return t2;

            return null;
        }

        internal static string ThingHolderString(Thing thing)
        {
            StringBuilder builder = new StringBuilder(thing.ToString());

            for (var parentHolder = thing.ParentHolder; parentHolder != null; parentHolder = parentHolder.ParentHolder)
            {
                builder.Insert(0, "=>");
                builder.Insert(0, parentHolder.ToString());
            }

            return builder.ToString();
        }

        private static void LogXML(SyncLogger log, byte[] xmlData)
        {
            if (log == null) return;

            var reader = XmlReader.Create(new MemoryStream(xmlData));

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    string name = reader.Name;
                    if (reader.GetAttribute("IsNull") == "True")
                        name += " (IsNull)";

                    if (reader.IsEmptyElement)
                        log.Node(name);
                    else
                        log.Enter(name);
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    log.Exit();
                }
                else if (reader.NodeType == XmlNodeType.Text)
                {
                    log.AppendToCurrentName($": {reader.Value}");
                }
            }
        }
    }
}
