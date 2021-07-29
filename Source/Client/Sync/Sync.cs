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

    public static class Sync
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

            SyncWorkerEntry entry = SyncDictionary.syncWorkers.GetOrAddEntry(type, isImplicit: isImplicit, shouldConstruct: shouldConstruct);

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

            SyncWorkerEntry entry = SyncDictionary.syncWorkers.GetOrAddEntry(type, isImplicit: isImplicit, shouldConstruct: shouldConstruct);

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
                    IntVec3 mouseCell = SyncSerialization.ReadSync<IntVec3>(data);
                    MouseCellPatch.result = mouseCell;
                }

                if (handler.context.HasFlag(SyncContext.MapSelected)) {
                    List<ISelectable> selected = SyncSerialization.ReadSync<List<ISelectable>>(data);
                    Find.Selector.selected = selected.Cast<object>().AllNotNull().ToList();
                }

                if (handler.context.HasFlag(SyncContext.WorldSelected)) {
                    List<ISelectable> selected = SyncSerialization.ReadSync<List<ISelectable>>(data);
                    Find.WorldSelector.selected = selected.Cast<WorldObject>().AllNotNull().ToList();
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
                SyncSerialization.WriteSync(data, UI.MouseCell());
            }

            if (handler.context.HasFlag(SyncContext.MapSelected))
                SyncSerialization.WriteSync(data, Find.Selector.selected.Cast<ISelectable>().ToList());

            if (handler.context.HasFlag(SyncContext.WorldSelected))
                SyncSerialization.WriteSync(data, Find.WorldSelector.selected.Cast<ISelectable>().ToList());

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

        public static SyncField[] PostApply(this SyncField[] group, Action<object, object> func)
        {
            foreach (SyncField field in group)
                field.PostApply(func);
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

        public void Add(Type type)
        {
            types.Add(new Pair<Type, string>(type, null));
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
