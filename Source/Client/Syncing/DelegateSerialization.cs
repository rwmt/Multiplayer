using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Multiplayer.Client;

public static class DelegateSerialization
{
    public static void WriteDelegate(ByteWriter writer, Delegate del)
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
            var fieldTypes = fieldPaths.Select(MpReflection.PathType).ToArray();

            void SyncObj(object obj, Type type, string debugInfo)
            {
                if (type.IsCompilerGenerated())
                    return;

                (writer as LoggingByteWriter)?.Log.Enter(debugInfo);

                try
                {
                    if (typeof(Delegate).IsAssignableFrom(type))
                        WriteDelegate(writer, (Delegate)obj);
                    else
                        SyncSerialization.WriteSyncObject(writer, obj, type);
                }
                finally
                {
                    (writer as LoggingByteWriter)?.Log.Exit();
                }
            }

            for (int i = 0; i < fieldPaths.Length; i++)
                SyncObj(del.Target.GetPropertyOrField(fieldPaths[i]), fieldTypes[i], fieldPaths[i]);
        }
    }

    public static Delegate ReadDelegate(ByteReader reader)
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
            CheckMethodAllowed(AccessTools.Method(type, methodName))
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

    private static Type[] allowedDeclaringTypes =
    {
        // For Dialog_BeginRitual.action
        typeof(Ability),
        typeof(AbilityComp),
        typeof(Command),
        typeof(ThingComp),
        typeof(Dialog_BeginRitual),
        typeof(LordToil),
        typeof(Precept),
        typeof(SocialCardUtility),

        // For DiaOption.action
        typeof(Letter),
        typeof(FactionDialogMaker),
        typeof(GenGameEnd),
        typeof(IncidentWorker),
        typeof(QuestPart),
        typeof(ResearchManager),
        typeof(ShipUtility),
    };

    private static bool IsDeclaringTypeAllowed(Type declaringType)
    {
        while (declaringType.DeclaringType != null)
            declaringType = declaringType.DeclaringType;

        do
        {
            if (allowedDeclaringTypes.Contains(declaringType))
                return true;
            declaringType = declaringType.BaseType;
        } while (declaringType != null);

        return false;
    }

    public static MethodInfo CheckMethodAllowed(MethodInfo method)
    {
        if (IsDeclaringTypeAllowed(method.DeclaringType))
            return method;

        throw new Exception($"Delegate deserialization: method not allowed {method.MethodDesc()}");
    }
}
