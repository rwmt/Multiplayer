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
    public record RitualData(
        Precept_Ritual Ritual,
        TargetInfo Target,
        RitualObligation Obligation,
        RitualOutcomeEffectDef Outcome,
        List<string> ExtraInfos,
        ActionCallback Action,
        string RitualLabel,
        string ConfirmText,
        Pawn Organizer,
        MpRitualAssignments Assignments)
    {
        public static void Write(ByteWriter writer, RitualData data)
        {
            SyncSerialization.WriteSync(writer, data.Ritual);
            SyncSerialization.WriteSync(writer, data.Target);
            SyncSerialization.WriteSync(writer, data.Obligation);
            SyncSerialization.WriteSync(writer, data.Outcome);
            SyncSerialization.WriteSync(writer, data.ExtraInfos);
            WriteDelegate(writer, data.Action);
            SyncSerialization.WriteSync(writer, data.RitualLabel);
            SyncSerialization.WriteSync(writer, data.ConfirmText);
            SyncSerialization.WriteSync(writer, data.Organizer);
            SyncSerialization.WriteSyncObject(writer, data.Assignments, new SyncType(typeof(MpRitualAssignments)) { expose = true });
        }

        public static RitualData Read(ByteReader reader)
        {
            var ritual = SyncSerialization.ReadSync<Precept_Ritual>(reader);
            var target = SyncSerialization.ReadSync<TargetInfo>(reader);
            var obligation = SyncSerialization.ReadSync<RitualObligation>(reader);
            var outcome = SyncSerialization.ReadSync<RitualOutcomeEffectDef>(reader);
            var extraInfos = SyncSerialization.ReadSync<List<string>>(reader);
            var action = (ActionCallback)ReadDelegate(reader);
            var label = SyncSerialization.ReadSync<string>(reader);
            var confirmText = SyncSerialization.ReadSync<string>(reader);
            var organizer = SyncSerialization.ReadSync<Pawn>(reader);
            var assignments = (MpRitualAssignments)SyncSerialization.ReadSyncObject(reader, new SyncType(typeof(MpRitualAssignments)) { expose = true });

            return new(
                ritual,
                target,
                obligation,
                outcome,
                extraInfos,
                action,
                label,
                confirmText,
                organizer,
                assignments
            );
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
