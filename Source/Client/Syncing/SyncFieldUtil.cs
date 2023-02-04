using HarmonyLib;
using Multiplayer.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Multiplayer.Client
{
    public static class SyncFieldUtil
    {
        public static Dictionary<SyncField, Dictionary<BufferTarget, BufferData>> bufferedChanges = new();
        private static Stack<FieldData?> watchedStack = new();

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
                var dataOrNull = watchedStack.Pop();

                if (dataOrNull is not { } data)
                    break; // The marker

                SyncField handler = data.handler;

                object newValue = MpReflection.GetValue(data.target, handler.memberPath, data.index);
                bool changed = !ValuesEqual(handler, newValue, data.oldValue);
                var cache =
                    handler.bufferChanges && !Multiplayer.IsReplay && !Multiplayer.GhostMode ?
                        bufferedChanges.GetValueSafe(handler) :
                        null;

                if (cache != null && cache.TryGetValue(new(data.target, data.index), out BufferData cached))
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
                    BufferData bufferData = new BufferData(handler, data.oldValue, SnapshotValueIfNeeded(handler, newValue));
                    cache[new(data.target, data.index)] = bufferData;
                }
                else
                {
                    handler.DoSyncCatch(data.target, newValue, data.index);
                }

                MpReflection.SetValue(data.target, handler.memberPath, data.oldValue, data.index);
            }
        }

        public static void StackPush(SyncField field, object target, object value, object index)
        {
            watchedStack.Push(new FieldData(field, target, value, index));
        }

        public static Func<BufferTarget, BufferData, bool> BufferedChangesPruner(Func<long> timeGetter)
        {
            return (target, data) =>
            {
                if (CheckShouldRemove(data.field, target, data))
                    return true;

                if (!data.sent && timeGetter() - data.timestamp > 200)
                {
                    if (data.field.DoSyncCatch(target.target, data.toSend, target.index) is false)
                        return true;

                    data.sent = true;
                    data.timestamp = timeGetter();
                }

                return false;
            };
        }

        private static Func<BufferTarget, BufferData, bool> bufferPredicate =
            BufferedChangesPruner(() => Utils.MillisNow);

        public static void UpdateSync()
        {
            foreach (SyncField f in Sync.bufferedFields)
            {
                if (f.inGameLoop) continue;
                bufferedChanges[f].RemoveAll(bufferPredicate);
            }
        }

        public static bool CheckShouldRemove(SyncField field, BufferTarget target, BufferData data)
        {
            if (data.sent && ValuesEqual(field, data.toSend, data.actualValue))
                return true;

            object currentValue = target.target.GetPropertyOrField(field.memberPath, target.index);

            if (!ValuesEqual(field, currentValue, data.actualValue))
            {
                if (data.sent)
                    return true;
                data.actualValue = SnapshotValueIfNeeded(field, currentValue);
            }

            return false;
        }

        public static object SnapshotValueIfNeeded(SyncField field, object value)
        {
            if (field.fieldType.expose)
                return ExposableSerialization.ReadExposable(field.fieldType.type, ScribeUtil.WriteExposable((IExposable)value));

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

        internal static void ApplyWatchFieldPatches(Type type)
        {
            HarmonyMethod prefix = new HarmonyMethod(AccessTools.Method(typeof(SyncFieldUtil), nameof(FieldWatchPrefix)));
            prefix.priority = MpPriority.MpFirst;
            HarmonyMethod postfix = new HarmonyMethod(AccessTools.Method(typeof(SyncFieldUtil), nameof(FieldWatchPostfix)));
            postfix.priority = MpPriority.MpLast;

            foreach (var toPatch in type.GetDeclaredMethods())
            {
                foreach (var attr in toPatch.AllAttributes<MpPrefix>())
                {
                    Multiplayer.harmony.PatchMeasure(attr.Method, prefix, postfix);
                }
            }
        }

        public static void ClearAllBufferedChanges()
        {
            foreach (var entry in bufferedChanges)
                entry.Value.Clear();
        }
    }

    public readonly struct FieldData
    {
        public readonly SyncField handler;
        public readonly object target;
        public readonly object oldValue;
        public readonly object index;

        public FieldData(SyncField handler, object target, object oldValue, object index)
        {
            this.handler = handler;
            this.target = target;
            this.oldValue = oldValue;
            this.index = index;
        }
    }

    public readonly struct BufferTarget
    {
        public readonly object target;
        public readonly object index;

        public BufferTarget(object target, object index)
        {
            this.target = target;
            this.index = index;
        }

        public override bool Equals(object obj)
        {
            return obj is BufferTarget other && Equals(target, other.target) && Equals(index, other.index);
        }

        public override int GetHashCode()
        {
            return Gen.HashCombineInt(target?.GetHashCode() ?? 0, index?.GetHashCode() ?? 0);
        }
    }

    public class BufferData
    {
        public SyncField field;
        public object actualValue;
        public object toSend;
        public long timestamp;
        public bool sent;

        public BufferData(SyncField field, object actualValue, object toSend)
        {
            this.field = field;
            this.actualValue = actualValue;
            this.toSend = toSend;
        }
    }
}
