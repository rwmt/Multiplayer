using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using static RimWorld.Dialog_BeginRitual;

namespace Multiplayer.Client.Persistent
{
    public class RitualData : ISynchronizable
    {
        public Precept_Ritual ritual;
        public TargetInfo target;
        public RitualObligation obligation;
        public RitualOutcomeEffectDef outcome;
        public List<string> extraInfos;
        public ActionCallback action;
        public string ritualLabel;
        public string confirmText;
        public Pawn organizer;
        public MpRitualAssignments assignments;

        public void Sync(SyncWorker sync)
        {
            sync.Bind(ref ritual);
            sync.Bind(ref target);
            sync.Bind(ref obligation);
            sync.Bind(ref outcome);
            sync.Bind(ref extraInfos);

            if (sync is WritingSyncWorker writer1)
                WriteDelegate(writer1.writer, action);
            else if (sync is ReadingSyncWorker reader)
                action = (ActionCallback)ReadDelegate(reader.reader);

            sync.Bind(ref ritualLabel);
            sync.Bind(ref confirmText);
            sync.Bind(ref organizer);

            if (sync is WritingSyncWorker writer2)
                writer2.Bind(ref assignments, new SyncType(typeof(MpRitualAssignments)) { expose = true });
            else if (sync is ReadingSyncWorker reader)
                reader.Bind(ref assignments, new SyncType(typeof(MpRitualAssignments)) { expose = true });
        }

        private static void WriteDelegate(ByteWriter writer, Delegate del)
        {
            writer.WriteBool(del != null);
            if (del == null) return;

            SyncSerialization.WriteSync(writer, del.GetType());
            SyncSerialization.WriteSync(writer, del.Method.DeclaringType);
            SyncSerialization.WriteSync(writer, del.Method.Name); // todo Handle the signature for ambiguous methods

            writer.WriteBool(del.Target != null);
            if (del.Target != null)
            {
                var targetType = del.Target.GetType();
                SyncSerialization.WriteSync(writer, targetType);

                var fieldPaths = GetFields(targetType).ToArray();
                var fieldTypes = fieldPaths.Select(path => MpReflection.PathType(path)).ToArray();

                void SyncObj(object obj, Type type, string debugInfo)
                {
                    if (type.IsCompilerGenerated())
                        return;

                    if (writer is LoggingByteWriter log1)
                        log1.Log.Enter(debugInfo);

                    try
                    {
                        if (typeof(Delegate).IsAssignableFrom(type))
                            WriteDelegate(writer, (Delegate)obj);
                        else
                            SyncSerialization.WriteSyncObject(writer, obj, type);
                    }
                    finally
                    {
                        if (writer is LoggingByteWriter log2)
                            log2.Log.Exit();
                    }
                }

                for (int i = 0; i < fieldPaths.Length; i++)
                    SyncObj(del.Target.GetPropertyOrField(fieldPaths[i]), fieldTypes[i], fieldPaths[i]);
            }
        }

        private static Delegate ReadDelegate(ByteReader reader)
        {
            if (!reader.ReadBool()) return null;

            var delegateType = SyncSerialization.ReadSync<Type>(reader);
            var type = SyncSerialization.ReadSync<Type>(reader);
            var methodName = SyncSerialization.ReadSync<string>(reader);
            object target = null;

            if (reader.ReadBool())
            {
                var targetType = SyncSerialization.ReadSync<Type>(reader);
                var fieldPaths = GetFields(targetType).ToArray();
                var fieldTypes = fieldPaths.Select(path => MpReflection.PathType(path)).ToArray();

                target = Activator.CreateInstance(targetType);

                for (int i = 0; i < fieldPaths.Length; i++)
                {
                    string path = fieldPaths[i];
                    string noTypePath = MpReflection.RemoveType(path);
                    Type fieldType = fieldTypes[i];
                    object value;

                    if (fieldType.IsCompilerGenerated())
                        value = Activator.CreateInstance(fieldType);
                    else if (typeof(Delegate).IsAssignableFrom(fieldType))
                        value = ReadDelegate(reader);
                    else
                        value = SyncSerialization.ReadSyncObject(reader, fieldType);

                    MpReflection.SetValue(target, path, value);
                }
            }

            return Delegate.CreateDelegate(
                delegateType,
                target,
                AccessTools.Method(type, methodName)
            );
        }

        const string CachedLambda = "<>9";

        private static List<string> GetFields(Type targetType)
        {
            var fieldList = new List<string>();
            SyncDelegate.AllDelegateFieldsRecursive(
                targetType,
                path => { if (!path.Contains(CachedLambda)) fieldList.Add(path); return false; },
                allowDelegates: true
            );

            return fieldList;
        }
    };
}
