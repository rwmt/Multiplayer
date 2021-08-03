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
        /// Returns whether the original should be cancelled
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
                SyncSerialization.WriteSyncObject(writer, target, targetType);
                if (context.map != null)
                    mapId = context.map.uniqueID;
            }

            SyncSerialization.WriteSyncObject(writer, value, fieldType);
            if (indexType != null)
                SyncSerialization.WriteSyncObject(writer, index, indexType);

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

            if (bufferChanges && Sync.bufferedChanges[this].TryGetValue((target, index), out BufferData cached))
            {
                value = cached.toSend;
                target.SetPropertyOrField(memberPath, value, index);
            }
            else
            {
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
            Sync.bufferedChanges[this] = new();
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

            method = AccessTools.Method(instanceType, methodName, argTypes?.Select(t => t.type).ToArray())
                ?? throw new Exception($"Couldn't find method {instanceType}::{methodName}");

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
                SyncSerialization.WriteSyncObject(writer, target, targetType);
                if (context.map is Map newMap)
                    map = newMap;
            }

            for (int i = 0; i < argTypes.Length; i++)
            {
                var argType = argTypes[i];
                SyncSerialization.WriteSyncObject(writer, args[i], argType);

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
                target = SyncSerialization.ReadSyncObject(data, targetType);
                if (target == null)
                    return;
            }

            if (!instancePath.NullOrEmpty())
                target = target.GetPropertyOrField(instancePath);

            object[] args = null;
            if (argTypes != null)
            {
                args = SyncSerialization.ReadSyncObjects(data, argTypes);
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

            if (fieldPaths == null)
            {
                List<string> fieldList = new List<string>();
                AllDelegateFieldsRecursive(delegateType, path => { fieldList.Add(path); return false; });
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
            writer.log.Node($"Sync delegate: {delegateType} method: {method}");
            writer.log.Node("Patch: " + patch?.FullDescription());

            writer.WriteInt32(syncId);

            Sync.WriteContext(this, writer);

            int mapId = ScheduledCommand.Global;

            IEnumerable<object> fields = fieldPaths.Select(p => delegateInstance.GetPropertyOrField(p));

            EnumerableHelper.ProcessCombined(fields.Concat(args), fieldTypes.Concat(argTypes), (obj, type) => {
                if (type.IsCompilerGenerated())
                    return;

                SyncSerialization.WriteSyncObject(writer, obj, type);

                if (context.map is Map map)
                {
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

            for (int i = 0; i < fieldPaths.Length; i++)
            {
                string path = fieldPaths[i];
                string noTypePath = MpReflection.RemoveType(path);
                Type fieldType = fieldTypes[i];
                object value;

                if (fieldType.IsCompilerGenerated())
                    value = Activator.CreateInstance(fieldType);
                else
                    value = SyncSerialization.ReadSyncObject(data, fieldType);

                if (value == null)
                {
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

            object[] parameters = SyncSerialization.ReadSyncObjects(data, argTypes);

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

        private static bool AllDelegateFieldsRecursive(Type type, Func<string, bool> getter, string path = "")
        {
            if (path.NullOrEmpty())
                path = type.ToString();

            foreach (FieldInfo field in type.GetDeclaredInstanceFields())
            {
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
            writer.log.Node("Sync action");

            writer.WriteInt32(syncId);

            SyncSerialization.WriteSync(writer, target);
            SyncSerialization.WriteSync(writer, arg0);
            SyncSerialization.WriteSync(writer, arg1);

            writer.WriteInt32(GenText.StableStringHash(original.Method.MethodDesc()));
            Log.Message(original.Method.MethodDesc());

            int mapId = writer.MpContext().map?.uniqueID ?? -1;

            writer.log.Node("Map id: " + mapId);
            Multiplayer.WriterLog.nodes.Add(writer.log.current);

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

                    Multiplayer.harmony.Patch(method, prefix, postfix);
                    SyncActions.syncActions[method] = this;
                }
            }
        }

        public override string ToString()
        {
            return "SyncAction";
        }
    }

}
