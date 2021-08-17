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
using System.Text;
using System.Threading.Tasks;
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

        public BufferData(object actualValue, object toSend)
        {
            this.actualValue = actualValue;
            this.toSend = toSend;
        }
    }

    [HotSwappable]
    public static class SyncUtil
    {
        public static Dictionary<SyncField, Dictionary<(object, object), BufferData>> bufferedChanges = new();
        public static Stack<FieldData> watchedStack = new Stack<FieldData>();

        public static bool isDialogNodeTreeOpen = false;

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
                bool changed = !ValuesEqual(handler, newValue, data.oldValue);
                var cache = (handler.bufferChanges && !Multiplayer.IsReplay) ? bufferedChanges.GetValueSafe(handler) : null;

                if (cache != null && cache.TryGetValue((data.target, data.index), out BufferData cached))
                {
                    if (changed && cached.sent)
                        cached.sent = false;

                    cached.toSend = SnapshotValueIfNeeded(handler, newValue);
                    MpReflection.SetValue(data.target, handler.memberPath, cached.actualValue, data.index);
                    continue;
                }

                if (!changed) continue;

                if (cache != null)
                {
                    BufferData bufferData = new BufferData(data.oldValue, SnapshotValueIfNeeded(handler, newValue));
                    cache[(data.target, data.index)] = bufferData;
                }
                else
                {
                    handler.DoSync(data.target, newValue, data.index);
                }

                MpReflection.SetValue(data.target, handler.memberPath, data.oldValue, data.index);
            }
        }

        public static void UpdateSync()
        {
            foreach (SyncField f in Sync.bufferedFields)
            {
                if (f.inGameLoop) continue;

                bufferedChanges[f].RemoveAll((k, data) =>
                {
                    if (CheckShouldRemove(f, k, data))
                        return true;

                    if (!data.sent && Utils.MillisNow - data.timestamp > 200)
                    {
                        f.DoSync(k.Item1, data.toSend, k.Item2);
                        data.sent = true;
                        data.timestamp = Utils.MillisNow;
                    }

                    return false;
                });
            }
        }

        public static bool CheckShouldRemove(SyncField field, (object, object) target, BufferData data)
        {
            if (data.sent && ValuesEqual(field, data.toSend, data.actualValue))
                return true;

            object currentValue = target.Item1.GetPropertyOrField(field.memberPath, target.Item2);

            if (!ValuesEqual(field, currentValue, data.actualValue))
            {
                if (data.sent)
                    return true;
                else
                    data.actualValue = SnapshotValueIfNeeded(field, currentValue);
            }

            return false;
        }

        public static object SnapshotValueIfNeeded(SyncField field, object value)
        {
            if (field.fieldType.expose)
                return SyncSerialization.ReadExposable(field.fieldType.type).Invoke(null, new[] { ScribeUtil.WriteExposable((IExposable)value), null });

            return value;
        }

        private static bool ValuesEqual(SyncField field, object newValue, object oldValue)
        {
            if (field.fieldType.expose)
            {
                return Enumerable.SequenceEqual(
                    ScribeUtil.WriteExposable((IExposable)newValue),
                    ScribeUtil.WriteExposable((IExposable)oldValue)
                );
            }

            return Equals(newValue, oldValue);
        }

        internal static void DialogNodeTreePostfix()
        {
            if (Multiplayer.Client != null && Find.WindowStack?.WindowOfType<Dialog_NodeTree>() != null)
                isDialogNodeTreeOpen = true;
        }

        internal static void PatchMethodForDialogNodeTreeSync(MethodBase method)
        {
            Multiplayer.harmony.PatchMeasure(method, postfix: new HarmonyMethod(typeof(SyncUtil), nameof(SyncUtil.DialogNodeTreePostfix)));
        }

        internal static void PatchMethodForSync(MethodBase method)
        {
            Multiplayer.harmony.PatchMeasure(method, transpiler: SyncTemplates.CreateTranspiler());
        }

        internal static void ApplyWatchFieldPatches(Type type)
        {
            HarmonyMethod prefix = new HarmonyMethod(AccessTools.Method(typeof(SyncUtil), nameof(SyncUtil.FieldWatchPrefix)));
            prefix.priority = MpPriority.MpFirst;
            HarmonyMethod postfix = new HarmonyMethod(AccessTools.Method(typeof(SyncUtil), nameof(SyncUtil.FieldWatchPostfix)));
            postfix.priority = MpPriority.MpLast;

            foreach (MethodBase toPatch in type.GetDeclaredMethods())
            {
                foreach (var attr in toPatch.AllAttributes<MpPrefix>())
                {
                    Multiplayer.harmony.PatchMeasure(attr.Method, prefix, postfix);
                }
            }
        }

        public static SyncHandler HandleCmd(ByteReader data)
        {
            int syncId = data.ReadInt32();
            SyncHandler handler;

            try
            {
                handler = Sync.handlers[syncId];
            }
            catch (ArgumentOutOfRangeException)
            {
                Log.Error($"Error: invalid syncId {syncId}/{Sync.handlers.Count}, this implies mismatched mods, ensure your versions match! Stacktrace follows.");
                throw;
            }

            List<object> prevSelected = Find.Selector.selected;
            List<WorldObject> prevWorldSelected = Find.WorldSelector.selected;

            bool shouldQueue = false;

            if (handler.context != SyncContext.None)
            {
                if (handler.context.HasFlag(SyncContext.MapMouseCell))
                {
                    IntVec3 mouseCell = SyncSerialization.ReadSync<IntVec3>(data);
                    MouseCellPatch.result = mouseCell;
                }

                if (handler.context.HasFlag(SyncContext.MapSelected))
                {
                    List<ISelectable> selected = SyncSerialization.ReadSync<List<ISelectable>>(data);
                    Find.Selector.selected = selected.Cast<object>().AllNotNull().ToList();
                }

                if (handler.context.HasFlag(SyncContext.WorldSelected))
                {
                    List<ISelectable> selected = SyncSerialization.ReadSync<List<ISelectable>>(data);
                    Find.WorldSelector.selected = selected.Cast<WorldObject>().AllNotNull().ToList();
                }

                if (handler.context.HasFlag(SyncContext.QueueOrder_Down))
                    shouldQueue = data.ReadBool();
            }

            KeyIsDownPatch.shouldQueue = shouldQueue;

            try
            {
                handler.Handle(data);
            }
            finally
            {
                MouseCellPatch.result = null;
                KeyIsDownPatch.shouldQueue = null;
                Find.Selector.selected = prevSelected;
                Find.WorldSelector.selected = prevWorldSelected;
            }

            return handler;
        }

        public static void WriteContext(SyncHandler handler, ByteWriter data)
        {
            if (handler.context == SyncContext.None) return;

            if (handler.context.HasFlag(SyncContext.CurrentMap))
                data.MpContext().map = Find.CurrentMap;

            if (handler.context.HasFlag(SyncContext.MapMouseCell))
            {
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
}
