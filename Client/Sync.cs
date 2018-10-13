using Harmony;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    public abstract class SyncHandler
    {
        public readonly int syncId;
        protected bool hasContext;

        public bool HasContext => hasContext;

        protected SyncHandler(int syncId)
        {
            this.syncId = syncId;
        }

        public abstract void Handle(ByteReader data);

        public override int GetHashCode() => syncId;

        public override bool Equals(object obj) => (obj as SyncHandler)?.syncId == syncId;
    }

    public class SyncField : SyncHandler
    {
        public readonly Type targetType;
        public readonly string memberPath;
        public readonly Type fieldType;
        public readonly Type indexType;

        public bool bufferChanges;
        public bool inGameLoop;

        private Action<object, object> preApply;
        private Action<object, object> postApply;

        public SyncField(int syncId, Type targetType, string memberPath) : base(syncId)
        {
            this.targetType = targetType;
            this.memberPath = targetType + "/" + memberPath;
            fieldType = MpReflection.PathType(this.memberPath);
            indexType = MpReflection.IndexType(this.memberPath);
        }

        /// <summary>
        /// Returns whether the original should cancelled
        /// </summary>
        public bool DoSync(object target, object value, object index = null)
        {
            if (!(inGameLoop || Multiplayer.ShouldSync))
                return false;

            LoggingByteWriter writer = new LoggingByteWriter();
            MpContext context = writer.MpContext();
            writer.LogNode("Sync field " + memberPath);

            writer.WriteInt32(syncId);

            int mapId = ScheduledCommand.Global;
            if (targetType != null)
            {
                Sync.WriteSyncObject(writer, target, targetType);
                if (context.map is Map map)
                    mapId = map.uniqueID;
            }

            Sync.WriteSyncObject(writer, value, fieldType);
            if (indexType != null)
                Sync.WriteSyncObject(writer, index, indexType);

            writer.LogNode("Map id: " + mapId);
            Multiplayer.PacketLog.nodes.Add(writer.current);

            Multiplayer.Client.SendCommand(CommandType.Sync, mapId, writer.GetArray());

            return true;
        }

        public override void Handle(ByteReader data)
        {
            object target = null;
            if (targetType != null)
                target = Sync.ReadSyncObject(data, targetType);
            object value = Sync.ReadSyncObject(data, fieldType);

            object index = null;
            if (indexType != null)
                index = Sync.ReadSyncObject(data, indexType);

            preApply?.Invoke(target, value);

            MpLog.Log($"Set {memberPath} in {target} to {value}, map {data.MpContext().map}, index {index}");
            MpReflection.SetValue(target, memberPath, value, index);

            postApply?.Invoke(target, value);
        }

        public SyncField PreApply(Action<object, object> action)
        {
            preApply = action;
            return this;
        }

        public SyncField PostApply(Action<object, object> action)
        {
            postApply = action;
            return this;
        }

        public SyncField SetBufferChanges()
        {
            Sync.bufferedChanges[this] = new Dictionary<Pair<object, object>, BufferData>();
            Sync.bufferedFields.Add(this);
            bufferChanges = true;
            return this;
        }

        public SyncField InGameLoop()
        {
            inGameLoop = true;
            return this;
        }
    }

    public class SyncMethod : SyncHandler
    {
        public readonly Type targetType;
        public readonly string instancePath;

        public readonly MethodInfo method;
        public Type[] argTypes;

        private int minTime = 100; // Milliseconds between resends
        private int lastSendTime;

        private Action<object, object[]> beforeCall;
        private Action<object, object[]> afterCall;

        public SyncMethod(int syncId, Type targetType, string instancePath, string methodName, params Type[] argTypes) : base(syncId)
        {
            this.targetType = targetType;

            Type instanceType = targetType;
            if (!instancePath.NullOrEmpty())
            {
                this.instancePath = instanceType + "/" + instancePath;
                instanceType = MpReflection.PathType(this.instancePath);
            }

            method = AccessTools.Method(instanceType, methodName, argTypes.Length > 0 ? Sync.TranslateArgTypes(argTypes) : null) ?? throw new Exception($"Couldn't find method {instanceType}::{methodName}");
            this.argTypes = CheckArgs(argTypes);
        }

        public SyncMethod(int syncId, Type targetType, MethodInfo method, params Type[] argTypes) : base(syncId)
        {
            this.method = method;
            this.targetType = targetType;
            this.argTypes = CheckArgs(argTypes);
        }

        private Type[] CheckArgs(Type[] argTypes)
        {
            if (argTypes.Length == 0)
                return method.GetParameters().Types();
            else if (argTypes.Length != method.GetParameters().Length)
                throw new Exception("Wrong parameter count for method " + method);

            return argTypes;
        }

        /// <summary>
        /// Returns whether the original should be cancelled
        /// </summary>
        public bool DoSync(object target, params object[] args)
        {
            if (!Multiplayer.ShouldSync)
                return false;

            // todo limit per specific target/argument
            //if (Environment.TickCount - lastSendTime < minTime)
            //    return true;

            LoggingByteWriter writer = new LoggingByteWriter();
            MpContext context = writer.MpContext();
            writer.LogNode("Sync method " + method);

            writer.WriteInt32(syncId);

            Sync.WriteContext(this, writer);

            int mapId = ScheduledCommand.Global;
            if (targetType != null)
            {
                Sync.WriteSyncObject(writer, target, targetType);
                if (context.map is Map map)
                    mapId = map.uniqueID;
            }

            for (int i = 0; i < argTypes.Length; i++)
            {
                Type type = argTypes[i];
                Sync.WriteSyncObject(writer, args[i], type);
                if (context.map is Map map)
                {
                    if (mapId != ScheduledCommand.Global && mapId != map.uniqueID)
                        throw new Exception("SyncMethod map mismatch");
                    mapId = map.uniqueID;
                }
            }

            writer.LogNode("Map id: " + mapId);
            Multiplayer.PacketLog.nodes.Add(writer.current);

            Multiplayer.Client.SendCommand(CommandType.Sync, mapId, writer.GetArray());

            lastSendTime = Environment.TickCount;

            return true;
        }

        public override void Handle(ByteReader data)
        {
            object target = null;

            if (targetType != null)
                target = Sync.ReadSyncObject(data, targetType);

            if (!instancePath.NullOrEmpty())
                target = target.GetPropertyOrField(instancePath);

            object[] args = Sync.ReadSyncObjects(data, argTypes);

            beforeCall?.Invoke(target, args);

            MpLog.Log("Invoked " + method + " on " + target + " with " + args.Length + " params " + args.ToStringSafeEnumerable());
            method.Invoke(target, args);

            afterCall?.Invoke(target, args);
        }

        public SyncMethod MinTime(int time)
        {
            minTime = time;
            return this;
        }

        public SyncMethod SetHasContext()
        {
            hasContext = true;
            return this;
        }

        public SyncMethod SetPreInvoke(Action<object, object[]> action)
        {
            beforeCall = action;
            return this;
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

        public SyncDelegate(int syncId, Type delegateType, MethodInfo method, string[] fieldPaths) : base(syncId)
        {
            this.hasContext = true;
            this.delegateType = delegateType;
            this.method = method;

            argTypes = method.GetParameters().Types();

            if (fieldPaths == null)
            {
                List<string> fieldList = new List<string>();
                Sync.AllDelegateFieldsRecursive(delegateType, path => { fieldList.Add(path); return false; });
                this.fieldPaths = fieldList.ToArray();
            }
            else
            {
                UniqueList<string> temp = new UniqueList<string>();
                foreach (string path in fieldPaths.Select(p => MpReflection.AppendType(p, delegateType)))
                {
                    string[] parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    string increment = parts[0] + "/" + parts[1];
                    for (int i = 2; i < parts.Length; i++)
                    {
                        if (!MpReflection.PathType(increment).IsCompilerGenerated())
                            break;
                        temp.Add(increment);
                        increment += "/" + parts[i];
                    }

                    temp.Add(path);
                }

                this.fieldPaths = temp.ToArray();
            }

            fieldTypes = this.fieldPaths.Select(path => MpReflection.PathType(path)).ToArray();
        }

        public bool DoSync(object delegateInstance, params object[] args)
        {
            if (!Multiplayer.ShouldSync)
                return false;

            LoggingByteWriter writer = new LoggingByteWriter();
            MpContext context = writer.MpContext();
            writer.LogNode($"Sync delegate: {delegateType} method: {method}");
            writer.LogNode("Patch: " + patch.FullDescription());

            writer.WriteInt32(syncId);

            Sync.WriteContext(this, writer);

            int mapId = ScheduledCommand.Global;

            IEnumerable<object> fields = fieldPaths.Select(p => delegateInstance.GetPropertyOrField(p));

            EnumerableHelper.ProcessCombined(fields.Concat(args), fieldTypes.Concat(argTypes), (obj, type) =>
            {
                if (type.IsCompilerGenerated())
                    return;

                Sync.WriteSyncObject(writer, obj, type);

                if (context.map is Map map)
                {
                    if (mapId != ScheduledCommand.Global && mapId != map.uniqueID)
                        throw new Exception("SyncDelegate map mismatch");
                    mapId = map.uniqueID;
                }
            });

            writer.LogNode("Map id: " + mapId);
            Multiplayer.PacketLog.nodes.Add(writer.current);

            Multiplayer.Client.SendCommand(CommandType.Sync, mapId, writer.GetArray());

            return true;
        }

        public override void Handle(ByteReader data)
        {
            object target = Activator.CreateInstance(delegateType);
            for (int i = 0; i < fieldPaths.Length; i++)
            {
                Type fieldType = fieldTypes[i];
                object value;

                if (fieldType.IsCompilerGenerated())
                    value = Activator.CreateInstance(fieldType);
                else
                    value = Sync.ReadSyncObject(data, fieldType);

                MpReflection.SetValue(target, fieldPaths[i], value);
            }

            object[] parameters = Sync.ReadSyncObjects(data, argTypes);

            MpLog.Log("Invoked delegate method " + method + " " + delegateType);
            method.Invoke(target, parameters);
        }
    }

    public class FieldData
    {
        public SyncField handler;
        public object target;
        public object oldValue;
        public object index;

        public FieldData(SyncField handler, object target, object oldValue, object index)
        {
            this.handler = handler;
            this.target = target;
            this.oldValue = oldValue;
            this.index = index;
        }
    }

    public class BufferData
    {
        public object actualValue;
        public object toSend;
        public int timestamp;
        public bool sent;

        public BufferData(object currentValue, object toSend)
        {
            this.actualValue = currentValue;
            this.toSend = toSend;
        }
    }

    public static class Sync
    {
        private static List<SyncHandler> handlers = new List<SyncHandler>();
        public static List<SyncField> bufferedFields = new List<SyncField>();
        private static Dictionary<MethodBase, SyncDelegate> syncDelegates = new Dictionary<MethodBase, SyncDelegate>();
        private static Dictionary<MethodBase, SyncMethod> syncMethods = new Dictionary<MethodBase, SyncMethod>();

        public static Dictionary<SyncField, Dictionary<Pair<object, object>, BufferData>> bufferedChanges = new Dictionary<SyncField, Dictionary<Pair<object, object>, BufferData>>();
        private static Stack<FieldData> watchedStack = new Stack<FieldData>();

        public static void FieldWatchPrefix()
        {
            if (Multiplayer.Client == null) return;
            watchedStack.Push(null); // Marker
        }

        // todo what happens on exceptions?
        public static void FieldWatchPostfix()
        {
            while (watchedStack.Count > 0)
            {
                FieldData data = watchedStack.Pop();
                if (data == null)
                    break; // The marker

                SyncField handler = data.handler;
                object newValue = data.target.GetPropertyOrField(handler.memberPath, data.index);
                bool changed = !Equals(newValue, data.oldValue);
                var cache = handler.bufferChanges ? bufferedChanges.GetValueSafe(handler) : null;

                if (cache != null && cache.TryGetValue(new Pair<object, object>(data.target, data.index), out BufferData cached))
                {
                    if (changed && cached.sent)
                        cached.sent = false;

                    cached.toSend = newValue;
                    data.target.SetPropertyOrField(handler.memberPath, cached.actualValue, data.index);
                    continue;
                }

                if (!changed) continue;

                if (cache != null)
                {
                    BufferData bufferData = new BufferData(data.oldValue, newValue);
                    cache[new Pair<object, object>(data.target, data.index)] = bufferData;
                }
                else
                {
                    handler.DoSync(data.target, newValue, data.index);
                }

                data.target.SetPropertyOrField(handler.memberPath, data.oldValue, data.index);
            }
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

        public static SyncMethod[] MethodMultiTarget(MultiTarget targetType, string methodName, params Type[] argTypes)
        {
            return targetType.Select(type => Method(type.First, type.Second, methodName, argTypes)).ToArray();
        }

        public static SyncField Field(Type targetType, string fieldName)
        {
            return Field(targetType, null, fieldName);
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
        /// Returns whether the original should be cancelled
        /// </summary>
        public static bool Delegate(object instance, MethodBase originalMethod, params object[] args)
        {
            SyncDelegate handler = syncDelegates[originalMethod];
            return handler.DoSync(instance, args ?? new object[0]);
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

                if (!field.FieldType.IsCompilerGenerated())
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
                    SyncDelegate handler = new SyncDelegate(handlers.Count, patchAttr.Type, patchAttr.Method, syncAttr.fields)
                    {
                        patch = method
                    };

                    syncDelegates[handler.method] = handler;
                    handlers.Add(handler);
                }
            }
        }

        public static SyncMethod RegisterSyncMethod(Type type, string methodName, params Type[] argTypes)
        {
            MethodInfo method = AccessTools.Method(type, methodName, argTypes.Length > 0 ? TranslateArgTypes(argTypes) : null);
            if (method == null)
                throw new Exception("Couldn't find method " + methodName + " in type " + type);
            return RegisterSyncMethod(method, argTypes);
        }

        public static SyncMethod RegisterSyncProperty(Type type, string propertyName, params Type[] argTypes)
        {
            return RegisterSyncMethod(type, "set_" + propertyName, argTypes);
        }

        public static void RegisterSyncMethods(Type inType)
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(inType))
            {
                if (!method.TryGetAttribute(out SyncMethodAttribute syncAttr))
                    continue;

                RegisterSyncMethod(method);
            }
        }

        public static Type[] TranslateArgTypes(Type[] argTypes)
        {
            return argTypes.Select(t =>
            {
                if (t.IsGenericType)
                {
                    // todo handle chains
                    if (t.GetGenericTypeDefinition() == typeof(Expose<>))
                        return t.GetGenericArguments()[0];
                    else if (t.GetGenericTypeDefinition() == typeof(CheckFaction<>))
                        return t.GetGenericArguments()[0];
                }

                return t;
            }).ToArray();
        }

        private static SyncMethod RegisterSyncMethod(MethodInfo method, params Type[] argTypes)
        {
            HarmonyMethod transpiler = new HarmonyMethod(typeof(Sync), nameof(Sync.SyncMethodTranspiler));
            transpiler.prioritiy = Priority.First;

            Multiplayer.harmony.Patch(method, null, null, transpiler);
            SyncMethod handler = new SyncMethod(handlers.Count, (method.IsStatic ? null : method.DeclaringType), method, argTypes);
            syncMethods[method] = handler;
            handlers.Add(handler);

            return handler;
        }

        private static void DoSyncMethod(RuntimeMethodHandle original, object instance, object[] args)
        {
            syncMethods[MethodBase.GetMethodFromHandle(original)].DoSync(instance, args);
        }

        // Cancels execution and sends the method with its arguments over the network if Multiplayer.ShouldSync
        private static IEnumerable<CodeInstruction> SyncMethodTranspiler(MethodBase original, ILGenerator gen, IEnumerable<CodeInstruction> insts)
        {
            Label jump = gen.DefineLabel();

            LocalBuilder retLocal = null;
            Type retType = (original as MethodInfo)?.ReturnType;
            if (retType != null && retType != typeof(void))
                retLocal = gen.DeclareLocal(retType);

            yield return new CodeInstruction(OpCodes.Call, AccessTools.Property(typeof(Multiplayer), nameof(Multiplayer.ShouldSync)).GetGetMethod());
            yield return new CodeInstruction(OpCodes.Brfalse, jump);
            yield return new CodeInstruction(OpCodes.Ldtoken, original);

            if (!original.IsStatic)
                yield return new CodeInstruction(OpCodes.Ldarg_0);
            else
                yield return new CodeInstruction(OpCodes.Ldnull);

            int len = original.GetParameters().Length;
            yield return new CodeInstruction(OpCodes.Ldc_I4, len);
            yield return new CodeInstruction(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < len; i++)
            {
                Type paramType = original.GetParameters()[i].ParameterType;

                yield return new CodeInstruction(OpCodes.Dup);
                yield return new CodeInstruction(OpCodes.Ldc_I4, i);

                if (paramType.IsByRef)
                {
                    // todo value types?
                    yield return new CodeInstruction(OpCodes.Ldnull);
                }
                else
                {
                    yield return new CodeInstruction(OpCodes.Ldarg, (original.IsStatic ? i : i + 1));
                    if (paramType.IsValueType)
                        yield return new CodeInstruction(OpCodes.Box, paramType);
                }

                yield return new CodeInstruction(OpCodes.Stelem, typeof(object));
            }

            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Sync), nameof(Sync.DoSyncMethod)));

            if (retLocal != null)
            {
                yield return new CodeInstruction(OpCodes.Ldloca, retLocal.LocalIndex);
                yield return new CodeInstruction(OpCodes.Initobj, retType);
                yield return new CodeInstruction(OpCodes.Ldloc, retLocal.LocalIndex);
            }

            yield return new CodeInstruction(OpCodes.Ret);

            insts.First().labels.Add(jump);

            foreach (CodeInstruction inst in insts)
                yield return inst;
        }

        public static void RegisterFieldPatches(Type type)
        {
            HarmonyMethod prefix = new HarmonyMethod(AccessTools.Method(typeof(Sync), nameof(Sync.FieldWatchPrefix)));
            prefix.prioritiy = Priority.First;
            HarmonyMethod postfix = new HarmonyMethod(AccessTools.Method(typeof(Sync), nameof(Sync.FieldWatchPostfix)));

            foreach (MethodBase patched in Multiplayer.harmony.DoMpPatches(type))
                Multiplayer.harmony.Patch(patched, prefix, postfix);
        }

        public static void Watch(this SyncField field, object target = null, object index = null)
        {
            if (!(field.inGameLoop || Multiplayer.ShouldSync))
                return;

            object value;

            if (field.bufferChanges && bufferedChanges[field].TryGetValue(new Pair<object, object>(target, index), out BufferData cached))
            {
                value = cached.toSend;
                target.SetPropertyOrField(field.memberPath, value, index);
            }
            else
            {
                value = target.GetPropertyOrField(field.memberPath, index);
            }

            watchedStack.Push(new FieldData(field, target, value, index));
        }

        public static void HandleCmd(ByteReader data)
        {
            int syncId = data.ReadInt32();
            SyncHandler handler = handlers[syncId];

            if (handler.HasContext)
            {
                IntVec3 mouseCell = ReadSync<IntVec3>(data);
                MouseCellPatch.result = mouseCell;

                Thing selThing = ReadSync<Thing>(data);
                ITabSelThingPatch.result = selThing;

                bool shouldQueue = data.ReadBool();
                KeyIsDownPatch.result = shouldQueue;
                KeyIsDownPatch.forKey = KeyBindingDefOf.QueueOrder;
            }

            try
            {
                handler.Handle(data);
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

        public static void WriteContext(SyncHandler handler, ByteWriter data)
        {
            if (!handler.HasContext) return;

            bool viewingMap = Find.CurrentMap != null && !WorldRendererUtility.WorldRenderedNow;
            WriteSync(data, viewingMap ? UI.MouseCell() : IntVec3.Invalid);
            WriteSync(data, selThingContext);
            data.WriteBool(KeyBindingDefOf.QueueOrder.IsDownEvent);
        }

        static ReaderDictionary readers = new ReaderDictionary
        {
            { data => data.ReadInt32() },
            { data => data.ReadBool() },
            { data => data.ReadString() },
            { data => data.ReadLong() },
            { data => data.ReadFloat() },
            { data => data.ReadDouble() },
            { data => data.ReadUInt16() },
            { data => data.ReadByte() },
            { data => ReadSync<Pawn>(data).mindState.priorityWork },
            { data => ReadSync<Pawn>(data).playerSettings },
            { data => ReadSync<Pawn>(data).timetable },
            { data => ReadSync<Pawn>(data).workSettings },
            { data => ReadSync<Pawn>(data).drafter },
            { data => ReadSync<Pawn>(data).jobs },
            { data => ReadSync<Pawn>(data).outfits },
            { data => ReadSync<Pawn>(data).drugs },
            { data => ReadSync<Pawn>(data).training },
            { data => new FloatRange(data.ReadFloat(), data.ReadFloat()) },
            { data => new IntRange(data.ReadInt32(), data.ReadInt32()) },
            { data => new QualityRange(ReadSync<QualityCategory>(data), ReadSync<QualityCategory>(data)) },
            { data => new IntVec3(data.ReadInt32(), data.ReadInt32(), data.ReadInt32()) },
            { data => new NameSingle(data.ReadString(), data.ReadBool()) },
            { data => new NameTriple(data.ReadString(), data.ReadString(), data.ReadString()) },
            { data => new Rot4(data.ReadByte()) },
            { data => new ITab_Bills() },
            { data => new ITab_Pawn_Gear() },
            { data => Current.Game.outfitDatabase },
            { data => Current.Game.drugPolicyDatabase },
            { data => (data.MpContext().map).areaManager },
            {
                data =>
                {
                    int id = data.ReadInt32();
                    return Multiplayer.WorldComp.trading.FirstOrDefault(s => s.sessionId == id);
                }
            },
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
            { (ByteWriter data, ushort u) => data.WriteUInt16(u) },
            { (ByteWriter data, byte b) => data.WriteByte(b) },
            { (ByteWriter data, PriorityWork work) => WriteSync(data, work.pawn) },
            { (ByteWriter data, Pawn_PlayerSettings comp) => WriteSync(data, comp.pawn) },
            { (ByteWriter data, Pawn_TimetableTracker comp) => WriteSync(data, comp.pawn) },
            { (ByteWriter data, Pawn_DraftController comp) => WriteSync(data, comp.pawn) },
            { (ByteWriter data, Pawn_WorkSettings comp) => WriteSync(data, comp.pawn) },
            { (ByteWriter data, Pawn_JobTracker comp) => WriteSync(data, comp.pawn) },
            { (ByteWriter data, Pawn_OutfitTracker comp) => WriteSync(data, comp.pawn) },
            { (ByteWriter data, Pawn_DrugPolicyTracker comp) => WriteSync(data, comp.pawn) },
            { (ByteWriter data, Pawn_TrainingTracker comp) => WriteSync(data, comp.pawn) },
            { (ByteWriter data, FloatRange range) => { data.WriteFloat(range.min); data.WriteFloat(range.max); }},
            { (ByteWriter data, IntRange range) => { data.WriteInt32(range.min); data.WriteInt32(range.max); }},
            { (ByteWriter data, QualityRange range) => { WriteSync(data, range.min); WriteSync(data, range.max); }},
            { (ByteWriter data, IntVec3 vec) => { data.WriteInt32(vec.x); data.WriteInt32(vec.y); data.WriteInt32(vec.z); }},
            { (ByteWriter data, NameSingle name) => { data.WriteString(name.nameInt); data.WriteBool(name.numerical); } },
            { (ByteWriter data, NameTriple name) => { data.WriteString(name.firstInt); data.WriteString(name.nickInt); data.WriteString(name.lastInt); } },
            { (ByteWriter data, Rot4 rot) => data.WriteByte(rot.AsByte) },
            { (ByteWriter data, ITab_Bills tab) => {} },
            { (ByteWriter data, ITab_Pawn_Gear tab) => {} },
            { (ByteWriter data, OutfitDatabase db) => {} },
            { (ByteWriter data, DrugPolicyDatabase db) => {} },
            { (ByteWriter data, AreaManager areas) => data.MpContext().map = areas.map },
            { (ByteWriter data, MpTradeSession session) => data.WriteInt32(session.sessionId) },
            {
                (ByteWriter data, LocalTargetInfo info) =>
                {
                    WriteSync(data, info.Thing);
                    if (!info.HasThing)
                        WriteSync(data, info.Cell);
                }
            }
        };

        private static Type[] storageParents = new[]
        {
            typeof(Building_Grave),
            typeof(Building_Storage),
            typeof(CompChangeableProjectile),
            typeof(Zone_Stockpile)
        };

        private static Type[] plantToGrowSettables = new[]
        {
            typeof(Building_PlantGrower),
            typeof(Zone_Growing),
        };

        public static MultiTarget thingFilterTarget = new MultiTarget()
        {
            { typeof(IStoreSettingsParent), "GetStoreSettings/filter" },
            { typeof(Bill), "ingredientFilter" },
            { typeof(Outfit), "filter" }
        };

        private static List<Type> thingCompTypes = typeof(ThingComp).AllSubclassesNonAbstract().ToList();
        private static List<Type> designatorTypes = typeof(Designator).AllSubclassesNonAbstract().ToList();

        public static T ReadSync<T>(ByteReader data)
        {
            return (T)ReadSyncObject(data, typeof(T));
        }

        private static MethodInfo ReadExposable = AccessTools.Method(typeof(ScribeUtil), nameof(ScribeUtil.ReadExposable));

        enum ListType
        {
            Normal, MapAllThings, MapAllDesignations
        }

        private static MethodInfo GetDefByIdMethod = AccessTools.Method(typeof(Sync), nameof(Sync.GetDefById));

        private static T GetDefById<T>(ushort id) where T : Def, new() => DefDatabase<T>.GetByShortHash(id);

        public static object ReadSyncObject(ByteReader data, Type type)
        {
            MpContext context = data.MpContext();
            Map map = context.map;

            if (type.IsByRef)
            {
                return null;
            }
            else if (readers.TryGetValue(type, out Func<ByteReader, object> reader))
            {
                return reader(data);
            }
            else if (type.IsEnum)
            {
                return Enum.ToObject(type, data.ReadInt32());
            }
            else if (type.IsArray && type.GetArrayRank() == 1)
            {
                Type elementType = type.GetElementType();
                int length = data.ReadInt32();
                Array arr = Array.CreateInstance(elementType, length);
                for (int i = 0; i < length; i++)
                    arr.SetValue(ReadSyncObject(data, elementType), i);
                return arr;
            }
            else if (type.IsGenericType)
            {
                if (type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    ListType specialList = ReadSync<ListType>(data);
                    if (specialList == ListType.MapAllThings)
                        return map.listerThings.AllThings;
                    else if (specialList == ListType.MapAllDesignations)
                        return map.designationManager.allDesignations;

                    Type listType = type.GetGenericArguments()[0];
                    int size = data.ReadInt32();
                    IList list = Activator.CreateInstance(type, size) as IList;
                    for (int j = 0; j < size; j++)
                        list.Add(ReadSyncObject(data, listType));
                    return list;
                }
                else if (type.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    bool isNull = data.ReadBool();
                    if (isNull) return null;

                    bool hasValue = data.ReadBool();
                    if (!hasValue) return Activator.CreateInstance(type);

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
                return null;
            }
            else if (typeof(Area).IsAssignableFrom(type))
            {
                int areaId = data.ReadInt32();
                if (areaId == -1)
                    return null;

                return map.areaManager.AllAreas.Find(a => a.ID == areaId);
            }
            else if (typeof(Zone).IsAssignableFrom(type))
            {
                int zoneId = data.ReadInt32();
                if (zoneId == -1)
                    return null;

                return map.zoneManager.AllZones.Find(zone => zone.ID == zoneId);
            }
            else if (typeof(Def).IsAssignableFrom(type))
            {
                ushort shortHash = data.ReadUInt16();
                if (shortHash == 0)
                    return null;

                return GetDefByIdMethod.MakeGenericMethod(type).Invoke(null, new object[] { shortHash });
            }
            else if (typeof(PawnColumnWorker).IsAssignableFrom(type))
            {
                PawnColumnDef def = ReadSync<PawnColumnDef>(data);
                return def.Worker;
            }
            else if (typeof(Command_SetPlantToGrow) == type)
            {
                IPlantToGrowSettable settable = ReadSync<IPlantToGrowSettable>(data);
                List<IPlantToGrowSettable> settables = ReadSync<List<IPlantToGrowSettable>>(data);

                Command_SetPlantToGrow command = (Command_SetPlantToGrow)FormatterServices.GetUninitializedObject(typeof(Command_SetPlantToGrow));
                command.settable = settable;
                command.settables = settables;

                return command;
            }
            else if (typeof(Designator).IsAssignableFrom(type))
            {
                int desId = data.ReadInt32();
                Type desType = designatorTypes[desId];

                Designator des;
                if (desType == typeof(Designator_Build))
                {
                    BuildableDef def = ReadSync<BuildableDef>(data);
                    des = new Designator_Build(def);
                }
                else
                {
                    des = (Designator)Activator.CreateInstance(desType);
                }

                return des;
            }
            else if (typeof(Thing).IsAssignableFrom(type))
            {
                int thingId = data.ReadInt32();
                if (thingId == -1)
                    return null;

                ThingDef def = ReadSync<ThingDef>(data);

                if (!context.readingThingParent)
                {
                    context.readingThingParent = true;
                    Thing parent = ReadSync<Thing>(data);
                    context.readingThingParent = false;

                    if (parent is IThingHolder holder)
                        return ThingOwnerUtility.GetAllThingsRecursively(holder).Find(t => t.thingIDNumber == thingId);
                    else if (parent != null)
                        return null;
                }

                return map.listerThings.ThingsOfDef(def).Find(t => t.thingIDNumber == thingId);
            }
            else if (typeof(CompChangeableProjectile) == type) // special case of ThingComp
            {
                Building_TurretGun parent = ReadSync<Thing>(data) as Building_TurretGun;
                if (parent == null)
                    return null;

                return (parent.gun as ThingWithComps).TryGetComp<CompChangeableProjectile>();
            }
            else if (typeof(ThingComp).IsAssignableFrom(type))
            {
                int compTypeId = data.ReadInt32();
                if (compTypeId == -1)
                    return null;

                ThingWithComps parent = ReadSync<Thing>(data) as ThingWithComps;
                if (parent == null)
                    return null;

                Type compType = thingCompTypes[compTypeId];
                return parent.AllComps.Find(comp => comp.props.compClass == compType);
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
                return billStack.Bills.Find(bill => bill.loadID == id);
            }
            else if (typeof(Outfit) == type)
            {
                int id = data.ReadInt32();
                return Current.Game.outfitDatabase.AllOutfits.Find(o => o.uniqueId == id);
            }
            else if (typeof(DrugPolicy) == type)
            {
                int id = data.ReadInt32();
                return Current.Game.drugPolicyDatabase.AllPolicies.Find(o => o.uniqueId == id);
            }
            else if (typeof(BodyPartRecord) == type)
            {
                int partIndex = data.ReadInt32();
                if (partIndex == -1) return null;

                BodyDef body = ReadSync<BodyDef>(data);
                return body.GetPartAtIndex(partIndex);
            }
            else if (typeof(MpTradeableReference) == type)
            {
                int sessionId = data.ReadInt32();
                MpTradeSession session = Multiplayer.WorldComp.trading.FirstOrDefault(s => s.sessionId == sessionId);
                if (session == null) return null;

                int thingId = data.ReadInt32();
                if (thingId == -1) return null;

                Tradeable tradeable = session.GetTradeableByThingId(thingId);
                if (tradeable == null) return null;

                return new MpTradeableReference(sessionId, tradeable);
            }
            else if (typeof(IStoreSettingsParent) == type)
            {
                return ReadWithImpl<IStoreSettingsParent>(data, storageParents);
            }
            else if (typeof(IPlantToGrowSettable) == type)
            {
                return ReadWithImpl<IPlantToGrowSettable>(data, plantToGrowSettables);
            }
            else if (typeof(StorageSettings) == type)
            {
                IStoreSettingsParent parent = ReadSync<IStoreSettingsParent>(data);
                if (parent == null) return null;
                return parent.GetStoreSettings();
            }

            throw new SerializationException("No reader for type " + type);
        }

        public static object[] ReadSyncObjects(ByteReader data, IEnumerable<Type> spec)
        {
            return spec.Select(type => ReadSyncObject(data, type)).ToArray();
        }

        public static void WriteSync<T>(ByteWriter data, T obj)
        {
            WriteSyncObject(data, obj, typeof(T));
        }

        public static void WriteSyncObject(ByteWriter data, object obj, Type type)
        {
            MpContext context = data.MpContext();

            LoggingByteWriter log = data as LoggingByteWriter;
            log?.LogEnter(type.FullName + ": " + (obj ?? "null"));

            try
            {
                if (type.IsByRef)
                {
                }
                else if (writers.TryGetValue(type, out Action<ByteWriter, object> writer))
                {
                    writer(data, obj);
                }
                else if (type.IsEnum)
                {
                    data.WriteInt32(Convert.ToInt32(obj));
                }
                else if (type.IsArray && type.GetArrayRank() == 1)
                {
                    Type elementType = type.GetElementType();
                    Array arr = obj as Array;
                    data.WriteInt32(arr.Length);
                    foreach (object e in arr)
                        WriteSyncObject(data, e, elementType);
                }
                else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    ListType specialList = ListType.Normal;
                    Type listType = type.GetGenericArguments()[0];

                    if (listType == typeof(Thing) && obj == Find.CurrentMap.listerThings.AllThings)
                    {
                        context.map = Find.CurrentMap;
                        specialList = ListType.MapAllThings;
                    }
                    else if (listType == typeof(Designation) && obj == Find.CurrentMap.designationManager.allDesignations)
                    {
                        context.map = Find.CurrentMap;
                        specialList = ListType.MapAllDesignations;
                    }

                    WriteSync(data, specialList);

                    if (specialList == ListType.Normal)
                    {
                        IList list = obj as IList;
                        data.WriteInt32(list.Count);
                        foreach (object e in list)
                            WriteSyncObject(data, e, listType);
                    }
                }
                else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    bool isNull = obj == null;
                    data.WriteBool(isNull);
                    if (isNull) return;

                    bool hasValue = (bool)obj.GetPropertyOrField("HasValue");
                    data.WriteBool(hasValue);

                    Type nullableType = type.GetGenericArguments()[0];
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
                    // todo implement?
                }
                else if (typeof(Area).IsAssignableFrom(type))
                {
                    if (obj is Area area)
                    {
                        context.map = area.Map;
                        data.WriteInt32(area.ID);
                    }
                    else
                    {
                        data.WriteInt32(-1);
                    }
                }
                else if (typeof(Zone).IsAssignableFrom(type))
                {
                    if (obj is Zone zone)
                    {
                        context.map = zone.Map;
                        data.WriteInt32(zone.ID);
                    }
                    else
                    {
                        data.WriteInt32(-1);
                    }
                }
                else if (typeof(Def).IsAssignableFrom(type))
                {
                    Def def = obj as Def;
                    data.WriteUInt16(def != null ? def.shortHash : (ushort)0);
                }
                else if (typeof(PawnColumnWorker).IsAssignableFrom(type))
                {
                    PawnColumnWorker worker = obj as PawnColumnWorker;
                    WriteSync(data, worker.def);
                }
                else if (typeof(Command_SetPlantToGrow) == type)
                {
                    Command_SetPlantToGrow command = obj as Command_SetPlantToGrow;
                    WriteSync(data, command.settable);
                    WriteSync(data, command.settables);
                }
                else if (typeof(Designator).IsAssignableFrom(type))
                {
                    Designator des = obj as Designator;
                    data.WriteInt32(designatorTypes.IndexOf(des.GetType()));

                    if (des is Designator_Build build)
                        WriteSync(data, build.PlacingDef);
                }
                else if (typeof(CompChangeableProjectile) == type) // special case of ThingComp
                {
                    CompChangeableProjectile comp = obj as CompChangeableProjectile;
                    if (comp == null)
                    {
                        WriteSync<Thing>(data, null);
                        return;
                    }

                    CompEquippable compEquippable = comp.parent.TryGetComp<CompEquippable>();

                    if (compEquippable.AllVerbs.Any())
                    {
                        Building_TurretGun turretGun = compEquippable.AllVerbs.Select(v => v.caster).OfType<Building_TurretGun>().FirstOrDefault();
                        if (turretGun != null)
                        {
                            WriteSync<Thing>(data, turretGun);
                            return;
                        }
                    }

                    throw new SerializationException("Couldn't save CompChangeableProjectile for thing " + comp.parent);
                }
                else if (typeof(ThingComp).IsAssignableFrom(type))
                {
                    if (obj is ThingComp comp)
                    {
                        data.WriteInt32(thingCompTypes.IndexOf(comp.GetType()));
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
                    WriteSync(data, workGiver.def);
                }
                else if (typeof(Thing).IsAssignableFrom(type))
                {
                    Thing thing = obj as Thing;
                    if (thing == null)
                    {
                        data.WriteInt32(-1);
                        return;
                    }

                    if (thing.Spawned)
                        context.map = thing.Map;

                    data.WriteInt32(thing.thingIDNumber);
                    WriteSync(data, thing.def);

                    Thing parent = ThingOwnerUtility.GetFirstSpawnedParentThing(thing);
                    if (!thing.Spawned && parent == null)
                        throw new SerializationException($"Thing {thing} is inaccessible");

                    WriteSync(data, thing.Spawned ? null : parent);
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
                    data.WriteInt32(bill.loadID);
                }
                else if (typeof(Outfit) == type)
                {
                    Outfit outfit = obj as Outfit;
                    data.WriteInt32(outfit.uniqueId);
                }
                else if (typeof(DrugPolicy) == type)
                {
                    DrugPolicy outfit = obj as DrugPolicy;
                    data.WriteInt32(outfit.uniqueId);
                }
                else if (typeof(BodyPartRecord) == type)
                {
                    if (obj == null)
                    {
                        data.WriteInt32(-1);
                        return;
                    }

                    BodyPartRecord part = obj as BodyPartRecord;
                    BodyDef body = part.body;

                    data.WriteInt32(body.GetIndexOfPart(part));
                    WriteSync(data, body);
                }
                else if (typeof(MpTradeableReference) == type)
                {
                    MpTradeableReference tr = (MpTradeableReference)obj;
                    data.WriteInt32(tr.sessionId);

                    Thing thing = tr.tradeable.FirstThingTrader ?? tr.tradeable.FirstThingColony;
                    data.WriteInt32(thing?.thingIDNumber ?? -1);
                }
                else if (typeof(IStoreSettingsParent) == type)
                {
                    WriteWithImpl<IStoreSettingsParent>(data, obj, storageParents);
                }
                else if (typeof(IPlantToGrowSettable) == type)
                {
                    WriteWithImpl<IPlantToGrowSettable>(data, obj, plantToGrowSettables);
                }
                else if (typeof(StorageSettings) == type)
                {
                    StorageSettings storage = obj as StorageSettings;
                    WriteSync(data, storage.owner);
                }
                else
                {
                    log?.LogNode("No writer for " + type);
                    throw new SerializationException("No writer for type " + type);
                }
            }
            catch
            {
                MpLog.Error($"Error writing type: {type}, obj: {obj}");
                throw;
            }
            finally
            {
                log?.LogExit();
            }
        }

        private static T ReadWithImpl<T>(ByteReader data, IList<Type> impls) where T : class
        {
            int impl = data.ReadInt32();
            if (impl == -1) return null;
            return (T)ReadSyncObject(data, impls[impl]);
        }

        private static void WriteWithImpl<T>(ByteWriter data, object obj, IList<Type> impls) where T : class
        {
            if (obj == null)
            {
                data.WriteInt32(-1);
                return;
            }

            int impl = -1;
            Type implType = null;
            for (int i = 0; i < impls.Count; i++)
            {
                if (impls[i].IsAssignableFrom(obj.GetType()))
                {
                    implType = impls[i];
                    impl = i;
                }
            }

            if (implType == null)
                throw new SerializationException($"Unknown {typeof(T)} implementation type {obj.GetType()}");

            data.WriteInt32(impl);
            WriteSyncObject(data, obj, implType);
        }
    }

    public static class GroupExtensions
    {
        public static void Watch(this SyncField[] group, object target = null, int index = -1)
        {
            foreach (SyncField field in group)
                if (field.targetType == null || field.targetType.IsAssignableFrom(target.GetType()))
                    field.Watch(target, index);
        }

        public static bool DoSync(this SyncMethod[] group, object target, params object[] args)
        {
            foreach (SyncMethod method in group)
                if (method.targetType == null || (target != null && method.targetType.IsAssignableFrom(target.GetType())))
                    return method.DoSync(target, args);

            return false;
        }

        public static SyncField[] SetBufferChanges(this SyncField[] group)
        {
            foreach (SyncField field in group)
                field.SetBufferChanges();
            return group;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
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

    [AttributeUsage(AttributeTargets.Method)]
    public class SyncMethodAttribute : Attribute
    {
    }

    public class Expose<T> { }

    public class CheckFaction<T> { }

    public class MultiTarget : IEnumerable<Pair<Type, string>>
    {
        private List<Pair<Type, string>> types = new List<Pair<Type, string>>();

        public void Add(Type type, string path)
        {
            types.Add(new Pair<Type, string>(type, path));
        }

        public void Add(MultiTarget type, string path)
        {
            foreach (var multiType in type)
                Add(multiType.First, multiType.Second + "/" + path);
        }

        public IEnumerator<Pair<Type, string>> GetEnumerator()
        {
            return types.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return types.GetEnumerator();
        }
    }

    class ReaderDictionary : OrderedDict<Type, Func<ByteReader, object>>
    {
        public void Add<T>(Func<ByteReader, T> writer)
        {
            Add(typeof(T), data => writer(data));
        }
    }

    class WriterDictionary : OrderedDict<Type, Action<ByteWriter, object>>
    {
        public void Add<T>(Action<ByteWriter, T> writer)
        {
            Add(typeof(T), (data, o) => writer(data, (T)o));
        }
    }

    public class OrderedDict<K, V> : IEnumerable
    {
        private List<K> list = new List<K>();
        private Dictionary<K, V> dict = new Dictionary<K, V>();

        public K this[int index]
        {
            get => list[index];
        }

        public V this[K key]
        {
            get => dict[key];
        }

        public void Add(K key, V value)
        {
            dict.Add(key, value);
            list.Add(key);
        }

        public void Insert(int index, K key, V value)
        {
            dict.Add(key, value);
            list.Insert(index, key);
        }

        public bool TryGetValue(K key, out V value)
        {
            value = default(V);
            return dict.TryGetValue(key, out value);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return dict.GetEnumerator();
        }
    }

    class UniqueList<T>
    {
        private List<T> list = new List<T>();
        private HashSet<T> set = new HashSet<T>();

        public void Add(T t)
        {
            if (set.Add(t))
                list.Add(t);
        }

        public T[] ToArray()
        {
            return list.ToArray();
        }
    }

    public class SerializationException : Exception
    {
        public SerializationException(string msg) : base(msg)
        {
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

        public override ByteWriter WriteString(string s)
        {
            LogEnter("string: " + s);
            base.WriteString(s);
            LogExit();
            return this;
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

    public class MethodGroup : IEnumerable<SyncMethod>
    {
        private List<SyncMethod> methods = new List<SyncMethod>();

        public void Add(string methodName, params Type[] argTypes)
        {
            methods.Add(Sync.Method(null, methodName, argTypes));
        }

        public bool MatchSync(object target, params object[] args)
        {
            if (!Multiplayer.ShouldSync)
                return false;

            foreach (SyncMethod method in methods)
            {
                if (Enumerable.SequenceEqual(method.argTypes, args.Select(o => o.GetType()), TypeComparer.INSTANCE))
                {
                    method.DoSync(target, args);
                    return true;
                }
            }

            return false;
        }

        private class TypeComparer : IEqualityComparer<Type>
        {
            public static TypeComparer INSTANCE = new TypeComparer();

            public bool Equals(Type x, Type y)
            {
                return x.IsAssignableFrom(y);
            }

            public int GetHashCode(Type obj)
            {
                throw new NotImplementedException();
            }
        }

        public IEnumerator<SyncMethod> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }

    public class MpContext
    {
        public Map map;
        public bool readingThingParent;
    }

}
