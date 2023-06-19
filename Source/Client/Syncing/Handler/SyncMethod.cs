using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common;
using System;
using System.Linq;
using System.Reflection;
using Multiplayer.Client.Util;
using Verse;

namespace Multiplayer.Client
{
    public record Serializer<Live, Networked>(
        Func<Live, object, object[], Networked> Writer, // (live, target, args) => networked
        Func<Networked, Live> Reader // (networked) => live
    );

    public static class Serializer
    {
        public static Serializer<Live, Networked> New<Live, Networked>(Func<Live, object, object[], Networked> writer, Func<Networked, Live> reader)
        {
            return new(writer, reader);
        }

        public static Serializer<Live, Networked> New<Live, Networked>(Func<Live, Networked> writer, Func<Networked, Live> reader)
        {
            return new((live, _, _) => writer(live), reader);
        }

        public static Serializer<Live, object> SimpleReader<Live>(Func<Live> reader)
        {
            return new((_, _, _) => null, _ => reader());
        }
    }

    public record SyncTransformer(Type LiveType, Type NetworkType, Delegate Writer, Delegate Reader);

    public delegate void SyncMethodWriter(object obj, SyncType type, string debugInfo);


    public class SyncMethod : SyncHandler, ISyncMethod
    {
        public readonly Type targetType;
        public readonly MethodInfo method;
        public readonly FastInvokeHandler methodDelegate;

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

            methodDelegate = MethodInvoker.GetHandler(method);
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
                if (argTransformers[i] is { } trans)
                    SyncObj(trans.Writer.DynamicInvoke(args[i], target, args), trans.NetworkType, $"Arg {i} {argNames[i]} (transformed)");

            int mapId = map?.uniqueID ?? ScheduledCommand.Global;
            writer.Log.Node("Map id: " + mapId);
            Multiplayer.WriterLog.AddCurrentNode(writer);

            SendSyncCommand(mapId, writer);

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

            if (targetType != null && target == null)
            {
                MpLog.Debug($"SyncMethod {this} read target null");
                return;
            }

            if (!instancePath.NullOrEmpty())
                target = target.GetPropertyOrField(instancePath);

            var args = new object[argTypes.Length];

            for (int i = 0; i < argTypes.Length; i++)
                if (argTransformers[i] == null)
                    args[i] = SyncSerialization.ReadSyncObject(data, argTypes[i]);

            for (int i = 0; i < argTypes.Length; i++)
                if (argTransformers[i] is { } trans)
                    args[i] = trans.Reader.DynamicInvoke(SyncSerialization.ReadSyncObject(data, trans.NetworkType));

            if (cancelIfAnyArgNull && args.Any(a => a == null))
                return;

            if (context.HasFlag(SyncContext.MapSelected) && cancelIfNoSelectedMapObjects && Find.Selector.selected.Count == 0)
                return;

            if (context.HasFlag(SyncContext.WorldSelected) && cancelIfNoSelectedWorldObjects && Find.WorldSelector.selected.Count == 0)
                return;

            beforeCall?.Invoke(target, args);

            MpLog.Debug($"Invoked {method} on {target} with {args.Length} params {args.ToStringSafeEnumerable()}");
            methodDelegate.Invoke(target, args);

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
                MpMethodUtil.GetLambda(parentType, parentMethod, MethodType.Normal, parentArgs, lambdaOrdinal),
                null
            );
        }

        public static SyncMethod LambdaInGetter(Type parentType, string parentMethod, int lambdaOrdinal)
        {
            return Sync.RegisterSyncMethod(
                MpMethodUtil.GetLambda(parentType, parentMethod, MethodType.Getter, null, lambdaOrdinal),
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

}
