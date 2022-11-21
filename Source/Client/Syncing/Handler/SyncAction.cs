using HarmonyLib;
using Multiplayer.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Multiplayer.Client
{
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

            SendSyncCommand(mapId, writer);
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
