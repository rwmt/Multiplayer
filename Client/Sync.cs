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
using System.Text;
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
    }

    public class SyncMethod : SyncHandler
    {
        public readonly Type targetType;
        public readonly string instancePath;

        public readonly MethodInfo method;
        public Type[] argTypes;

        private int minTime = 100; // Milliseconds between resends
        private long lastSendTime;

        private bool cancelIfAnyArgNull;
        private bool cancelIfNoSelectedObjects;

        private Action<object, object[]> beforeCall;
        private Action<object, object[]> afterCall;

        public SyncMethod(int syncId, Type targetType, string instancePath, string methodName, Type[] argTypes) : base(syncId)
        {
            this.targetType = targetType;

            Type instanceType = targetType;
            if (!instancePath.NullOrEmpty())
            {
                this.instancePath = instanceType + "/" + instancePath;
                instanceType = MpReflection.PathType(this.instancePath);
            }

            method = AccessTools.Method(instanceType, methodName, argTypes != null ? Sync.TranslateArgTypes(argTypes) : null) ?? throw new Exception($"Couldn't find method {instanceType}::{methodName}");
            this.argTypes = CheckArgs(argTypes);
        }

        public SyncMethod(int syncId, Type targetType, MethodInfo method, Type[] argTypes) : base(syncId)
        {
            this.method = method;
            this.targetType = targetType;
            this.argTypes = CheckArgs(argTypes);
        }

        private Type[] CheckArgs(Type[] argTypes)
        {
            if (argTypes == null || argTypes.Length == 0)
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
            //if (Utils.MillisNow - lastSendTime < minTime)
            //    return true;

            LoggingByteWriter writer = new LoggingByteWriter();
            MpContext context = writer.MpContext();
            writer.LogNode("Sync method " + method.FullDescription());

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

            if (hasContext && cancelIfNoSelectedObjects && Find.Selector.selected.Count == 0)
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

        public SyncMethod CancelIfAnyArgNull()
        {
            cancelIfAnyArgNull = true;
            return this;
        }

        public SyncMethod CancelIfNoSelectedObjects()
        {
            cancelIfNoSelectedObjects = true;
            return this;
        }

        public static SyncMethod Register(Type type, string methodOrPropertyName, Type[] argTypes = null)
        {
            return Sync.RegisterSyncMethod(type, methodOrPropertyName, argTypes);
        }
    }

    public class SyncDelegate : SyncHandler
    {
        public readonly Type delegateType;
        public readonly MethodInfo method;

        private Type[] argTypes;
        public string[] fieldPaths;
        private Type[] fieldTypes;

        private bool cancelIfAnyFieldNull;
        private bool cancelIfNoSelectedObjects;

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

        public bool DoSync(object delegateInstance, object[] args)
        {
            if (!Multiplayer.ShouldSync)
                return false;

            LoggingByteWriter writer = new LoggingByteWriter();
            MpContext context = writer.MpContext();
            writer.LogNode($"Sync delegate: {delegateType} method: {method}");
            writer.LogNode("Patch: " + patch?.FullDescription());

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

                if (cancelIfAnyFieldNull && value == null)
                    return;

                if (fieldPaths[i].EndsWith("$this") && value == null)
                    return;

                MpReflection.SetValue(target, fieldPaths[i], value);
            }

            if (hasContext && cancelIfNoSelectedObjects && Find.Selector.selected.Count == 0)
                return;

            object[] parameters = Sync.ReadSyncObjects(data, argTypes);

            MpLog.Log("Invoked delegate method " + method + " " + delegateType);
            method.Invoke(target, parameters);
        }

        public SyncDelegate CancelIfAnyFieldNull(params string[] without)
        {
            cancelIfAnyFieldNull = true;
            return this;
        }

        public SyncDelegate CancelIfFieldsNull(params string[] whitelist)
        {
            cancelIfAnyFieldNull = true;
            return this;
        }

        public SyncDelegate CancelIfNoSelectedObjects()
        {
            cancelIfNoSelectedObjects = true;
            return this;
        }

        public SyncDelegate RemoveNullsFromLists(params string[] listFields)
        {
            cancelIfNoSelectedObjects = true;
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
    }

    public class SyncAction : SyncHandler
    {
        private Type targetType;
        private MethodInfo method;
        private string targetPath;
        private Func<object, IEnumerable<Action>> actionSource;

        public SyncAction(int syncId, Type targetType, MethodInfo method, string targetPath, Func<object, IEnumerable<Action>> actionSource) : base(syncId)
        {
            this.targetType = targetType;
            this.method = method;
            this.targetPath = targetPath;
            this.actionSource = actionSource;
        }

        public void DoSync(object delegateInstance)
        {
            object target = delegateInstance.GetPropertyOrField(targetPath);

            LoggingByteWriter writer = new LoggingByteWriter();
            MpContext context = writer.MpContext();
            writer.LogNode($"Sync action: {method}");

            writer.WriteInt32(syncId);

            int mapId = ScheduledCommand.Global;
            Sync.WriteSyncObject(writer, target, targetType);

            if (context.map != null)
                mapId = context.map.uniqueID;

            writer.LogNode("Map id: " + mapId);
            Multiplayer.PacketLog.nodes.Add(writer.current);

            Multiplayer.Client.SendCommand(CommandType.Sync, mapId, writer.GetArray());
        }

        public override void Handle(ByteReader data)
        {
            object target = Sync.ReadSyncObject(data, targetType);
            if (target == null)
                return;

            actionSource(target).FirstOrDefault(a => a.Method == method)?.Invoke();
        }

        public static void Register<T>(string innerType, string methodName, string targetPath, Func<T, IEnumerable<Action>> actionSource) where T : class
        {
            string typeName = $"{typeof(T)}+{innerType}";
            Type type = MpReflection.GetTypeByName(typeName);
            if (type == null)
                throw new Exception($"Couldn't find type {typeName}");

            MethodInfo method = AccessTools.Method(type, methodName, new Type[0]);
            if (method == null)
                throw new Exception($"Couldn't find method {type}::{methodName}");

            SyncAction handler = new SyncAction(Sync.handlers.Count, typeof(T), method, targetPath, obj => actionSource((T)obj));
            Sync.handlers.Add(handler);
            Sync.syncActions[method] = handler;

            var prefix = new HarmonyMethod(AccessTools.Method(typeof(SyncAction), nameof(Prefix)));
            prefix.prioritiy = Priority.First;

            Multiplayer.harmony.Patch(method, prefix, null);
        }

        static bool Prefix(object __instance, MethodBase __originalMethod)
        {
            if (Multiplayer.ShouldSync)
            {
                Sync.syncActions[__originalMethod].DoSync(__instance);
                return false;
            }

            return true;
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

        private static Dictionary<MethodBase, SyncDelegate> syncDelegates = new Dictionary<MethodBase, SyncDelegate>();
        private static Dictionary<MethodBase, SyncMethod> syncMethods = new Dictionary<MethodBase, SyncMethod>();
        public static Dictionary<MethodBase, SyncAction> syncActions = new Dictionary<MethodBase, SyncAction>();

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

        public static SyncMethod Method(Type targetType, string methodName, Type[] argTypes = null)
        {
            return Method(targetType, null, methodName, argTypes);
        }

        public static SyncMethod Method(Type targetType, string instancePath, string methodName, Type[] argTypes = null)
        {
            SyncMethod handler = new SyncMethod(handlers.Count, targetType, instancePath, methodName, argTypes);
            handlers.Add(handler);
            return handler;
        }

        public static SyncMethod[] MethodMultiTarget(MultiTarget targetType, string methodName, Type[] argTypes = null)
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

        public static SyncDelegate RegisterSyncDelegate(Type type, string nestedType, string method)
        {
            return RegisterSyncDelegate(type, nestedType, method, null);
        }

        // todo support methods with arguments (currently there has been no need for it)
        public static SyncDelegate RegisterSyncDelegate(Type inType, string nestedType, string methodName, string[] fields)
        {
            string typeName = $"{inType}+{nestedType}";
            Type type = MpReflection.GetTypeByName(typeName);
            if (type == null)
                throw new Exception($"Couldn't find type {typeName}");

            MethodInfo method = AccessTools.Method(type, methodName, new Type[0]);
            if (method == null)
                throw new Exception($"Couldn't find method {typeName}::{methodName}");

            SyncDelegate handler = new SyncDelegate(handlers.Count, type, method, fields);
            syncDelegates[handler.method] = handler;
            handlers.Add(handler);

            HarmonyMethod prefix = new HarmonyMethod(typeof(Sync), nameof(Sync.SyncDelegatePrefix));
            prefix.prioritiy = Priority.First;
            Multiplayer.harmony.Patch(method, prefix, null);

            return handler;
        }

        static bool SyncDelegatePrefix(object __instance, MethodBase __originalMethod)
        {
            return !Delegate(__instance, __originalMethod);
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

        public static SyncMethod RegisterSyncMethod(Type type, string methodOrPropertyName, Type[] argTypes = null)
        {
            MethodInfo method = AccessTools.Method(type, methodOrPropertyName, argTypes != null ? Sync.TranslateArgTypes(argTypes) : null);

            if (method == null)
            {
                PropertyInfo property = AccessTools.Property(type, methodOrPropertyName);
                method = property.GetSetMethod();
            }

            if (method == null)
                throw new Exception($"Couldn't find method or property {methodOrPropertyName} in type {type}");

            return RegisterSyncMethod(method, argTypes);
        }

        public static void RegisterSyncMethods(Type inType)
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(inType))
            {
                if (!method.TryGetAttribute(out SyncMethodAttribute syncAttr))
                    continue;

                RegisterSyncMethod(method, null);
            }
        }

        public static Type[] TranslateArgTypes(Type[] argTypes)
        {
            return argTypes.Select(t =>
            {
                if (t.IsGenericType)
                {
                    if (t.GetGenericTypeDefinition() == typeof(Expose<>))
                        return t.GetGenericArguments()[0];
                }

                return t;
            }).ToArray();
        }

        public static SyncMethod RegisterSyncMethod(MethodInfo method, Type[] argTypes)
        {
            HarmonyMethod transpiler = new HarmonyMethod(typeof(Sync), nameof(Sync.SyncMethodTranspiler));
            transpiler.prioritiy = Priority.First;

            SyncMethod handler = new SyncMethod(handlers.Count, (method.IsStatic ? null : method.DeclaringType), method, argTypes);
            syncMethods[method] = handler;
            handlers.Add(handler);

            Multiplayer.harmony.Patch(method, null, null, transpiler);

            return handler;
        }

        private static void DoSyncMethod(int index, object instance, object[] args)
        {
            ((SyncMethod)handlers[index]).DoSync(instance, args);
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

            yield return new CodeInstruction(OpCodes.Ldc_I4, syncMethods[original].syncId);

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

            List<object> prevSelected = Find.Selector.selected;

            if (handler.HasContext)
            {
                IntVec3 mouseCell = ReadSync<IntVec3>(data);
                MouseCellPatch.result = mouseCell;

                List<ISelectable> selected = ReadSync<List<ISelectable>>(data);
                Find.Selector.selected = selected.Cast<object>().Where(o => o != null).ToList();

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
                Find.Selector.selected = prevSelected;
            }
        }

        public static void WriteContext(SyncHandler handler, ByteWriter data)
        {
            if (!handler.HasContext) return;

            bool viewingMap = Find.CurrentMap != null && !WorldRendererUtility.WorldRenderedNow;
            WriteSync(data, viewingMap ? UI.MouseCell() : IntVec3.Invalid);
            WriteSync(data, Find.Selector.selected.Cast<ISelectable>().ToList());
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
        public bool syncingThingParent;
    }

}
