using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Multiplayer.Client
{
    public abstract class SyncHandler
    {
        public int syncId = -1;

        public SyncContext context;
        public bool debugOnly;
        public bool hostOnly;
        public int version;

        protected SyncHandler()
        {
        }

        public abstract void Handle(ByteReader data);
    }

    public class SyncField : SyncHandler, ISyncField
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

        public SyncField(Type targetType, string memberPath)
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
            writer.log.Node($"Sync field {memberPath}");

            writer.WriteInt32(syncId);

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

            writer.log.Node($"Map id: {mapId}");
            Multiplayer.WriterLog.nodes.Add(writer.log.current);

            Multiplayer.Client.SendCommand(CommandType.Sync, mapId, writer.ToArray());

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

        public void Watch(object target = null, object index = null)
        {
            if (!(inGameLoop || Multiplayer.ShouldSync))
                return;

            object value;

            if (bufferChanges && Sync.bufferedChanges[this].TryGetValue(new Pair<object, object>(target, index), out BufferData cached)) {
                value = cached.toSend;
                target.SetPropertyOrField(memberPath, value, index);
            } else {
                value = target.GetPropertyOrField(memberPath, index);
            }

            Sync.watchedStack.Push(new FieldData(this, target, value, index));
        }

        public ISyncField SetVersion(int version)
        {
            this.version = version;
            return this;
        }

        public ISyncField PreApply(Action<object, object> action)
        {
            preApply = action;
            return this;
        }

        public ISyncField PostApply(Action<object, object> action)
        {
            postApply = action;
            return this;
        }

        public ISyncField SetBufferChanges()
        {
            Sync.bufferedChanges[this] = new Dictionary<Pair<object, object>, BufferData>();
            Sync.bufferedFields.Add(this);
            bufferChanges = true;
            return this;
        }

        public ISyncField InGameLoop()
        {
            inGameLoop = true;
            return this;
        }

        public ISyncField CancelIfValueNull()
        {
            cancelIfValueNull = true;
            return this;
        }

        public ISyncField SetDebugOnly()
        {
            debugOnly = true;
            return this;
        }

        public ISyncField SetHostOnly()
        {
            hostOnly = true;
            return this;
        }

        public override string ToString()
        {
            return $"SyncField {memberPath}";
        }
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

        public SyncMethod(Type targetType, string instancePath, string methodName, SyncType[] argTypes)
        {
            this.targetType = targetType;

            Type instanceType = targetType;
            if (!instancePath.NullOrEmpty())
            {
                this.instancePath = instanceType + "/" + instancePath;
                instanceType = MpReflection.PathType(this.instancePath);
            }

            method = AccessTools.Method(instanceType, methodName, argTypes?.Select(t => t.type).ToArray()) ?? throw new Exception($"Couldn't find method {instanceType}::{methodName}");
            this.argTypes = CheckArgs(argTypes);
        }

        public SyncMethod(Type targetType, MethodInfo method, SyncType[] argTypes)
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
            writer.log.Node("Sync method " + method.FullDescription());

            writer.WriteInt32(syncId);

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
            writer.log.Node("Map id: " + mapId);
            Multiplayer.WriterLog.nodes.Add(writer.log.current);

            Multiplayer.Client.SendCommand(CommandType.Sync, mapId, writer.ToArray());

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

        public ISyncMethod MinTime(int time)
        {
            minTime = time;
            return this;
        }

        public ISyncMethod SetContext(SyncContext context)
        {
            this.context = context;
            return this;
        }

        public ISyncMethod SetVersion(int version)
        {
            this.version = version;
            return this;
        }

        public ISyncMethod SetDebugOnly()
        {
            debugOnly = true;
            return this;
        }

        public ISyncMethod SetPreInvoke(Action<object, object[]> action)
        {
            beforeCall = action;
            return this;
        }

        public ISyncMethod SetPostInvoke(Action<object, object[]> action)
        {
            afterCall = action;
            return this;
        }

        public ISyncMethod CancelIfAnyArgNull()
        {
            cancelIfAnyArgNull = true;
            return this;
        }

        public ISyncMethod CancelIfNoSelectedMapObjects()
        {
            cancelIfNoSelectedMapObjects = true;
            return this;
        }

        public ISyncMethod CancelIfNoSelectedWorldObjects()
        {
            cancelIfNoSelectedWorldObjects = true;
            return this;
        }

        public ISyncMethod ExposeParameter(int index)
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

    public class SyncDelegate : SyncHandler, ISyncDelegate
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

        public SyncDelegate(Type delegateType, MethodInfo method, string[] fieldPaths)
        {
            this.delegateType = delegateType;
            this.method = method;

            argTypes = method.GetParameters().Types();

            if (fieldPaths == null) {
                List<string> fieldList = new List<string>();
                Sync.AllDelegateFieldsRecursive(delegateType, path => { fieldList.Add(path); return false; });
                this.fieldPaths = fieldList.ToArray();
            } else {
                var temp = new UniqueList<string>();
                foreach (string path in fieldPaths.Select(p => MpReflection.AppendType(p, delegateType))) {
                    string[] parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    string increment = parts[0] + "/" + parts[1];
                    for (int i = 2; i < parts.Length; i++) {
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
            writer.log.Node($"Sync delegate: {delegateType} method: {method}");
            writer.log.Node("Patch: " + patch?.FullDescription());

            writer.WriteInt32(syncId);

            Sync.WriteContext(this, writer);

            int mapId = ScheduledCommand.Global;

            IEnumerable<object> fields = fieldPaths.Select(p => delegateInstance.GetPropertyOrField(p));

            EnumerableHelper.ProcessCombined(fields.Concat(args), fieldTypes.Concat(argTypes), (obj, type) => {
                if (type.IsCompilerGenerated())
                    return;

                Sync.WriteSyncObject(writer, obj, type);

                if (context.map is Map map) {
                    if (mapId != ScheduledCommand.Global && mapId != map.uniqueID)
                        throw new Exception("SyncDelegate map mismatch");
                    mapId = map.uniqueID;
                }
            });

            writer.log.Node("Map id: " + mapId);
            Multiplayer.WriterLog.nodes.Add(writer.log.current);

            Multiplayer.Client.SendCommand(CommandType.Sync, mapId, writer.ToArray());

            return true;
        }

        public override void Handle(ByteReader data)
        {
            object target = Activator.CreateInstance(delegateType);

            for (int i = 0; i < fieldPaths.Length; i++) {
                string path = fieldPaths[i];
                string noTypePath = MpReflection.RemoveType(path);
                Type fieldType = fieldTypes[i];
                object value;

                if (fieldType.IsCompilerGenerated())
                    value = Activator.CreateInstance(fieldType);
                else
                    value = Sync.ReadSyncObject(data, fieldType);

                if (value == null) {
                    if (cancelIfAnyNullBlacklist != null && !cancelIfAnyNullBlacklist.Contains(noTypePath))
                        return;

                    if (path.EndsWith("4__this"))
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

        public ISyncDelegate SetContext(SyncContext context)
        {
            this.context = context;
            return this;
        }

        public ISyncDelegate CancelIfAnyFieldNull(params string[] without)
        {
            cancelIfAnyNullBlacklist = without;
            return this;
        }

        public ISyncDelegate CancelIfFieldsNull(params string[] whitelist)
        {
            cancelIfNull = whitelist;
            return this;
        }

        public ISyncDelegate CancelIfNoSelectedObjects()
        {
            cancelIfNoSelectedObjects = true;
            return this;
        }

        public ISyncDelegate RemoveNullsFromLists(params string[] listFields)
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

        public ISyncDelegate SetDebugOnly()
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

        public SyncAction(Func<A, B, C, IEnumerable<T>> func, ActionGetter<T> actionGetter)
        {
            this.func = func;
            this.actionGetter = actionGetter;
        }

        public IEnumerable<T> DoSync(A target, B arg0, C arg1)
        {
            SyncActions.wantOriginal = true;

            try {
                int i = 0;

                foreach (T t in func(target, arg0, arg1)) {
                    int j = i;
                    i++;
                    var original = actionGetter(t);
                    actionGetter(t) = () => ActualSync(target, arg0, arg1, original);

                    yield return t;
                }
            } finally {
                SyncActions.wantOriginal = false;
            }
        }

        public IEnumerable DoSync(object target, object arg0, object arg1)
        {
            return DoSync((A) target, (B) arg0, (C) arg1);
        }

        private void ActualSync(A target, B arg0, C arg1, Action original)
        {
            LoggingByteWriter writer = new LoggingByteWriter();
            MpContext context = writer.MpContext();
            writer.log.Node("Sync action");

            writer.WriteInt32(syncId);

            Sync.WriteSync(writer, target);
            Sync.WriteSync(writer, arg0);
            Sync.WriteSync(writer, arg1);

            writer.WriteInt32(GenText.StableStringHash(original.Method.MethodDesc()));
            Log.Message(original.Method.MethodDesc());

            int mapId = writer.MpContext().map?.uniqueID ?? -1;

            writer.log.Node("Map id: " + mapId);
            Multiplayer.WriterLog.nodes.Add(writer.log.current);

            Multiplayer.Client.SendCommand(CommandType.Sync, mapId, writer.ToArray());
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
            foreach (var type in typeof(A).AllSubtypesAndSelf()) {
                if (type.IsAbstract) continue;

                foreach (var method in type.GetDeclaredMethods().Where(m => m.Name == methodName)) {
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

                    MultiplayerMod.harmony.Patch(method, prefix, postfix);
                    SyncActions.syncActions[method] = this;
                }
            }
        }

        public override string ToString()
        {
            return "SyncAction";
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

    public static partial class Sync
    {
        public static List<SyncHandler> handlers = new List<SyncHandler>();
        public static List<SyncField> bufferedFields = new List<SyncField>();

        // Internal maps for Harmony patches
        public static Dictionary<MethodBase, int> methodBaseToInternalId = new Dictionary<MethodBase, int>();
        public static List<ISyncCall> internalIdToSyncMethod = new List<ISyncCall>();

        static Dictionary<string, SyncField> registeredSyncFields = new Dictionary<string, SyncField>();

        public static Dictionary<SyncField, Dictionary<Pair<object, object>, BufferData>> bufferedChanges = new Dictionary<SyncField, Dictionary<Pair<object, object>, BufferData>>();
        public static Stack<FieldData> watchedStack = new Stack<FieldData>();

        public static bool isDialogNodeTreeOpen = false;

        public static void PostInitHandlers()
        {
            handlers.SortStable((a, b) => a.version.CompareTo(b.version));

            for (int i = 0; i < handlers.Count; i++)
                handlers[i].syncId = i;
        }

        public static void FieldWatchPrefix()
        {
            if (Multiplayer.Client == null) return;
            watchedStack.Push(null); // Marker
        }

        // todo what happens on exceptions?
        public static void FieldWatchPostfix()
        {
            if (Multiplayer.Client == null) return;

            while (watchedStack.Count > 0) {
                FieldData data = watchedStack.Pop();

                if (data == null)
                    break; // The marker

                SyncField handler = data.handler;

                object newValue = MpReflection.GetValue(data.target, handler.memberPath, data.index);
                bool changed = !Equals(newValue, data.oldValue);
                var cache = (handler.bufferChanges && !Multiplayer.IsReplay) ? bufferedChanges.GetValueSafe(handler) : null;

                if (cache != null && cache.TryGetValue(new Pair<object, object>(data.target, data.index), out BufferData cached)) {
                    if (changed && cached.sent)
                        cached.sent = false;

                    cached.toSend = newValue;
                    MpReflection.SetValue(data.target, handler.memberPath, cached.actualValue, data.index);
                    continue;
                }

                if (!changed) continue;

                if (cache != null) {
                    BufferData bufferData = new BufferData(data.oldValue, newValue);
                    cache[new Pair<object, object>(data.target, data.index)] = bufferData;
                } else {
                    handler.DoSync(data.target, newValue, data.index);
                }

                MpReflection.SetValue(data.target, handler.memberPath, data.oldValue, data.index);
            }
        }

        public static void DialogNodeTreePostfix()
        {
            if (Multiplayer.Client != null && Find.WindowStack?.WindowOfType<Dialog_NodeTree>() != null) isDialogNodeTreeOpen = true;
        }

        public static SyncMethod Method(Type targetType, string methodName, SyncType[] argTypes = null)
        {
            return Method(targetType, null, methodName, argTypes);
        }

        public static SyncMethod Method(Type targetType, string instancePath, string methodName, SyncType[] argTypes = null)
        {
            SyncMethod handler = new SyncMethod(targetType, instancePath, methodName, argTypes);
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
            SyncField handler = new SyncField(targetType, instancePath + "/" + fieldName);
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

            foreach (FieldInfo field in type.GetDeclaredInstanceFields()) {
                string curPath = path + "/" + field.Name;

                if (typeof(Delegate).IsAssignableFrom(field.FieldType))
                    continue;

                if (getter(curPath))
                    return true;

                if (!field.FieldType.IsCompilerGenerated())
                    continue;

                if (AllDelegateFieldsRecursive(field.FieldType, getter, curPath))
                    return true;
            }

            return false;
        }

        public static ISyncField RegisterSyncField(Type targetType, string fieldName)
        {
            SyncField sf = Field(targetType, null, fieldName);

            registeredSyncFields.Add(targetType + "/" + fieldName, sf);

            return sf;
        }

        public static ISyncField RegisterSyncField(FieldInfo field)
        {
            string memberPath = field.ReflectedType + "/" + field.Name;
            SyncField sf;
            if (field.IsStatic) {
                sf = Field(null, null, memberPath);
            } else {
                sf = Field(field.ReflectedType, null, field.Name);
            }

            registeredSyncFields.Add(memberPath, sf);

            return sf;
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

            SyncDelegate handler = new SyncDelegate(type, method, fields);
            methodBaseToInternalId[handler.method] = internalIdToSyncMethod.Count;
            internalIdToSyncMethod.Add(handler);
            handlers.Add(handler);

            PatchMethodForSync(method);

            return handler;
        }

        public static SyncMethod RegisterSyncMethod(Type type, string methodOrPropertyName, SyncType[] argTypes = null)
        {
            MethodInfo method = AccessTools.Method(type, methodOrPropertyName, argTypes != null ? argTypes.Select(t => t.type).ToArray() : null);

            if (method == null) {
                PropertyInfo property = AccessTools.Property(type, methodOrPropertyName);

                if (property != null) {
                    method = property.GetSetMethod();
                }
            }

            if (method == null)
                throw new Exception($"Couldn't find method or property {methodOrPropertyName} in type {type}");

            return RegisterSyncMethod(method, argTypes);
        }

        /// <summary>
        /// Registers all declared attributes SyncMethods and SyncFields in the assembly
        /// </summary>
        internal static void RegisterAllAttributes(Assembly asm)
        {
            foreach (Type type in asm.GetTypes()) {
                foreach (MethodInfo method in type.GetDeclaredMethods()) {
                    if (method.TryGetAttribute(out SyncMethodAttribute sma)) {
                        RegisterSyncMethod(method, sma);
                    } else if (method.TryGetAttribute(out SyncWorkerAttribute swa)) {
                        RegisterSyncWorker(method, isImplicit: swa.isImplicit, shouldConstruct: swa.shouldConstruct);
                    } else if (method.TryGetAttribute(out SyncDialogNodeTreeAttribute sdnta)) {
                        RegisterSyncDialogNodeTree(method);
                    }
                }
                foreach (FieldInfo field in AccessTools.GetDeclaredFields(type)) {
                    if (field.TryGetAttribute(out SyncFieldAttribute sfa)) {
                        RegisterSyncField(field, sfa);
                    }
                }
            }
        }

        static void RegisterSyncMethod(MethodInfo method, SyncMethodAttribute attribute)
        {
            int[] exposeParameters = attribute.exposeParameters;
            int paramNum = method.GetParameters().Length;

            if (exposeParameters != null) {
                if (exposeParameters.Length != paramNum) {
                    Log.Error($"Failed to register a method: Invalid number of parameters to expose in SyncMethod attribute applied to {method.DeclaringType.FullName}::{method}. Expected {paramNum}, got {exposeParameters.Length}");
                    return;
                } else if (exposeParameters.Any(p => p < 0 || p >= paramNum)) {
                    Log.Error($"Failed to register a method: One or more indexes of parameters to expose in SyncMethod attribute applied to {method.DeclaringType.FullName}::{method} is invalid.");
                    return;
                }
            }

            var sm = RegisterSyncMethod(method, (SyncType[]) null);
            sm.context = attribute.context;
            sm.debugOnly = attribute.debugOnly;

            sm.SetContext(attribute.context);

            if (attribute.cancelIfAnyArgNull)
                sm.CancelIfAnyArgNull();

            if (attribute.cancelIfNoSelectedMapObjects)
                sm.CancelIfNoSelectedMapObjects();

            if (attribute.cancelIfNoSelectedWorldObjects)
                sm.CancelIfNoSelectedWorldObjects();

            if (attribute.debugOnly)
                sm.SetDebugOnly();

            if (exposeParameters != null) {
                int i = 0;

                try {
                    for (; i < exposeParameters.Length; i++) {
                        Log.Message($"Exposing parameter {exposeParameters[i]}");
                        sm.ExposeParameter(exposeParameters[i]);
                    }
                } catch (Exception exc) {
                    Log.Error($"An exception occurred while exposing parameter {i} ({method.GetParameters()[i]}) for method {method.DeclaringType.FullName}::{method}: {exc}");
                }
            }
        }

        public static SyncField GetRegisteredSyncField(Type target, string name)
        {
            return GetRegisteredSyncField(target + "/" + name);
        }

        public static SyncField GetRegisteredSyncField(string memberPath)
        {
            if (registeredSyncFields.TryGetValue(memberPath, out SyncField cached))
                return cached;

            var syncField = handlers.OfType<SyncField>().FirstOrDefault(sf => sf.memberPath == memberPath);

            registeredSyncFields[memberPath] = syncField;
            return syncField;
        }

        static void RegisterSyncField(FieldInfo field, SyncFieldAttribute attribute)
        {
            SyncField sf = Field(field.ReflectedType, field.Name);

            registeredSyncFields.Add(field.ReflectedType + "/" + field.Name, sf);

            if (MpVersion.IsDebug) { 
                Log.Message($"Registered Field: {field.ReflectedType}/{field.Name}");
            }

            if (attribute.cancelIfValueNull)
                sf.CancelIfValueNull();

            if (attribute.inGameLoop)
                sf.InGameLoop();

            if (attribute.bufferChanges)
                sf.SetBufferChanges();

            if (attribute.debugOnly)
                sf.SetDebugOnly();

            if (attribute.hostOnly)
                sf.SetHostOnly();

            if (attribute.version > 0)
                sf.SetVersion(attribute.version);
        }

        public static SyncMethod RegisterSyncMethod(MethodInfo method, SyncType[] argTypes = null)
        {
            MpUtil.MarkNoInlining(method);

            SyncMethod handler = new SyncMethod((method.IsStatic ? null : method.DeclaringType), method, argTypes);
            methodBaseToInternalId[handler.method] = internalIdToSyncMethod.Count;
            internalIdToSyncMethod.Add(handler);
            handlers.Add(handler);

            PatchMethodForSync(method);

            return handler;
        }

        public static void RegisterSyncWorker(MethodInfo method, Type targetType = null, bool isImplicit = false, bool shouldConstruct = false)
        {
            Type[] parameters = method.GetParameters().Select(p => p.ParameterType).ToArray();

            if (!method.IsStatic) {
                Log.Error($"Error in {method.DeclaringType.FullName}::{method}: SyncWorker method has to be static.");
                return;
            }

            if (parameters.Length != 2) {
                Log.Error($"Error in {method.DeclaringType.FullName}::{method}: SyncWorker method has an invalid number of parameters.");
                return;
            }

            if (parameters[0] != typeof(SyncWorker)) {
                Log.Error($"Error in {method.DeclaringType.FullName}::{method}: SyncWorker method has an invalid first parameter (got {parameters[0]}, expected ISyncWorker).");
                return;
            }

            if (targetType != null && parameters[1].IsAssignableFrom(targetType)) {
                Log.Error($"Error in {method.DeclaringType.FullName}::{method}: SyncWorker method has an invalid second parameter (got {parameters[1]}, expected {targetType} or assignable).");
                return;
            }

            if (!parameters[1].IsByRef) {
                Log.Error($"Error in {method.DeclaringType.FullName}::{method}: SyncWorker method has an invalid second parameter, should be a ref.");
                return;
            }

            var type = targetType ?? parameters[1].GetElementType();

            if (isImplicit) {
                if (method.ReturnType != typeof(bool)) {
                    Log.Error($"Error in {method.DeclaringType.FullName}::{method}: SyncWorker set as implicit (or the argument type is an interface) requires bool type as a return value.");
                    return;
                }
            } else if (method.ReturnType != typeof(void)) {
                Log.Error($"Error in {method.DeclaringType.FullName}::{method}: SyncWorker set as explicit should have void as a return value.");
                return;
            }

            SyncWorkerEntry entry = syncWorkers.GetOrAddEntry(type, isImplicit: isImplicit, shouldConstruct: shouldConstruct);

            entry.Add(method);

            if (!(isImplicit || type.IsInterface) && entry.SyncWorkerCount > 1) {
                Log.Warning($"Warning in {method.DeclaringType.FullName}::{method}: type {type} has already registered an explicit SyncWorker, the code in this method may be not used.");
            }

            Log.Message($"Registered a SyncWorker {method.DeclaringType.FullName}::{method} for type {type} in assembly {method.DeclaringType.Assembly.GetName().Name}");
        }

        public static void RegisterSyncWorker<T>(SyncWorkerDelegate<T> syncWorkerDelegate, Type targetType = null, bool isImplicit = false, bool shouldConstruct = false)
        {
            MethodInfo method = syncWorkerDelegate.Method;

            Type[] parameters = method.GetParameters().Select(p => p.ParameterType).ToArray();

            if (targetType != null && parameters[1].IsAssignableFrom(targetType)) {
                Log.Error($"Error in {method.DeclaringType.FullName}::{method}: SyncWorker method has an invalid second parameter (got {parameters[1]}, expected {targetType} or assignable).");
                return;
            }

            var type = targetType ?? typeof(T);

            SyncWorkerEntry entry = syncWorkers.GetOrAddEntry(type, isImplicit: isImplicit, shouldConstruct: shouldConstruct);

            entry.Add(syncWorkerDelegate);

            if (!(isImplicit || type.IsInterface) && entry.SyncWorkerCount > 1) {
                Log.Warning($"Warning in {method.DeclaringType.FullName}::{method}: type {type} has already registered an explicit SyncWorker, the code in this method may be not used.");
            }
        }

        public static void RegisterSyncDialogNodeTree(Type type, string methodOrPropertyName, SyncType[] argTypes = null)
        {
            MethodInfo method = AccessTools.Method(type, methodOrPropertyName, argTypes != null ? argTypes.Select(t => t.type).ToArray() : null);

            if (method == null)
            {
                PropertyInfo property = AccessTools.Property(type, methodOrPropertyName);

                if (property != null)
                {
                    method = property.GetSetMethod();
                }
            }

            if (method == null)
                throw new Exception($"Couldn't find method or property {methodOrPropertyName} in type {type}");

            RegisterSyncDialogNodeTree(method);
        }

        public static void RegisterSyncDialogNodeTree(MethodInfo method)
        {
            PatchMethodForDialogNodeTreeSync(method);
        }

        private static void PatchMethodForSync(MethodBase method)
        {
            MultiplayerMod.harmony.Patch(method, transpiler: SyncTemplates.CreateTranspiler());
        }

        public static void ApplyWatchFieldPatches(Type type)
        {
            HarmonyMethod prefix = new HarmonyMethod(AccessTools.Method(typeof(Sync), nameof(Sync.FieldWatchPrefix)));
            prefix.priority = MpPriority.MpFirst;
            HarmonyMethod postfix = new HarmonyMethod(AccessTools.Method(typeof(Sync), nameof(Sync.FieldWatchPostfix)));
            postfix.priority = MpPriority.MpLast;

            foreach (MethodBase toPatch in type.GetDeclaredMethods()) {
                foreach (var attr in toPatch.AllAttributes<MpPrefix>()) {
                    MultiplayerMod.harmony.Patch(attr.Method, prefix, postfix);
                }
            }
        }

        public static void PatchMethodForDialogNodeTreeSync(MethodBase method)
        {
            MultiplayerMod.harmony.Patch(method, postfix: new HarmonyMethod(typeof(Sync), nameof(DialogNodeTreePostfix)));
        }

        public static void HandleCmd(ByteReader data)
        {
            int syncId = data.ReadInt32();
            SyncHandler handler;
            try {
                handler = handlers[syncId];
            }
            catch (ArgumentOutOfRangeException) {
                Log.Error($"Error: invalid syncId {syncId}/{handlers.Count}, this implies mismatched mods, ensure your versions match! Stacktrace follows.");
                throw;
            }

            List<object> prevSelected = Find.Selector.selected;
            List<WorldObject> prevWorldSelected = Find.WorldSelector.selected;

            bool shouldQueue = false;

            if (handler.context != SyncContext.None) {
                if (handler.context.HasFlag(SyncContext.MapMouseCell)) {
                    IntVec3 mouseCell = ReadSync<IntVec3>(data);
                    MouseCellPatch.result = mouseCell;
                }

                if (handler.context.HasFlag(SyncContext.MapSelected)) {
                    List<ISelectable> selected = ReadSync<List<ISelectable>>(data);
                    Find.Selector.selected = selected.Cast<object>().NotNull().ToList();
                }

                if (handler.context.HasFlag(SyncContext.WorldSelected)) {
                    List<ISelectable> selected = ReadSync<List<ISelectable>>(data);
                    Find.WorldSelector.selected = selected.Cast<WorldObject>().NotNull().ToList();
                }

                if (handler.context.HasFlag(SyncContext.QueueOrder_Down))
                    shouldQueue = data.ReadBool();
            }

            KeyIsDownPatch.shouldQueue = shouldQueue;

            try {
                handler.Handle(data);
            } finally {
                MouseCellPatch.result = null;
                KeyIsDownPatch.shouldQueue = null;
                Find.Selector.selected = prevSelected;
                Find.WorldSelector.selected = prevWorldSelected;
            }
        }

        public static void WriteContext(SyncHandler handler, ByteWriter data)
        {
            if (handler.context == SyncContext.None) return;

            if (handler.context.HasFlag(SyncContext.CurrentMap))
                data.MpContext().map = Find.CurrentMap;

            if (handler.context.HasFlag(SyncContext.MapMouseCell)) {
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
                if (field.targetType == null || field.targetType.IsInstanceOfType(target))
                    field.Watch(target, index);
        }

        public static bool DoSync(this SyncMethod[] group, object target, params object[] args)
        {
            foreach (SyncMethod method in group)
                if (method.targetType == null || method.targetType.IsInstanceOfType(target))
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

            foreach (SyncMethod method in methods) {
                if (Enumerable.SequenceEqual(method.argTypes.Select(t => t.type), args.Select(o => o.GetType()), TypeComparer.INSTANCE)) {
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
