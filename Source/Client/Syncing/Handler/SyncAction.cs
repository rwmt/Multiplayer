using HarmonyLib;
using Multiplayer.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.Client.Util;
using Verse;

namespace Multiplayer.Client
{
    public delegate ref Action ActionGetter<in T>(T t);
    public delegate Action ActionWrapper<in T, in A, in B, in C>(T instance, A a, B b, C c, Action original, Action syncAction);

    public interface ISyncAction
    {
        IEnumerable DoSync(object target, object arg0, object arg1);
    }

    public class SyncAction<T, A, B, C> : SyncHandler, ISyncAction
    {
        private Func<A, B, C, IEnumerable<T>> func;
        private ActionGetter<T> actionGetter;
        private ActionWrapper<T, A, B, C> actionWrapper;

        public SyncAction(Func<A, B, C, IEnumerable<T>> func, ActionGetter<T> actionGetter, ActionWrapper<T, A, B, C> actionWrapper = null)
        {
            this.func = func;
            this.actionGetter = actionGetter;
            this.actionWrapper = actionWrapper ?? ((_, _, _, _, _, _) => null);
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
                    var sync = () => ActualSync(target, arg0, arg1, original);
                    var wrapper = actionWrapper(t, target, arg0, arg1, original, sync);
                    actionGetter(t) = wrapper ?? sync;

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

            var methodDesc = original.Method.MethodDesc();
            writer.Log.Node($"Method desc: {methodDesc}");
            writer.WriteInt32(GenText.StableStringHash(methodDesc));

            // If target is null then just sync the hash for null
            var typeDesc = (original.Target?.GetType()).FullDescription();
            writer.Log.Node($"Type desc: {typeDesc}");
            writer.WriteInt32(GenText.StableStringHash(typeDesc));

            int mapId = writer.MpContext().map?.uniqueID ?? -1;

            writer.Log.Node($"Map id: {mapId}");
            Multiplayer.WriterLog.AddCurrentNode(writer);

            SendSyncCommand(mapId, writer);
        }

        public override void Handle(ByteReader data)
        {
            A target = SyncSerialization.ReadSync<A>(data);
            B arg0 = SyncSerialization.ReadSync<B>(data);
            C arg1 = SyncSerialization.ReadSync<C>(data);

            int methodDescHash = data.ReadInt32();
            int typeDescHash = data.ReadInt32();

            var action = func(target, arg0, arg1)
                .Where(t =>
                {
                    var a = actionGetter(t);
                    // Match both the method description and target type description (including generics), or "null" string for the type
                    return GenText.StableStringHash(a.Method.MethodDesc()) == methodDescHash &&
                           GenText.StableStringHash((a.Target?.GetType()).FullDescription()) == typeDescHash;
                })
                .Select(t =>
                {
                    var a = actionGetter(t);
                    var w = actionWrapper(t, target, arg0, arg1, a, null);
                    // Return the wrapper (if present) or the action itself
                    return w ?? a;
                })
                .FirstOrDefault();

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
