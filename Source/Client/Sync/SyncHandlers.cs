using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
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

        public abstract void Validate();

        protected void ValidateType(string desc, SyncType type)
        {
            if (type.type != null && !SyncSerialization.CanHandle(type))
                throw new Exception($"Sync handler uses a non-serializable type: {type.type}. Details: {desc}");
        }
    }

    public class SyncField : SyncHandler, ISyncField
    {
        public readonly Type targetType;
        public readonly string memberPath;
        public SyncType fieldType;
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
        /// Returns whether the original should be cancelled
        /// </summary>
        public bool DoSync(object target, object value, object index = null)
        {
            if (!(inGameLoop || Multiplayer.ShouldSync))
                return false;

            LoggingByteWriter writer = new LoggingByteWriter();
            MpContext context = writer.MpContext();
            writer.Log.Node(ToString());

            writer.WriteInt32(syncId);

            int mapId = ScheduledCommand.Global;
            if (targetType != null)
            {
                SyncSerialization.WriteSyncObject(writer, target, targetType);
                if (context.map != null)
                    mapId = context.map.uniqueID;
            }

            SyncSerialization.WriteSyncObject(writer, value, fieldType);
            if (indexType != null)
                SyncSerialization.WriteSyncObject(writer, index, indexType);

            writer.Log.Node($"Map id: {mapId}");
            Multiplayer.WriterLog.AddCurrentNode(writer);

            Multiplayer.Client.SendCommand(CommandType.Sync, mapId, writer.ToArray());

            return true;
        }

        public override void Handle(ByteReader data)
        {
            object target = null;
            if (targetType != null)
            {
                target = SyncSerialization.ReadSyncObject(data, targetType);
                if (target == null)
                    return;
            }

            object value = SyncSerialization.ReadSyncObject(data, fieldType);
            if (cancelIfValueNull && value == null)
                return;

            object index = null;
            if (indexType != null)
                index = SyncSerialization.ReadSyncObject(data, indexType);

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

            if (bufferChanges && SyncUtil.bufferedChanges[this].TryGetValue((target, index), out BufferData cached))
            {
                value = cached.toSend;
                target.SetPropertyOrField(memberPath, value, index);
            }
            else
            {
                value = SyncUtil.SnapshotValueIfNeeded(this, target.GetPropertyOrField(memberPath, index));
            }

            SyncUtil.watchedStack.Push(new FieldData(this, target, value, index));
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
            SyncUtil.bufferedChanges[this] = new();
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

        public ISyncField ExposeValue()
        {
            fieldType.expose = true;
            return this;
        }

        public override void Validate()
        {
            ValidateType("Target type", targetType);
            ValidateType("Field type", fieldType);
            ValidateType("Index type", indexType);
        }

        public override string ToString()
        {
            return $"SyncField {memberPath}";
        }
    }

    public record Serializer<Live, Networked>(Func<Live, object, object[], Networked> Writer, Func<Networked, Live> Reader);

    public static class Serializer
    {
        public static Serializer<Live, Networked> New<Live, Networked>(Func<Live, object, object[], Networked> Writer, Func<Networked, Live> Reader)
        {
            return new(Writer, Reader);
        }
    }

    public record SyncTransformer(Type LiveType, Type NetworkType, Delegate Writer, Delegate Reader);

    public delegate void SyncMethodWriter(object obj, SyncType type, string debugInfo);

    public class SyncMethod : SyncHandler, ISyncMethod
    {
        public readonly Type targetType;
        public readonly MethodInfo method;

        protected readonly string instancePath;

        protected SyncTransformer targetTransformer;
        public SyncType[] argTypes;
        protected string[] argNames;
        protected SyncTransformer[] argTransformers;

        private int minTime = 100; // Milliseconds between resends
        private long lastSendTime;

        private bool cancelIfAnyArgNull;
        private bool cancelIfNoSelectedMapObjects;
        private bool cancelIfNoSelectedWorldObjects;

        private Action<object, object[]> beforeCall;
        private Action<object, object[]> afterCall;

        public SyncMethod(Type targetType, string instancePath, MethodInfo method, SyncType[] inTypes)
        {
            this.method = method;
            this.targetType = targetType;
            this.instancePath = instancePath;

            argTypes = CheckArgs(method, inTypes);
            argNames = method.GetParameters().Names();
            argTransformers = new SyncTransformer[argTypes.Length];
        }

        private static SyncType[] CheckArgs(MethodInfo method, SyncType[] argTypes)
        {
            if (argTypes == null || argTypes.Length == 0)
                return method.GetParameters().Select(p => (SyncType)p).ToArray();
            else if (argTypes.Select(a => a.type).Zip(method.GetParameters().Types()).Any(t => t.a != t.b))
                throw new Exception($"Wrong parameter types for method: {method}");

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
            writer.Log.Node(ToString());

            writer.WriteInt32(syncId);
            SyncUtil.WriteContext(this, writer);

            var map = context.map;

            void SyncObj(object obj, SyncType type, string debugInfo)
            {
                writer.Log.Enter(debugInfo);

                try
                {
                    SyncSerialization.WriteSyncObject(writer, obj, type);
                }
                finally
                {
                    writer.Log.Exit();
                }

                if (type.contextMap && obj is Map contextMap)
                    map = contextMap;

                if (context.map is Map newMap)
                {
                    if (map != null && map != newMap)
                        throw new Exception($"{this}: map mismatch ({map?.uniqueID} and {newMap?.uniqueID})");
                    map = newMap;
                }
            }

            if (targetTransformer != null)
                SyncObj(targetTransformer.Writer.DynamicInvoke(target, target, args), targetTransformer.NetworkType, "Target (transformed)");
            else
                WriteTarget(target, args, SyncObj);

            for (int i = 0; i < argTypes.Length; i++)
                if (argTransformers[i] == null)
                    SyncObj(args[i], argTypes[i], $"Arg {i} {argNames[i]}");

            for (int i = 0; i < argTypes.Length; i++)
                if (argTransformers[i] is SyncTransformer trans)
                    SyncObj(trans.Writer.DynamicInvoke(args[i], target, args), trans.NetworkType, $"Arg {i} {argNames[i]} (transformed)");

            int mapId = map?.uniqueID ?? ScheduledCommand.Global;
            writer.Log.Node("Map id: " + mapId);
            Multiplayer.WriterLog.AddCurrentNode(writer);

            Multiplayer.Client.SendCommand(CommandType.Sync, mapId, writer.ToArray());

            lastSendTime = Utils.MillisNow;

            return true;
        }

        protected virtual void WriteTarget(object target, object[] args, SyncMethodWriter writer)
        {
            if (targetType != null)
                writer(target, targetType, "Target");
        }

        public override void Handle(ByteReader data)
        {
            object target;

            if (targetTransformer != null)
                target = targetTransformer.Reader.DynamicInvoke(SyncSerialization.ReadSyncObject(data, targetTransformer.NetworkType));
            else
                target = ReadTarget(data);

            if (targetType != null && target == null) return;

            if (!instancePath.NullOrEmpty())
                target = target.GetPropertyOrField(instancePath);

            var args = new object[argTypes.Length];

            for (int i = 0; i < argTypes.Length; i++)
                if (argTransformers[i] == null)
                    args[i] = SyncSerialization.ReadSyncObject(data, argTypes[i]);

            for (int i = 0; i < argTypes.Length; i++)
                if (argTransformers[i] is SyncTransformer trans)
                    args[i] = trans.Reader.DynamicInvoke(SyncSerialization.ReadSyncObject(data, trans.NetworkType));

            if (cancelIfAnyArgNull && args.Any(a => a == null))
                return;

            if (context.HasFlag(SyncContext.MapSelected) && cancelIfNoSelectedMapObjects && Find.Selector.selected.Count == 0)
                return;

            if (context.HasFlag(SyncContext.WorldSelected) && cancelIfNoSelectedWorldObjects && Find.WorldSelector.selected.Count == 0)
                return;

            beforeCall?.Invoke(target, args);

            MpLog.Log($"Invoked {method} on {target} with {args.Length} params {args.ToStringSafeEnumerable()}");
            method.Invoke(target, args);

            afterCall?.Invoke(target, args);
        }

        protected virtual object ReadTarget(ByteReader data)
        {
            if (targetType != null)
                return SyncSerialization.ReadSyncObject(data, targetType);

            return null;
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

        public SyncMethod TransformArgument<Live, Networked>(int index, Serializer<Live, Networked> serializer)
        {
            if (argTypes[index].type != typeof(Live))
                throw new Exception($"Arg transformer type mismatch for {this}: {argTypes[index].type} != {typeof(Live)}");

            argTransformers[index] = new(typeof(Live), typeof(Networked), serializer.Writer, serializer.Reader);
            return this;
        }

        public SyncMethod TransformTarget<Live, Networked>(Serializer<Live, Networked> serializer)
        {
            if (targetType != typeof(Live))
                throw new Exception($"Target transformer type mismatch for {this}: {targetType} != {typeof(Live)}");

            targetTransformer = new(typeof(Live), typeof(Networked), serializer.Writer, serializer.Reader);
            return this;
        }

        public SyncMethod SetHostOnly()
        {
            hostOnly = true;
            return this;
        }

        public static SyncMethod Register(Type type, string methodOrPropertyName, SyncType[] argTypes = null)
        {
            return Sync.RegisterSyncMethod(type, methodOrPropertyName, argTypes);
        }

        public static SyncMethod Lambda(Type parentType, string parentMethod, int lambdaOrdinal, Type[] parentArgs = null)
        {
            return Sync.RegisterSyncMethod(
                MpUtil.GetLambda(parentType, parentMethod, MethodType.Normal, parentArgs, lambdaOrdinal),
                null
            );
        }

        public override void Validate()
        {
            ValidateType("Target type", targetTransformer?.NetworkType ?? targetType);

            for (int i = 0; i < argTypes.Length; i++)
                ValidateType($"Arg {i} type", argTransformers[i]?.NetworkType ?? argTypes[i]);
        }

        public override string ToString()
        {
            return $"SyncMethod {method.MethodDesc()}";
        }
    }

    public class SyncDelegate : SyncMethod, ISyncDelegate
    {
        public const string DELEGATE_THIS = "<>4__this";

        private Type[] fieldTypes;
        private string[] fieldPaths;
        private string[] fieldPathsNoTypes;
        private SyncTransformer[] fieldTransformers;

        private string[] allowedNull;
        private string[] cancelIfNull;
        private string[] removeNullsFromLists;
        
        public SyncDelegate(Type delegateType, MethodInfo method, string[] inPaths) :
            base(delegateType, null, method, null)
        {
            if (inPaths == null)
            {
                List<string> fieldList = new List<string>();
                AllDelegateFieldsRecursive(delegateType, path => { fieldList.Add(path); return false; });
                fieldPaths = fieldList.ToArray();
            }
            else
            {
                var temp = new UniqueList<string>();
                foreach (string path in inPaths.Select(p => MpReflection.AppendType(p, delegateType)))
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

                fieldPaths = temp.ToArray();
            }

            fieldTypes = fieldPaths.Select(path => MpReflection.PathType(path)).ToArray();
            fieldPathsNoTypes = fieldPaths.Select(path => MpReflection.RemoveType(path)).ToArray();
            fieldTransformers = new SyncTransformer[fieldTypes.Length];
        }

        protected override void WriteTarget(object target, object[] args, SyncMethodWriter writer)
        {
            for (int i = 0; i < fieldPaths.Length; i++)
            {
                var val = target.GetPropertyOrField(fieldPaths[i]);
                var type = fieldTypes[i];
                var path = fieldPaths[i];

                if (fieldTransformers[i] is SyncTransformer tr)
                    writer(tr.Writer.DynamicInvoke(val, target, args), tr.NetworkType, path);
                else if (!fieldTypes[i].IsCompilerGenerated())
                    writer(val, type, path);
            }
        }

        protected override object ReadTarget(ByteReader data)
        {
            object target = Activator.CreateInstance(targetType);

            for (int i = 0; i < fieldPaths.Length; i++)
            {
                string path = fieldPaths[i];
                string noTypePath = fieldPathsNoTypes[i];
                Type fieldType = fieldTypes[i];
                object value;

                if (fieldTransformers[i] is SyncTransformer tr)
                    value = tr.Reader.DynamicInvoke(SyncSerialization.ReadSyncObject(data, tr.NetworkType));
                else if (fieldType.IsCompilerGenerated())
                    value = Activator.CreateInstance(fieldType);
                else
                    value = SyncSerialization.ReadSyncObject(data, fieldType);

                if (value == null)
                {
                    if (allowedNull != null && !allowedNull.Contains(noTypePath)) return null;
                    if (noTypePath.EndsWith(DELEGATE_THIS)) return null;
                    if (cancelIfNull != null && cancelIfNull.Contains(noTypePath)) return null;
                }

                if (removeNullsFromLists != null && removeNullsFromLists.Contains(noTypePath) && value is IList list)
                    list.RemoveNulls();

                MpReflection.SetValue(target, path, value);
            }

            return target;
        }

        public ISyncDelegate CancelIfAnyFieldNull(params string[] allowed)
        {
            CheckFieldsExist(allowed);
            allowedNull = allowed;
            return this;
        }

        public ISyncDelegate CancelIfFieldsNull(params string[] fields)
        {
            CheckFieldsExist(fields);
            cancelIfNull = fields;
            return this;
        }

        public ISyncDelegate RemoveNullsFromLists(params string[] listFields)
        {
            CheckFieldsExist(listFields);
            removeNullsFromLists = listFields;
            return this;
        }

        public ISyncMethod TransformField<Live, Networked>(string field, Serializer<Live, Networked> serializer)
        {
            CheckFieldsExist(field);

            var index = fieldPathsNoTypes.FindIndex(field);

            if (fieldTypes[index] != typeof(Live))
                throw new Exception($"Arg transformer param mismatch for {this}: {fieldTypes[index]} != {typeof(Live)}");

            fieldTransformers[index] = new(typeof(Live), typeof(Networked), serializer.Writer, serializer.Reader);
            return this;
        }

        private void CheckFieldsExist(params string[] fields)
        {
            foreach (var f in fields)
                if (!fieldPathsNoTypes.Contains(f))
                    throw new Exception($"Field with path {f} not found");
        }

        public static SyncDelegate Register(Type type, string nestedType, string method)
        {
            return Sync.RegisterSyncDelegate(type, nestedType, method);
        }

        public static SyncDelegate Lambda(Type parentType, string parentMethod, int lambdaOrdinal, Type[] parentArgs = null, MethodType parentMethodType = MethodType.Normal)
        {
            return Sync.RegisterSyncDelegate(
                MpUtil.GetLambda(parentType, parentMethod, parentMethodType, parentArgs, lambdaOrdinal),
                null
            );
        }

        public static SyncDelegate LocalFunc(Type parentType, string parentMethod, string name, Type[] parentArgs = null)
        {
            return Sync.RegisterSyncDelegate(
                MpUtil.GetLocalFunc(parentType, parentMethod, MethodType.Normal, parentArgs, name),
                null
            );
        }

        public static SyncDelegate Register(Type inType, string nestedType, string methodName, string[] fields)
        {
            return Sync.RegisterSyncDelegate(inType, nestedType, methodName, fields);
        }

        public override void Validate()
        {
            for (int i = 0; i < fieldTypes.Length; i++)
                if (fieldTransformers[i] is SyncTransformer tr)
                    ValidateType($"Field {fieldPaths[i]} type", tr.NetworkType);
                else if (!fieldTypes[i].IsCompilerGenerated())
                    ValidateType($"Field {fieldPaths[i]} type", fieldTypes[i]);

            for (int i = 0; i < argTypes.Length; i++)
                ValidateType($"Arg {i} type", argTransformers[i]?.NetworkType ?? argTypes[i]);
        }

        public override string ToString()
        {
            return $"SyncDelegate {method.MethodDesc()}";
        }

        public static bool AllDelegateFieldsRecursive(Type type, Func<string, bool> getter, string path = "", bool allowDelegates = false)
        {
            if (path.NullOrEmpty())
                path = type.ToString();

            foreach (FieldInfo field in type.GetDeclaredInstanceFields())
            {
                string curPath = path + "/" + field.Name;

                if (!allowDelegates && typeof(Delegate).IsAssignableFrom(field.FieldType))
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

        public ISyncDelegate CancelIfNoSelectedObjects()
        {
            CancelIfNoSelectedMapObjects();
            return this;
        }

        ISyncDelegate ISyncDelegate.SetContext(SyncContext context)
        {
            SetContext(context);
            return this;
        }

        ISyncDelegate ISyncDelegate.SetDebugOnly()
        {
            SetDebugOnly();
            return this;
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
            writer.Log.Node("Sync action");

            writer.WriteInt32(syncId);

            SyncSerialization.WriteSync(writer, target);
            SyncSerialization.WriteSync(writer, arg0);
            SyncSerialization.WriteSync(writer, arg1);

            writer.WriteInt32(GenText.StableStringHash(original.Method.MethodDesc()));
            Log.Message(original.Method.MethodDesc());

            int mapId = writer.MpContext().map?.uniqueID ?? -1;

            writer.Log.Node("Map id: " + mapId);
            Multiplayer.WriterLog.AddCurrentNode(writer);

            Multiplayer.Client.SendCommand(CommandType.Sync, mapId, writer.ToArray());
        }

        public override void Handle(ByteReader data)
        {
            A target = SyncSerialization.ReadSync<A>(data);
            B arg0 = SyncSerialization.ReadSync<B>(data);
            C arg1 = SyncSerialization.ReadSync<C>(data);

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

                    Multiplayer.harmony.PatchMeasure(method, prefix, postfix);
                    SyncActions.syncActions[method] = this;
                }
            }
        }

        public override void Validate()
        {
            ValidateType("Target type", typeof(A));
            ValidateType("Arg 0 type", typeof(B));
            ValidateType("Arg 1 type", typeof(C));
        }

        public override string ToString()
        {
            return "SyncAction";
        }
    }

}
