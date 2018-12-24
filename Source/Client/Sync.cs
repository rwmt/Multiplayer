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
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    public abstract class SyncHandler
    {
        private readonly int syncId;

        public int SyncId => syncId;

        public SyncContext context;
        public bool debugOnly;

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

        private bool cancelIfValueNull;

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

            writer.WriteInt32(SyncId);

            int mapId = ScheduledCommand.Global;
            if (targetType != null)
            {
                Sync.WriteSyncObject(writer, target, targetType);
                if (context.map != null)
                    mapId = context.map.uniqueID;
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
            {
                target = Sync.ReadSyncObject(data, targetType);
                if (target == null)
                    return;
            }

            object value = Sync.ReadSyncObject(data, fieldType);
            if (cancelIfValueNull && value == null)
                return;

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

        public SyncField CancelIfValueNull()
        {
            cancelIfValueNull = true;
            return this;
        }

        public SyncField SetDebugOnly()
        {
            debugOnly = true;
            return this;
        }

        public override string ToString()
        {
            return $"SyncField {memberPath}";
        }
    }

    public interface ISyncMethod
    {
        int SyncId { get; }

        bool DoSync(object target, object[] args);
    }

    public class SyncMethod : SyncHandler, ISyncMethod
    {
        public readonly Type targetType;
        public readonly string instancePath;

        public readonly MethodInfo method;
        public SyncType[] argTypes;

        private int minTime = 100; // Milliseconds between resends
        private long lastSendTime;

        private bool cancelIfAnyArgNull;
        private bool cancelIfNoSelectedMapObjects;
        private bool cancelIfNoSelectedWorldObjects;

        private Action<object, object[]> beforeCall;
        private Action<object, object[]> afterCall;

        public SyncMethod(int syncId, Type targetType, string instancePath, string methodName, SyncType[] argTypes) : base(syncId)
        {
            this.targetType = targetType;

            Type instanceType = targetType;
            if (!instancePath.NullOrEmpty())
            {
                this.instancePath = instanceType + "/" + instancePath;
                instanceType = MpReflection.PathType(this.instancePath);
            }

            method = AccessTools.Method(instanceType, methodName, argTypes != null ? argTypes.Select(t => t.type).ToArray() : null) ?? throw new Exception($"Couldn't find method {instanceType}::{methodName}");
            this.argTypes = CheckArgs(argTypes);
        }

        public SyncMethod(int syncId, Type targetType, MethodInfo method, SyncType[] argTypes) : base(syncId)
        {
            this.method = method;
            this.targetType = targetType;
            this.argTypes = CheckArgs(argTypes);
        }

        private SyncType[] CheckArgs(SyncType[] argTypes)
        {
            if (argTypes == null || argTypes.Length == 0)
            {
                return method.GetParameters().Select(p => (SyncType)p).ToArray();
            }
            else if (argTypes.Length != method.GetParameters().Length)
            {
                throw new Exception("Wrong parameter count for method " + method);
            }

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
            //if (Utils.MillisNow - lastSendTime < minTime)
            //    return true;

            LoggingByteWriter writer = new LoggingByteWriter();
            MpContext context = writer.MpContext();
            writer.LogNode("Sync method " + method.FullDescription());

            writer.WriteInt32(SyncId);

            Sync.WriteContext(this, writer);

            Map map = writer.MpContext().map;

            if (targetType != null)
            {
                Sync.WriteSyncObject(writer, target, targetType);
                if (context.map is Map newMap)
                    map = newMap;
            }

            for (int i = 0; i < argTypes.Length; i++)
            {
                var argType = argTypes[i];
                Sync.WriteSyncObject(writer, args[i], argType);

                if (argType.contextMap && args[i] is Map contextMap)
                    map = contextMap;

                if (context.map is Map newMap)
                {
                    if (map != null && map != newMap)
                        throw new Exception($"SyncMethod map mismatch ({map?.uniqueID} and {newMap?.uniqueID})");
                    map = newMap;
                }
            }

            int mapId = map?.uniqueID ?? ScheduledCommand.Global;
            writer.LogNode("Map id: " + mapId);
            Multiplayer.PacketLog.nodes.Add(writer.current);

            Multiplayer.Client.SendCommand(CommandType.Sync, mapId, writer.GetArray());

            lastSendTime = Utils.MillisNow;

            return true;
        }

        public override void Handle(ByteReader data)
        {
            object target = null;

            if (targetType != null)
            {
                target = Sync.ReadSyncObject(data, targetType);
                if (target == null)
                    return;
            }

            if (!instancePath.NullOrEmpty())
                target = target.GetPropertyOrField(instancePath);

            object[] args = null;
            if (argTypes != null)
            {
                args = Sync.ReadSyncObjects(data, argTypes);
                if (cancelIfAnyArgNull && args.Any(a => a == null))
                    return;
            }

            if (context.HasFlag(SyncContext.MapSelected) && cancelIfNoSelectedMapObjects && Find.Selector.selected.Count == 0)
                return;

            if (context.HasFlag(SyncContext.WorldSelected) && cancelIfNoSelectedWorldObjects && Find.WorldSelector.selected.Count == 0)
                return;

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

        public SyncMethod SetContext(SyncContext context)
        {
            this.context = context;
            return this;
        }

        public SyncMethod SetDebugOnly()
        {
            debugOnly = true;
            return this;
        }

        public SyncMethod SetPreInvoke(Action<object, object[]> action)
        {
            beforeCall = action;
            return this;
        }

        public SyncMethod CancelIfAnyArgNull()
        {
            cancelIfAnyArgNull = true;
            return this;
        }

        public SyncMethod CancelIfNoSelectedMapObjects()
        {
            cancelIfNoSelectedMapObjects = true;
            return this;
        }

        public SyncMethod CancelIfNoSelectedWorldObjects()
        {
            cancelIfNoSelectedWorldObjects = true;
            return this;
        }

        public SyncMethod ExposeParameter(int index)
        {
            argTypes[index].expose = true;
            return this;
        }

        public static SyncMethod Register(Type type, string methodOrPropertyName, SyncType[] argTypes = null)
        {
            return Sync.RegisterSyncMethod(type, methodOrPropertyName, argTypes);
        }

        public override string ToString()
        {
            return $"SyncMethod {method.FullDescription()}";
        }
    }

    public class SyncDelegate : SyncHandler, ISyncMethod
    {
        public readonly Type delegateType;
        public readonly MethodInfo method;

        private Type[] argTypes;
        public string[] fieldPaths;
        private Type[] fieldTypes;

        private string[] cancelIfAnyNullBlacklist;
        private string[] cancelIfNull;
        private bool cancelIfNoSelectedObjects;
        private string[] removeNullsFromLists;

        public MethodInfo patch;

        public SyncDelegate(int syncId, Type delegateType, MethodInfo method, string[] fieldPaths) : base(syncId)
        {
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
                var temp = new UniqueList<string>();
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

        public bool DoSync(object delegateInstance, object[] args)
        {
            if (!Multiplayer.ShouldSync)
                return false;

            LoggingByteWriter writer = new LoggingByteWriter();
            MpContext context = writer.MpContext();
            writer.LogNode($"Sync delegate: {delegateType} method: {method}");
            writer.LogNode("Patch: " + patch?.FullDescription());

            writer.WriteInt32(SyncId);

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
                string path = fieldPaths[i];
                string noTypePath = MpReflection.RemoveType(path);
                Type fieldType = fieldTypes[i];
                object value;

                if (fieldType.IsCompilerGenerated())
                    value = Activator.CreateInstance(fieldType);
                else
                    value = Sync.ReadSyncObject(data, fieldType);

                if (value == null)
                {
                    if (cancelIfAnyNullBlacklist != null && !cancelIfAnyNullBlacklist.Contains(noTypePath))
                        return;

                    if (path.EndsWith("$this"))
                        return;

                    if (cancelIfNull != null && cancelIfNull.Contains(noTypePath))
                        return;
                }

                if (removeNullsFromLists != null && removeNullsFromLists.Contains(noTypePath) && value is IList list)
                    list.RemoveNulls();

                MpReflection.SetValue(target, path, value);
            }

            if (context.HasFlag(SyncContext.MapSelected) && cancelIfNoSelectedObjects && Find.Selector.selected.Count == 0)
                return;

            object[] parameters = Sync.ReadSyncObjects(data, argTypes);

            MpLog.Log("Invoked delegate method " + method + " " + delegateType);
            method.Invoke(target, parameters);
        }

        public SyncDelegate SetContext(SyncContext context)
        {
            this.context = context;
            return this;
        }

        public SyncDelegate CancelIfAnyFieldNull(params string[] without)
        {
            cancelIfAnyNullBlacklist = without;
            return this;
        }

        public SyncDelegate CancelIfFieldsNull(params string[] whitelist)
        {
            cancelIfNull = whitelist;
            return this;
        }

        public SyncDelegate CancelIfNoSelectedObjects()
        {
            cancelIfNoSelectedObjects = true;
            return this;
        }

        public SyncDelegate RemoveNullsFromLists(params string[] listFields)
        {
            removeNullsFromLists = listFields;
            return this;
        }

        public static SyncDelegate Register(Type type, string nestedType, string method)
        {
            return Sync.RegisterSyncDelegate(type, nestedType, method);
        }

        public static SyncDelegate Register(Type inType, string nestedType, string methodName, string[] fields)
        {
            return Sync.RegisterSyncDelegate(inType, nestedType, methodName, fields);
        }

        public SyncDelegate SetDebugOnly()
        {
            debugOnly = true;
            return this;
        }

        public override string ToString()
        {
            return $"SyncDelegate {method.FullDescription()}";
        }
    }

    public delegate ref Action ActionGetter<T>(T t);

    public interface ISyncAction
    {
        IEnumerable DoSync(object target, object arg0, object arg1);
    }

    public class SyncAction<T, A, B, C> : SyncHandler, ISyncAction
    {
        private Func<A, B, C, IEnumerable<T>> func;
        private ActionGetter<T> actionGetter;

        public SyncAction(int syncId, Func<A, B, C, IEnumerable<T>> func, ActionGetter<T> actionGetter) : base(syncId)
        {
            this.func = func;
            this.actionGetter = actionGetter;
        }

        public IEnumerable<T> DoSync(A target, B arg0, C arg1)
        {
            SyncActions.wantOriginal = true;

            try
            {
                int i = 0;

                foreach (T t in func(target, arg0, arg1))
                {
                    int j = i;
                    i++;
                    var original = actionGetter(t);
                    actionGetter(t) = () => ActualSync(target, arg0, arg1, original);

                    yield return t;
                }
            }
            finally
            {
                SyncActions.wantOriginal = false;
            }
        }

        public IEnumerable DoSync(object target, object arg0, object arg1)
        {
            return DoSync((A)target, (B)arg0, (C)arg1);
        }

        private void ActualSync(A target, B arg0, C arg1, Action original)
        {
            LoggingByteWriter writer = new LoggingByteWriter();
            MpContext context = writer.MpContext();
            writer.LogNode("Sync action");

            writer.WriteInt32(SyncId);

            Sync.WriteSync(writer, target);
            Sync.WriteSync(writer, arg0);
            Sync.WriteSync(writer, arg1);

            writer.WriteInt32(GenText.StableStringHash(original.Method.MethodDesc()));
            Log.Message(original.Method.MethodDesc());

            int mapId = writer.MpContext().map?.uniqueID ?? -1;

            writer.LogNode("Map id: " + mapId);
            Multiplayer.PacketLog.nodes.Add(writer.current);

            Multiplayer.Client.SendCommand(CommandType.Sync, mapId, writer.GetArray());
        }

        public override void Handle(ByteReader data)
        {
            A target = Sync.ReadSync<A>(data);
            B arg0 = Sync.ReadSync<B>(data);
            C arg1 = Sync.ReadSync<C>(data);

            int descHash = data.ReadInt32();

            var action = func(target, arg0, arg1).Select(t => actionGetter(t)).FirstOrDefault(a => GenText.StableStringHash(a.Method.MethodDesc()) == descHash);
            action?.Invoke();
        }

        public void PatchAll(string methodName)
        {
            foreach (var type in typeof(A).AllSubtypesAndSelf())
            {
                if (type.IsAbstract) continue;

                foreach (var method in type.GetDeclaredMethods().Where(m => m.Name == methodName))
                {
                    HarmonyMethod prefix = new HarmonyMethod(typeof(SyncActions), nameof(SyncActions.SyncAction_Prefix));
                    prefix.priority = MpPriority.MpFirst;

                    HarmonyMethod postfix;

                    if (method.GetParameters().Length == 1)
                        postfix = new HarmonyMethod(typeof(SyncActions), nameof(SyncActions.SyncAction1_Postfix));
                    else if (method.GetParameters().Length == 2)
                        postfix = new HarmonyMethod(typeof(SyncActions), nameof(SyncActions.SyncAction2_Postfix));
                    else
                        throw new Exception($"Too many arguments to patch {method.FullDescription()}");

                    postfix.priority = MpPriority.MpLast;

                    Multiplayer.harmony.Patch(method, prefix, postfix);
                    SyncActions.syncActions[method] = this;
                }
            }
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
        public long timestamp;
        public bool sent;

        public BufferData(object currentValue, object toSend)
        {
            this.actualValue = currentValue;
            this.toSend = toSend;
        }
    }

    [Flags]
    public enum SyncContext
    {
        None = 0,
        MapMouseCell = 1,
        MapSelected = 2,
        WorldSelected = 4,
        QueueOrder_Down = 8,
        CurrentMap = 16,
    }

    public static partial class Sync
    {
        public static List<SyncHandler> handlers = new List<SyncHandler>();
        public static List<SyncField> bufferedFields = new List<SyncField>();

        public static Dictionary<MethodBase, ISyncMethod> syncMethods = new Dictionary<MethodBase, ISyncMethod>();

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
            if (Multiplayer.Client == null) return;

            while (watchedStack.Count > 0)
            {
                FieldData data = watchedStack.Pop();
                if (data == null)
                    break; // The marker

                SyncField handler = data.handler;
                object newValue = MpReflection.GetValue(data.target, handler.memberPath, data.index);
                bool changed = !Equals(newValue, data.oldValue);
                var cache = (handler.bufferChanges && !Multiplayer.IsReplay) ? bufferedChanges.GetValueSafe(handler) : null;

                if (cache != null && cache.TryGetValue(new Pair<object, object>(data.target, data.index), out BufferData cached))
                {
                    if (changed && cached.sent)
                        cached.sent = false;

                    cached.toSend = newValue;
                    MpReflection.SetValue(data.target, handler.memberPath, cached.actualValue, data.index);
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

                MpReflection.SetValue(data.target, handler.memberPath, data.oldValue, data.index);
            }
        }

        public static SyncMethod Method(Type targetType, string methodName, SyncType[] argTypes = null)
        {
            return Method(targetType, null, methodName, argTypes);
        }

        public static SyncMethod Method(Type targetType, string instancePath, string methodName, SyncType[] argTypes = null)
        {
            SyncMethod handler = new SyncMethod(handlers.Count, targetType, instancePath, methodName, argTypes);
            handlers.Add(handler);
            return handler;
        }

        public static SyncMethod[] MethodMultiTarget(MultiTarget targetType, string methodName, SyncType[] argTypes = null)
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

        public static bool AllDelegateFieldsRecursive(Type type, Func<string, bool> getter, string path = "")
        {
            if (path.NullOrEmpty())
                path = type.ToString();

            foreach (FieldInfo field in type.GetDeclaredInstanceFields())
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

        public static SyncDelegate RegisterSyncDelegate(Type type, string nestedType, string method)
        {
            return RegisterSyncDelegate(type, nestedType, method, null);
        }

        // todo support methods with arguments (currently there has been no need for it)
        public static SyncDelegate RegisterSyncDelegate(Type inType, string nestedType, string methodName, string[] fields, Type[] args = null)
        {
            string typeName = $"{inType}+{nestedType}";
            Type type = MpReflection.GetTypeByName(typeName);
            if (type == null)
                throw new Exception($"Couldn't find type {typeName}");

            MethodInfo method = AccessTools.Method(type, methodName, args);
            if (method == null)
                throw new Exception($"Couldn't find method {typeName}::{methodName}");

            MpUtil.MarkNoInlining(method);

            SyncDelegate handler = new SyncDelegate(handlers.Count, type, method, fields);
            syncMethods[handler.method] = handler;
            handlers.Add(handler);

            PatchMethodForSync(method);

            return handler;
        }

        public static SyncMethod RegisterSyncMethod(Type type, string methodOrPropertyName, SyncType[] argTypes = null)
        {
            MethodInfo method = AccessTools.Method(type, methodOrPropertyName, argTypes != null ? argTypes.Select(t => t.type).ToArray() : null);

            if (method == null)
            {
                PropertyInfo property = AccessTools.Property(type, methodOrPropertyName);
                method = property.GetSetMethod();
            }

            if (method == null)
                throw new Exception($"Couldn't find method or property {methodOrPropertyName} in type {type}");

            return RegisterSyncMethod(method, argTypes);
        }

        public static void RegisterAllSyncMethods()
        {
            foreach (Type type in MpUtil.AllModTypes())
            {
                foreach (MethodInfo method in type.GetDeclaredMethods())
                {
                    if (!method.TryGetAttribute(out SyncMethodAttribute syncAttr))
                        continue;

                    var syncMethod = RegisterSyncMethod(method, null);
                    syncMethod.context = syncAttr.context;
                    syncMethod.debugOnly = method.HasAttribute<SyncDebugOnlyAttribute>();
                }
            }
        }

        public static SyncMethod RegisterSyncMethod(MethodInfo method, SyncType[] argTypes)
        {
            MpUtil.MarkNoInlining(method);

            SyncMethod handler = new SyncMethod(handlers.Count, (method.IsStatic ? null : method.DeclaringType), method, argTypes);
            syncMethods[method] = handler;
            handlers.Add(handler);

            PatchMethodForSync(method);

            return handler;
        }

        private static void PatchMethodForSync(MethodBase method)
        {
            var prefixMethod = AccessTools.Method(typeof(SyncTemplates), $"Prefix_{method.GetParameters().Length}");
            if (prefixMethod == null)
                throw new Exception($"No prefix method for {method.GetParameters().Length} parameters.");

            HarmonyMethod prefix = new HarmonyMethod(prefixMethod);
            prefix.priority = Priority.First;
            Multiplayer.harmony.Patch(method, prefix);
        }

        public static void ApplyWatchFieldPatches(Type type)
        {
            HarmonyMethod prefix = new HarmonyMethod(AccessTools.Method(typeof(Sync), nameof(Sync.FieldWatchPrefix)));
            prefix.priority = Priority.First;
            HarmonyMethod postfix = new HarmonyMethod(AccessTools.Method(typeof(Sync), nameof(Sync.FieldWatchPostfix)));
            postfix.priority = Priority.Last;

            foreach (MethodBase toPatch in type.GetDeclaredMethods())
            {
                foreach (var attr in toPatch.AllAttributes<MpPrefix>())
                    Multiplayer.harmony.Patch(attr.Method, prefix, postfix);
            }
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

            List<object> prevSelected = Find.Selector.selected;
            List<WorldObject> prevWorldSelected = Find.WorldSelector.selected;

            if (handler.context != SyncContext.None)
            {
                if (handler.context.HasFlag(SyncContext.MapMouseCell))
                {
                    IntVec3 mouseCell = ReadSync<IntVec3>(data);
                    MouseCellPatch.result = mouseCell;
                }

                if (handler.context.HasFlag(SyncContext.MapSelected))
                {
                    List<ISelectable> selected = ReadSync<List<ISelectable>>(data);
                    Find.Selector.selected = selected.Cast<object>().NotNull().ToList();
                }

                if (handler.context.HasFlag(SyncContext.WorldSelected))
                {
                    List<ISelectable> selected = ReadSync<List<ISelectable>>(data);
                    Find.WorldSelector.selected = selected.Cast<WorldObject>().NotNull().ToList();
                }

                if (handler.context.HasFlag(SyncContext.QueueOrder_Down))
                {
                    bool shouldQueue = data.ReadBool();
                    KeyIsDownPatch.result = shouldQueue;
                    KeyIsDownPatch.forKey = KeyBindingDefOf.QueueOrder;
                }
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
                Find.Selector.selected = prevSelected;
                Find.WorldSelector.selected = prevWorldSelected;
            }
        }

        public static void WriteContext(SyncHandler handler, ByteWriter data)
        {
            if (handler.context == SyncContext.None) return;

            if (handler.context.HasFlag(SyncContext.CurrentMap))
                data.MpContext().map = Find.CurrentMap;

            if (handler.context.HasFlag(SyncContext.MapMouseCell))
            {
                data.MpContext().map = Find.CurrentMap;
                WriteSync(data, UI.MouseCell());
            }

            if (handler.context.HasFlag(SyncContext.MapSelected))
                WriteSync(data, Find.Selector.selected.Cast<ISelectable>().ToList());

            if (handler.context.HasFlag(SyncContext.WorldSelected))
                WriteSync(data, Find.WorldSelector.selected.Cast<ISelectable>().ToList());

            if (handler.context.HasFlag(SyncContext.QueueOrder_Down))
                data.WriteBool(KeyBindingDefOf.QueueOrder.IsDownEvent);
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
    public class SyncMethodAttribute : Attribute
    {
        public SyncContext context;

        public SyncMethodAttribute(SyncContext context = SyncContext.None)
        {
            this.context = context;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class SyncDebugOnlyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class SyncExpose : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class SyncContextMap : Attribute
    {
    }

    public struct SyncType
    {
        public readonly Type type;
        public bool expose;
        public bool contextMap;

        public SyncType(Type type)
        {
            this.type = type;
            this.expose = false;
            contextMap = false;
        }

        public static implicit operator SyncType(ParameterInfo param)
        {
            return new SyncType(param.ParameterType) { expose = param.HasAttribute<SyncExpose>(), contextMap = param.HasAttribute<SyncContextMap>() };
        }

        public static implicit operator SyncType(Type type)
        {
            return new SyncType(type);
        }
    }

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

        public override void WriteUShort(ushort val)
        {
            LogNode("ushort: " + val);
            base.WriteUShort(val);
        }

        public override void WriteShort(short val)
        {
            LogNode("short: " + val);
            base.WriteShort(val);
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

        public void Add(string methodName, params SyncType[] argTypes)
        {
            methods.Add(Sync.Method(null, methodName, argTypes));
        }

        public bool MatchSync(object target, params object[] args)
        {
            if (!Multiplayer.ShouldSync)
                return false;

            foreach (SyncMethod method in methods)
            {
                if (Enumerable.SequenceEqual(method.argTypes.Select(t => t.type), args.Select(o => o.GetType()), TypeComparer.INSTANCE))
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
        public bool syncingThingParent;
    }

}
