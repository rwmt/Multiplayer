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

        public static void StackPush(SyncField field, object target, object value, object index)
        {
            watchedStack.Push(new FieldData(field, target, value, index));
        }

        // True if an enclosing watch scope already watches this field of this target. That scope restores and syncs it,
        // watching it again in a nested scope would revert the pending (buffered) value in the middle of the outer scope.
        internal static bool IsWatched(SyncField field, object target, object index)
        {
            foreach (var entry in watchedStack)
                if (entry is { } data && data.handler == field && Equals(data.target, target) && Equals(data.index, index))
                    return true;

            return false;
        }

        public static void FieldWatchPrefix()
        {
            if (Multiplayer.Client == null) return;
            watchedStack.Push(null); // Marker
        }

        public static void FieldWatchPostfix()
        {
            if (Multiplayer.Client == null) return;
            PopWatchScope();
        }

        // Harmony patches use a finalizer, so the simulation values get restored even if the watched method throws
        static void FieldWatchPatchPrefix(out bool __state)
        {
            __state = Multiplayer.Client != null;
            if (__state)
                watchedStack.Push(null); // Marker
        }

        static void FieldWatchPatchFinalizer(bool __state)
        {
            if (__state)
                PopWatchScope();
        }

        /// Wraps the action in a watch scope which is closed even if the action throws.
        internal static Action WatchScope(Action watchAction, Action action) => () =>
        {
            bool pushed = Multiplayer.Client != null;
            if (pushed)
                watchedStack.Push(null); // Marker

            try
            {
                watchAction();
                action();
            }
            finally
            {
                if (pushed)
                    PopWatchScope();
            }
        };

        private static void PopWatchScope()
        {
            // Iterate until we hit the marker (`null`). Always pop the whole scope, even if handling an entry fails.
            while (watchedStack.TryPop(out var dataOrNull) && dataOrNull is { } data)
            {
                try
                {
                    HandleWatchedField(data);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception handling watched {data.handler}: {e}");
                }
            }
        }

        private static void HandleWatchedField(FieldData data)
        {
            SyncField handler = data.handler;
            object newValue = MpReflection.GetValue(data.target, handler.memberPath, data.index);
            bool changed = !ValuesEqual(handler, newValue, data.oldValue);
            var cache =
                handler.bufferChanges && !Multiplayer.IsReplay && !Multiplayer.GhostMode
                    ? bufferedChanges.GetValueSafe(handler)
                    : null;
            var bufferTarget = new BufferTarget(data.target, data.index);
            var cachedData = cache?.GetValueSafe(bufferTarget);

            // Revert the local field's value for simulation purposes.
            // For unbuffered fields, this rollback is only necessary if the value actually changed.
            // For buffered fields, however, we always perform the restore, since the value is overwritten
            // whenever watching it begins — assuming the value was previously changed and thus the buffer was
            // initialized.
            // If any change has happened, we record it and either send it immediately (unbuffered field) or queue
            // it (buffered field). The server will eventually acknowledge the change and send it back, at which
            // point the field is updated.
            if (changed || cachedData != null)
            {
                // The actual value can be null, so don't fall back to oldValue (the pending UI value) based on it
                var simulationValue = cachedData != null ? cachedData.actualValue : data.oldValue;
                MpReflection.SetValue(data.target, handler.memberPath, simulationValue, data.index);
            }

            // No changes happened means no syncing needed either - we are done.
            if (!changed)
                return;

            // For unbuffered fields, just immediately sync any changes.
            if (cache == null)
            {
                handler.DoSyncCatch(data.target, newValue, data.index);
                return;
            }

            // For buffered fields, update the value to be sent.
            if (cachedData != null)
            {
                cachedData.sent = false;
                cachedData.lastChangedAtMillis = Utils.MillisNow;
                cachedData.toSend = SnapshotValueIfNeeded(handler, newValue);
                return;
            }

            // The field is buffered but had no prior changes; initialize the cache entry now that a change has occurred.
            cache[bufferTarget] = new BufferData(handler, data.oldValue, SnapshotValueIfNeeded(handler, newValue));
        }

        public static void UpdateSync()
        {
            // Watch scopes never span frames. Leftovers mean a scope wasn't closed (e.g. an exception between the API's
            // WatchBegin and WatchEnd), restore and sync those fields now.
            if (watchedStack.Count > 0)
            {
                Log.WarningOnce($"Multiplayer: {watchedStack.Count} field watch stack entries weren't closed, restoring them", 0x5e1d3a7);
                while (watchedStack.Count > 0)
                    PopWatchScope();
            }

            foreach (var (field, fieldBufferedChanges) in bufferedChanges)
            {
                if (field.inGameLoop) continue;
                fieldBufferedChanges.RemoveAll(SyncPendingAndPruneFinished);
            }
        }

        private static bool SyncPendingAndPruneFinished(BufferTarget target, BufferData data)
        {
            if (data.IsAlreadySynced(target))
                return true;

            var millisNow = Utils.MillisNow;
            if (!data.sent && millisNow - data.lastChangedAtMillis > 200)
            {
                // If syncing fails with an exception don't try to reattempt and just give up.
                if (data.field.DoSyncCatch(target.target, data.toSend, target.index) is false)
                    return true;

                data.sent = true;
            }

            return false;
        }

        private static bool IsAlreadySynced(this BufferData data, BufferTarget target)
        {
            var field = data.field;
            if (data.sent && ValuesEqual(field, data.toSend, data.actualValue)) return true;

            object currentValue = target.target.GetPropertyOrField(field.memberPath, target.index);

            // Data hasn't been sent yet, or we are waiting for the server to acknowledge it and send it back.
            if (ValuesEqual(field, currentValue, data.actualValue)) return false;

            // The last seen value differs from the current field value — likely because a sync command from the server
            // has overwritten it (possibly even with the desired value). If we've already sent our update, we assume it's fine;
            // once the server processes it, it will likely acknowledge and send back the update via another sync command,
            // restoring the field to the intended value.
            if (data.sent) return true;

            data.actualValue = SnapshotValueIfNeeded(field, currentValue);
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
            HarmonyMethod prefix = new HarmonyMethod(AccessTools.Method(typeof(SyncFieldUtil), nameof(FieldWatchPatchPrefix)));
            prefix.priority = MpPriority.MpFirst;
            HarmonyMethod finalizer = new HarmonyMethod(AccessTools.Method(typeof(SyncFieldUtil), nameof(FieldWatchPatchFinalizer)));
            finalizer.priority = MpPriority.MpLast;

            foreach (var toPatch in type.GetDeclaredMethods())
            {
                foreach (var attr in toPatch.AllAttributes<MpPrefix>())
                {
                    try
                    {
                        Multiplayer.harmony.PatchMeasure(attr.Method, prefix, finalizer: finalizer);
                    } catch (Exception e) {
                        Log.Error($"FAIL: {attr.Method.DeclaringType.FullName}:{attr.Method.Name} with {e}");
                        Multiplayer.loadingErrors = true;
                    }
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
        /// Reflects the value the field had before the most recent GUI update.
        /// For unbuffered fields, this is also the current simulation value.
        /// For buffered fields, which may modify the field across multiple `Watch` calls,
        /// this represents the value at the start of the latest `Watch` invocation.
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

    public class BufferData(SyncField field, object actualValue, object toSend)
    {
        public SyncField field = field;
        /// This is the real field's value. If this were an unbuffered field, it'd be equivalent to `FieldData.oldValue`,
        /// however for buffered fields `oldValue` reflects the value prior to the last GUI update. Use this field to
        /// access the original value before any user interaction occurred.
        public object actualValue = actualValue;
        public object toSend = toSend;
        public long lastChangedAtMillis = Utils.MillisNow;
        public bool sent;
    }
}
