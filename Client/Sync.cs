using Harmony;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;
using Verse.AI;
using MultiType = Verse.Pair<System.Type, string>;

namespace Multiplayer.Client
{
    public abstract class SyncHandler
    {
        public readonly int syncId;

        public SyncHandler(int syncId)
        {
            this.syncId = syncId;
        }

        public abstract void Read(ByteReader data);
    }

    public class SyncField : SyncHandler
    {
        public readonly Type targetType;
        public readonly string memberPath;
        public readonly Type fieldType;
        public readonly Func<object, object> instanceFunc;

        public SyncField(int syncId, Type targetType, string memberPath) : base(syncId)
        {
            this.targetType = targetType;
            this.memberPath = targetType + "/" + memberPath;

            fieldType = MpReflection.PropertyOrFieldType(this.memberPath);
        }

        /// <summary>
        /// Returns whether the sync has been done
        /// </summary>
        public bool DoSync(object target, object value)
        {
            if (!Multiplayer.ShouldSync)
                return false;

            int mapId = Sync.GetMap(target)?.uniqueID ?? ScheduledCommand.GLOBAL;
            LoggingByteWriter writer = new LoggingByteWriter();
            writer.LogNode("Sync field " + memberPath);

            writer.WriteInt32(syncId);

            Sync.WriteContext(writer, mapId);
            Sync.WriteSyncObject(writer, target, targetType);
            Sync.WriteSyncObject(writer, value, fieldType);

            Multiplayer.packetLog.nodes.Add(writer.current);

            Multiplayer.client.SendCommand(CommandType.SYNC, mapId, writer.GetArray());

            return true;
        }

        public override void Read(ByteReader data)
        {
            object target = Sync.ReadSyncObject(data, targetType);
            object value = Sync.ReadSyncObject(data, fieldType);

            MpLog.Log("Set " + memberPath + " in " + target + " to " + value + " map " + data.context);
            MpReflection.SetPropertyOrField(target, memberPath, value);
        }
    }

    public class SyncMethod : SyncHandler
    {
        public readonly Type targetType;
        public readonly string instancePath;

        public readonly MethodInfo method;
        public readonly Type[] argTypes;

        public SyncMethod(int syncId, Type targetType, string instancePath, string methodName, Type[] argTypes) : base(syncId)
        {
            this.targetType = targetType;

            Type instanceType = targetType;
            if (!instancePath.NullOrEmpty())
            {
                this.instancePath = instanceType + "/" + instancePath;
                instanceType = MpReflection.PropertyOrFieldType(this.instancePath);
            }

            method = AccessTools.Method(instanceType, methodName) ?? throw new Exception($"Couldn't find method {instanceType}::{methodName}");

            if (argTypes.Length == 0)
                this.argTypes = method.GetParameters().Types();
            else if (argTypes.Length != method.GetParameters().Length)
                throw new Exception("Wrong parameter count for method " + method);
            else
                this.argTypes = argTypes;
        }

        /// <summary>
        /// Returns whether the sync has been done
        /// </summary>
        public bool DoSync(object target, params object[] args)
        {
            if (!Multiplayer.ShouldSync)
                return false;

            int mapId = Sync.GetMap(target)?.uniqueID ?? ScheduledCommand.GLOBAL;
            LoggingByteWriter writer = new LoggingByteWriter();
            writer.LogNode("Sync method " + method);

            writer.WriteInt32(syncId);

            Sync.WriteContext(writer, mapId);
            Sync.WriteSyncObject(writer, target, targetType);
            Sync.WriteSyncObjects(writer, args, argTypes);

            Multiplayer.packetLog.nodes.Add(writer.current);

            Multiplayer.client.SendCommand(CommandType.SYNC, mapId, writer.GetArray());

            return true;
        }

        public override void Read(ByteReader data)
        {
            object target = Sync.ReadSyncObject(data, targetType);
            if (!instancePath.NullOrEmpty())
                target = target.GetPropertyOrField(instancePath);

            object[] parameters = Sync.ReadSyncObjects(data, argTypes);

            MpLog.Log("Invoked " + method + " on " + target + " with " + parameters.Length + " params");
            method.Invoke(target, parameters);
        }
    }

    public class SyncDelegate : SyncHandler
    {
        public readonly Type delegateType;
        public readonly MethodInfo method;

        private Type[] argTypes;
        public string[] fieldPaths;
        private Type[] fieldTypes;

        public MethodInfo patch;

        public SyncDelegate(int syncId, Type delegateType, string delegateMethod, string[] fieldPaths) : base(syncId)
        {
            this.delegateType = delegateType;
            method = AccessTools.Method(delegateType, delegateMethod);

            argTypes = method.GetParameters().Types();

            this.fieldPaths = fieldPaths;
            if (fieldPaths == null)
            {
                List<string> fieldList = new List<string>();
                Sync.AllDelegateFieldsRecursive(delegateType, path => { fieldList.Add(path); return false; });
                this.fieldPaths = fieldList.ToArray();
            }
            else
            {
                for (int i = 0; i < this.fieldPaths.Length; i++)
                {
                    this.fieldPaths[i] = MpReflection.AppendType(this.fieldPaths[i], delegateType);
                }
            }

            fieldTypes = this.fieldPaths.Select(path => MpReflection.PropertyOrFieldType(path)).ToArray();
        }

        public bool DoSync(object delegateInstance, int mapId, params object[] args)
        {
            if (!Multiplayer.ShouldSync)
                return false;

            LoggingByteWriter writer = new LoggingByteWriter();
            writer.LogNode($"Sync delegate: {delegateType} method: {method}");
            writer.LogNode("Patch: " + patch.FullDescription());

            writer.WriteInt32(syncId);

            Sync.WriteContext(writer, mapId);

            for (int i = 0; i < fieldPaths.Length; i++)
                Sync.WriteSyncObject(writer, delegateInstance.GetPropertyOrField(fieldPaths[i]), fieldTypes[i]);

            Sync.WriteSyncObjects(writer, args, argTypes);

            Multiplayer.packetLog.nodes.Add(writer.current);

            Multiplayer.client.SendCommand(CommandType.SYNC, mapId, writer.GetArray());

            return true;
        }

        public override void Read(ByteReader data)
        {
            object target = Activator.CreateInstance(delegateType);
            for (int i = 0; i < fieldPaths.Length; i++)
                MpReflection.SetPropertyOrField(target, fieldPaths[i], Sync.ReadSyncObject(data, fieldTypes[i]));

            object[] parameters = Sync.ReadSyncObjects(data, argTypes);

            MpLog.Log("Invoked delegate method " + method + " " + delegateType);
            method.Invoke(target, parameters);
        }
    }

    public static class Sync
    {
        private static List<SyncHandler> handlers = new List<SyncHandler>();
        private static Dictionary<MethodBase, SyncDelegate> delegates = new Dictionary<MethodBase, SyncDelegate>();

        private static List<SyncData> watchedData = new List<SyncData>();
        private static bool syncing;

        public static MultiTarget storageTarget = new MultiTarget()
        {
            { typeof(Building_Grave), "GetStoreSettings" },
            { typeof(Building_Storage), "GetStoreSettings" },
            { typeof(CompChangeableProjectile), "GetStoreSettings" },
            { typeof(Zone_Stockpile), "GetStoreSettings" }
        };

        public static MultiTarget thingFilterTarget = new MultiTarget()
        {
            { storageTarget, "filter" },
            { typeof(Bill), "ingredientFilter" },
            { typeof(Outfit), "filter" }
        };

        private static void Prefix(ref bool __state)
        {
            if (!syncing && Multiplayer.ShouldSync)
            {
                syncing = __state = true;
            }
        }

        private static void Postfix(ref bool __state)
        {
            if (!__state)
                return;

            foreach (SyncData data in watchedData)
            {
                object newValue = data.target.GetPropertyOrField(data.handler.memberPath);

                if (!Equals(newValue, data.value))
                {
                    MpReflection.SetPropertyOrField(data.target, data.handler.memberPath, data.value);
                    data.handler.DoSync(data.target, newValue);
                }
            }

            watchedData.Clear();
            syncing = __state = false;
        }

        public static SyncMethod Method(Type targetType, string methodName)
        {
            return Method(targetType, null, methodName);
        }

        public static SyncMethod Method(Type targetType, string methodName, params Type[] argTypes)
        {
            return Method(targetType, null, methodName, argTypes);
        }

        public static SyncMethod Method(Type targetType, string instancePath, string methodName, params Type[] argTypes)
        {
            SyncMethod handler = new SyncMethod(handlers.Count, targetType, instancePath, methodName, argTypes);
            handlers.Add(handler);
            return handler;
        }

        public static SyncMethod[] MethodMultiTarget(MultiTarget targetType, string methodName)
        {
            return targetType.Select(type => Method(type.First, type.Second, methodName)).ToArray();
        }

        public static SyncField Field(Type targetType, string fieldName)
        {
            SyncField handler = new SyncField(handlers.Count, targetType, fieldName);
            handlers.Add(handler);
            return handler;
        }

        public static SyncField Field(Type targetType, string instancePath, string fieldName)
        {
            SyncField handler = new SyncField(handlers.Count, targetType, instancePath + "/" + fieldName);
            handlers.Add(handler);
            return handler;
        }

        public static SyncField[] FieldMultiTarget(MultiTarget targetType, string fieldName)
        {
            return targetType.Select(type => Field(type.First, type.Second, fieldName)).ToArray();
        }

        public static SyncField[] Fields(Type targetType, string instancePath, params string[] memberPaths)
        {
            return memberPaths.Select(path => Field(targetType, instancePath, path)).ToArray();
        }

        /// <summary>
        /// Returns whether the sync has been done
        /// </summary>
        public static bool Delegate(object instance, MethodBase originalMethod, object mapProvider, params object[] args)
        {
            SyncDelegate handler = delegates[originalMethod];
            int mapId = GetMap(mapProvider)?.uniqueID ?? ScheduledCommand.GLOBAL;

            if (mapProvider == MapProviderMode.ANY_FIELD)
            {
                foreach (string path in handler.fieldPaths)
                {
                    object obj = instance.GetPropertyOrField(path);
                    Map map = GetMap(obj);
                    if (map != null)
                    {
                        mapId = map.uniqueID;
                        break;
                    }
                }
            }

            args = args ?? new object[0];
            return handler.DoSync(instance, mapId, args);
        }

        public static bool AllDelegateFieldsRecursive(Type type, Func<string, bool> getter, string path = "")
        {
            if (path.NullOrEmpty())
                path = type.ToString();

            foreach (FieldInfo field in AccessTools.GetDeclaredFields(type))
            {
                string curPath = path + "/" + field.Name;

                if (getter(curPath))
                    return true;

                if (Attribute.GetCustomAttribute(field.FieldType, typeof(CompilerGeneratedAttribute)) == null)
                    continue;

                if (AllDelegateFieldsRecursive(field.FieldType, getter, curPath))
                    return true;
            }

            return false;
        }

        public static void RegisterSyncDelegates(Type inType)
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(inType))
            {
                if (!method.TryGetAttribute(out SyncDelegateAttribute syncAttr))
                    continue;

                foreach (MpPrefix patchAttr in method.AllAttributes<MpPrefix>())
                {
                    Type type = patchAttr.type ?? MpReflection.GetTypeByName(patchAttr.typeName);
                    SyncDelegate handler = new SyncDelegate(handlers.Count, type, patchAttr.method, syncAttr.fields);
                    handler.patch = method;
                    delegates[handler.method] = handler;
                    handlers.Add(handler);
                }
            }
        }

        public static bool CanSerialize(Type type)
        {
            return type.GetConstructors(AccessTools.all).Any(c => c.GetParameters().Length == 0);
        }

        public static void RegisterFieldPatches(Type type)
        {
            HarmonyMethod prefix = new HarmonyMethod(AccessTools.Method(typeof(Sync), "Prefix"));
            HarmonyMethod postfix = new HarmonyMethod(AccessTools.Method(typeof(Sync), "Postfix"));

            List<MethodBase> patched = MpPatch.DoPatches(type);
            new PatchProcessor(Multiplayer.harmony, patched, prefix, postfix).Patch();
        }

        public static void Watch(this SyncField field, object target = null)
        {
            if (!Multiplayer.ShouldSync) return;

            object value = target.GetPropertyOrField(field.memberPath);
            watchedData.Add(new SyncData(target, field, value));
        }

        public static void Watch(this SyncField[] group, object target = null)
        {
            foreach (SyncField sync in group)
                if (target == null || sync.targetType.IsAssignableFrom(target.GetType()))
                    sync.Watch(target);
        }

        public static bool DoSync(this SyncMethod[] group, object target, params object[] args)
        {
            foreach (SyncMethod sync in group)
                if (target == null || sync.targetType.IsAssignableFrom(target.GetType()))
                    return sync.DoSync(target);

            return false;
        }

        public static void HandleCmd(ByteReader data)
        {
            int syncId = data.ReadInt32();
            SyncHandler handler = handlers[syncId];

            if (data.context is Map)
            {
                IntVec3 mouseCell = ReadSync<IntVec3>(data);
                MouseCellPatch.result = mouseCell;

                Thing selThing = ReadSync<Thing>(data);
                ITabSelThingPatch.result = selThing;
            }

            bool shouldQueue = data.ReadBool();
            KeyIsDownPatch.result = shouldQueue;
            KeyIsDownPatch.forKey = KeyBindingDefOf.QueueOrder;

            try
            {
                handler.Read(data);
            }
            finally
            {
                MouseCellPatch.result = null;
                KeyIsDownPatch.result = null;
                KeyIsDownPatch.forKey = null;
                ITabSelThingPatch.result = null;
            }
        }

        public static Thing selThingContext; // for ITabs

        public static void WriteContext(ByteWriter data, int mapId)
        {
            if (mapId >= 0)
            {
                WriteSync(data, UI.MouseCell());
                WriteSync(data, selThingContext);
            }

            data.WriteBool(KeyBindingDefOf.QueueOrder.IsDownEvent);
        }

        public static Map GetMap(object obj)
        {
            if (obj is Thing thing)
                return thing.Map;
            else if (obj is ThingComp comp)
                return comp.parent.Map;
            else if (obj is Zone zone)
                return zone.Map;
            else if (obj is Bill bill)
                return bill.Map;
            else if (obj is BillStack bills)
                return bills.billGiver.Map;

            return null;
        }

        static ReaderDictionary readers = new ReaderDictionary
        {
            { data => data.ReadInt32() },
            { data => data.ReadBool() },
            { data => data.ReadString() },
            { data => data.ReadLong() },
            { data => data.ReadFloat() },
            { data => data.ReadDouble() },
            { data => ReadSync<Pawn>(data).mindState.priorityWork },
            { data => ReadSync<Pawn>(data).playerSettings },
            { data => new FloatRange(data.ReadFloat(), data.ReadFloat()) },
            { data => new IntRange(data.ReadInt32(), data.ReadInt32()) },
            { data => new QualityRange(ReadSync<QualityCategory>(data), ReadSync<QualityCategory>(data)) },
            { data => new IntVec3(data.ReadInt32(), data.ReadInt32(), data.ReadInt32()) },
            { data => new ITab_Bills() },
            {
                data =>
                {
                    Thing thing = ReadSync<Thing>(data);
                    if (thing != null)
                        return new LocalTargetInfo(thing);
                    else
                        return new LocalTargetInfo(ReadSync<IntVec3>(data));
                }
            }
        };

        static WriterDictionary writers = new WriterDictionary
        {
            { (ByteWriter data, int i) => data.WriteInt32(i) },
            { (ByteWriter data, bool b) => data.WriteBool(b) },
            { (ByteWriter data, string s) => data.WriteString(s) },
            { (ByteWriter data, long l) => data.WriteLong(l) },
            { (ByteWriter data, float f) => data.WriteFloat(f) },
            { (ByteWriter data, double d) => data.WriteDouble(d) },
            { (ByteWriter data, PriorityWork work) => WriteSync(data, work.GetPropertyOrField("pawn") as Pawn) },
            { (ByteWriter data, Pawn_PlayerSettings settings) => WriteSync(data, settings.GetPropertyOrField("pawn") as Pawn) },
            { (ByteWriter data, FloatRange range) => { data.WriteFloat(range.min); data.WriteFloat(range.max); }},
            { (ByteWriter data, IntRange range) => { data.WriteInt32(range.min); data.WriteInt32(range.max); }},
            { (ByteWriter data, QualityRange range) => { WriteSync(data, range.min); WriteSync(data, range.max); }},
            { (ByteWriter data, IntVec3 vec) => { data.WriteInt32(vec.x); data.WriteInt32(vec.y); data.WriteInt32(vec.z); }},
            { (ByteWriter data, ITab_Bills tag) => {} },
            {
                (ByteWriter data, LocalTargetInfo info) =>
                {
                    WriteSync(data, info.Thing);
                    if (!info.HasThing)
                        WriteSync(data, info.Cell);
                }
            }
        };

        public static T ReadSync<T>(ByteReader data)
        {
            return (T)ReadSyncObject(data, typeof(T));
        }

        private static MethodInfo ReadExposable = AccessTools.Method(typeof(ScribeUtil), "ReadExposable");

        public static object ReadSyncObject(ByteReader data, Type type)
        {
            Map map = data.context as Map;

            if (type.IsEnum)
            {
                return Enum.ToObject(type, data.ReadInt32());
            }
            else if (type.IsGenericType)
            {
                if (type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    Type listType = type.GetGenericArguments()[0];
                    int size = data.ReadInt32();
                    IList list = Activator.CreateInstance(type, size) as IList;
                    for (int j = 0; j < size; j++)
                        list.Add(ReadSyncObject(data, listType));
                    return list;
                }
                else if (type.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    bool hasValue = data.ReadBool();
                    if (!hasValue)
                        return Activator.CreateInstance(type);

                    Type nullableType = type.GetGenericArguments()[0];
                    return Activator.CreateInstance(type, ReadSyncObject(data, nullableType));
                }
                else if (type.GetGenericTypeDefinition() == typeof(Expose<>))
                {
                    Type exposableType = type.GetGenericArguments()[0];
                    byte[] exposableData = data.ReadPrefixedBytes();
                    return ReadExposable.MakeGenericMethod(exposableType).Invoke(null, new[] { exposableData, null });
                }
            }
            else if (typeof(ThinkNode).IsAssignableFrom(type))
            {
                // todo temporary
                return null;
            }
            else if (typeof(Area).IsAssignableFrom(type))
            {
                int areaId = data.ReadInt32();
                if (areaId == -1)
                    return null;

                return map.areaManager.AllAreas.FirstOrDefault(a => a.ID == areaId);
            }
            else if (typeof(Zone).IsAssignableFrom(type))
            {
                string name = data.ReadString();
                Log.Message("Reading zone " + name + " " + map);
                if (name.NullOrEmpty())
                    return null;

                return map.zoneManager.AllZones.FirstOrDefault(zone => zone.label == name);
            }
            else if (typeof(Def).IsAssignableFrom(type))
            {
                ushort shortHash = data.ReadUInt16();
                if (shortHash == 0)
                    return null;

                Type dbType = typeof(DefDatabase<>).MakeGenericType(type);
                return AccessTools.Method(dbType, "GetByShortHash").Invoke(null, new object[] { shortHash });
            }
            else if (typeof(Thing).IsAssignableFrom(type))
            {
                int thingId = data.ReadInt32();
                if (thingId == -1)
                    return null;

                ThingDef def = ReadSync<ThingDef>(data);
                return map.listerThings.ThingsOfDef(def).FirstOrDefault(t => t.thingIDNumber == thingId);
            }
            else if (typeof(ThingComp).IsAssignableFrom(type))
            {
                string compType = data.ReadString();
                if (compType.NullOrEmpty())
                    return null;

                ThingWithComps parent = ReadSync<Thing>(data) as ThingWithComps;
                if (parent == null)
                    return null;

                return parent.AllComps.FirstOrDefault(comp => comp.props.compClass.FullName == compType);
            }
            else if (typeof(WorkGiver).IsAssignableFrom(type))
            {
                WorkGiverDef def = ReadSync<WorkGiverDef>(data);
                return def?.Worker;
            }
            else if (typeof(BillStack) == type)
            {
                Thing thing = ReadSync<Thing>(data);
                if (thing is IBillGiver billGiver)
                    return billGiver.BillStack;
                return null;
            }
            else if (typeof(Bill).IsAssignableFrom(type))
            {
                BillStack billStack = ReadSync<BillStack>(data);
                if (billStack == null)
                    return null;

                int id = data.ReadInt32();
                return billStack.Bills.FirstOrDefault(bill => (int)bill.GetPropertyOrField("loadID") == id);
            }
            else if (typeof(BodyPartRecord) == type)
            {
                BodyDef body = ReadSync<BodyDef>(data);
                int partIndex = data.ReadInt32();

                return body.GetPartAtIndex(partIndex);
            }
            else if (readers.TryGetValue(type, out Func<ByteReader, object> reader))
            {
                return reader(data);
            }

            throw new SerializationException("No reader for type " + type);
        }

        public static object[] ReadSyncObjects(ByteReader data, Type[] spec)
        {
            return spec.Select(type => ReadSyncObject(data, type)).ToArray();
        }

        public static void WriteSync<T>(ByteWriter data, T obj)
        {
            WriteSyncObject(data, obj, typeof(T));
        }

        public static void WriteSyncObject(ByteWriter data, object obj)
        {
            WriteSyncObject(data, obj, obj.GetType());
        }

        public static void WriteSyncObject(ByteWriter data, object obj, Type type)
        {
            LoggingByteWriter log = data as LoggingByteWriter;
            log?.LogEnter(type.FullName + ": " + (obj ?? "null"));

            try
            {
                if (type.IsEnum)
                {
                    data.WriteInt32(Convert.ToInt32(obj));
                }
                else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    Type listType = type.GetGenericArguments()[0];
                    IList list = obj as IList;
                    data.WriteInt32(list.Count);
                    foreach (object e in list)
                        WriteSyncObject(data, e, listType);
                }
                else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    Type nullableType = type.GetGenericArguments()[0];
                    bool hasValue = (bool)obj.GetPropertyOrField("HasValue");

                    data.WriteBool(hasValue);
                    if (hasValue)
                        WriteSyncObject(data, obj.GetPropertyOrField("Value"), nullableType);
                }
                else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Expose<>))
                {
                    Type exposableType = type.GetGenericArguments()[0];
                    if (!exposableType.IsAssignableFrom(obj.GetType()))
                        throw new SerializationException($"Expose<> types {obj.GetType()} and {exposableType} don't match");

                    IExposable exposable = obj as IExposable;
                    data.WritePrefixedBytes(ScribeUtil.WriteExposable(exposable));
                }
                else if (typeof(ThinkNode).IsAssignableFrom(type))
                {
                    // todo temporary
                }
                else if (typeof(Area).IsAssignableFrom(type))
                {
                    data.WriteInt32(obj is Area area ? area.ID : -1);
                }
                else if (typeof(Zone).IsAssignableFrom(type))
                {
                    data.WriteString(obj is Zone zone ? zone.label : "");
                }
                else if (typeof(Def).IsAssignableFrom(type))
                {
                    data.WriteUInt16(obj is Def def ? def.shortHash : (ushort)0);
                }
                else if (typeof(ThingComp).IsAssignableFrom(type))
                {
                    if (obj is ThingComp comp)
                    {
                        data.WriteString(comp.props.compClass.FullName);
                        WriteSync<Thing>(data, comp.parent);
                    }
                    else
                    {
                        data.WriteString("");
                    }
                }
                else if (typeof(WorkGiver).IsAssignableFrom(type))
                {
                    WorkGiver workGiver = obj as WorkGiver;
                    if (workGiver == null)
                        return;

                    WriteSync(data, workGiver);
                }
                else if (typeof(Thing).IsAssignableFrom(type))
                {
                    Thing thing = (Thing)obj;
                    if (thing == null)
                    {
                        data.WriteInt32(-1);
                        return;
                    }

                    data.WriteInt32(thing.thingIDNumber);
                    WriteSync(data, thing.def);
                }
                else if (typeof(BillStack) == type)
                {
                    Thing billGiver = (obj as BillStack)?.billGiver as Thing;
                    WriteSync(data, billGiver);
                }
                else if (typeof(Bill).IsAssignableFrom(type))
                {
                    Bill bill = obj as Bill;
                    WriteSync(data, bill.billStack);
                    data.WriteInt32((int)bill.GetPropertyOrField("loadID"));
                }
                else if (typeof(BodyPartRecord) == type)
                {
                    BodyPartRecord part = obj as BodyPartRecord;
                    BodyDef body = (from b in DefDatabase<BodyDef>.AllDefsListForReading
                                    from p in b.AllParts
                                    where p == part
                                    select b).FirstOrDefault();

                    if (body == null)
                        throw new SerializationException($"Couldn't find body for body part: {part}");

                    WriteSync(data, body);
                    data.WriteInt32(body.GetIndexOfPart(part));
                }
                else if (writers.TryGetValue(type, out Action<ByteWriter, object> writer))
                {
                    writer(data, obj);
                }
                else
                {
                    log.LogNode("No writer for " + type);
                    throw new SerializationException("No writer for type " + type);
                }
            }
            finally
            {
                log?.LogExit();
            }
        }

        public static void WriteSyncObjects(ByteWriter data, object[] objs, Type[] spec)
        {
            for (int i = 0; i < spec.Length; i++)
                WriteSyncObject(data, objs[i], spec[i]);
        }
    }

    public static class MapProviderMode
    {
        public static readonly object ANY_FIELD = new object();
    }

    public class SyncDelegateAttribute : Attribute
    {
        public readonly string[] fields;

        public SyncDelegateAttribute()
        {
        }

        public SyncDelegateAttribute(params string[] fields)
        {
            this.fields = fields;
        }
    }

    public class Expose<T> { }

    public class MultiTarget : IEnumerable<MultiType>
    {
        private List<MultiType> types = new List<MultiType>();

        public void Add(Type type, string path)
        {
            types.Add(new MultiType(type, path));
        }

        public void Add(MultiTarget type, string path)
        {
            foreach (MultiType multiType in type)
                Add(multiType.First, multiType.Second + "/" + path);
        }

        public IEnumerator<MultiType> GetEnumerator()
        {
            return types.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return types.GetEnumerator();
        }
    }

    class ReaderDictionary : OrderedDictionary<Type, Func<ByteReader, object>>
    {
        public void Add<T>(Func<ByteReader, T> writer)
        {
            Add(typeof(T), data => writer(data));
        }
    }

    class WriterDictionary : OrderedDictionary<Type, Action<ByteWriter, object>>
    {
        public void Add<T>(Action<ByteWriter, T> writer)
        {
            Add(typeof(T), (data, o) => writer(data, (T)o));
        }
    }

    abstract class OrderedDictionary<K, V> : IEnumerable
    {
        private OrderedDictionary dict = new OrderedDictionary();

        protected void Add(K key, V value)
        {
            dict.Add(key, value);
        }

        public bool TryGetValue(K key, out V value)
        {
            value = (V)dict[key];
            return value != null;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return dict.GetEnumerator();
        }
    }

    public class SerializationException : Exception
    {
        public SerializationException(string msg) : base(msg)
        {
        }
    }

    public struct SyncData
    {
        public readonly object target;
        public readonly SyncField handler;
        public readonly object value;

        public SyncData(object target, SyncField handler, object value)
        {
            this.target = target;
            this.handler = handler;
            this.value = value;
        }
    }

    public class LoggingByteWriter : ByteWriter
    {
        public LogNode current = new LogNode("Root");

        public override void WriteInt32(int val)
        {
            LogNode("int: " + val);
            base.WriteInt32(val);
        }

        public override void WriteBool(bool val)
        {
            LogNode("bool: " + val);
            base.WriteBool(val);
        }

        public override void WriteDouble(double val)
        {
            LogNode("double: " + val);
            base.WriteDouble(val);
        }

        public override void WriteUInt16(ushort val)
        {
            LogNode("ushort: " + val);
            base.WriteUInt16(val);
        }

        public override void WriteFloat(float val)
        {
            LogNode("float: " + val);
            base.WriteFloat(val);
        }

        public override void WriteLong(long val)
        {
            LogNode("long: " + val);
            base.WriteLong(val);
        }

        public override void WritePrefixedBytes(byte[] bytes)
        {
            LogEnter("byte[]");
            base.WritePrefixedBytes(bytes);
            LogExit();
        }

        public override void WriteString(string s)
        {
            LogEnter("string: " + s);
            base.WriteString(s);
            LogExit();
        }

        public LogNode LogNode(string text)
        {
            LogNode node = new LogNode(text, current);
            current.children.Add(node);
            return node;
        }

        public void LogEnter(string text)
        {
            current = LogNode(text);
        }

        public void LogExit()
        {
            current = current.parent;
        }

        public void Print()
        {
            Print(current, 1);
        }

        private void Print(LogNode node, int depth)
        {
            Log.Message(new string(' ', depth) + node.text);
            foreach (LogNode child in node.children)
                Print(child, depth + 1);
        }
    }

    public class LogNode
    {
        public LogNode parent;
        public List<LogNode> children = new List<LogNode>();
        public string text;
        public bool expand;

        public LogNode(string text, LogNode parent = null)
        {
            this.text = text;
            this.parent = parent;
        }
    }

}
