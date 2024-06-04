using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client;

public static class RwSerialization
{
    public static void Init()
    {
        Multiplayer.serialization = new Common.SyncSerialization(new RwSyncTypeHelper());

        // CanHandle hooks
        Multiplayer.serialization.AddCanHandleHook(syncType =>
        {
            var type = syncType.type;

            // Verse.Pair<,>
            if (type.IsGenericType && type.GetGenericTypeDefinition() is { } gtd)
                if (gtd == typeof(Pair<,>))
                    return Multiplayer.serialization.CanHandleGenericArgs(type);

            // Verse.IExposable
            if (syncType.expose)
                return typeof(IExposable).IsAssignableFrom(type);

            // Multiplayer.API.ISyncSimple
            if (type == typeof(ISyncSimple))
                return true;
            if (typeof(ISyncSimple).IsAssignableFrom(type))
                return ApiSerialization.syncSimples.
                    Where(t => type.IsAssignableFrom(t)).
                    SelectMany(AccessTools.GetDeclaredFields).
                    All(f => Multiplayer.serialization.CanHandle(f.FieldType));

            return false;
        });

        // Verse.Pair<,> serialization
        Multiplayer.serialization.AddSerializationHook(
            syncType => syncType.type.IsGenericType && syncType.type.GetGenericTypeDefinition() is { } gtd && gtd == typeof(Pair<,>),
            (data, syncType) =>
            {
                Type[] arguments = syncType.type.GetGenericArguments();
                object[] parameters =
                {
                    SyncSerialization.ReadSyncObject(data, arguments[0]),
                    SyncSerialization.ReadSyncObject(data, arguments[1]),
                };
                return syncType.type.GetConstructors().First().Invoke(parameters);
            },
            (data, obj, syncType) =>
            {
                var type = syncType.type;
                Type[] arguments = type.GetGenericArguments();

                SyncSerialization.WriteSyncObject(data, AccessTools.DeclaredField(type, "first").GetValue(obj), arguments[0]);
                SyncSerialization.WriteSyncObject(data, AccessTools.DeclaredField(type, "second").GetValue(obj), arguments[1]);
            }
        );

        // Verse.IExposable serialization
        Multiplayer.serialization.AddSerializationHook(
            syncType => syncType.expose,
            (data, syncType) =>
            {
                if (!typeof(IExposable).IsAssignableFrom(syncType.type))
                    throw new SerializationException($"Type {syncType.type} can't be exposed because it isn't IExposable");

                byte[] exposableData = data.ReadPrefixedBytes();
                return ExposableSerialization.ReadExposable(syncType.type, exposableData);
            },
            (data, obj, syncType) =>
            {
                if (!typeof(IExposable).IsAssignableFrom(syncType.type))
                    throw new SerializationException($"Type {syncType} can't be exposed because it isn't IExposable");

                var log = (data as LoggingByteWriter)?.Log;
                IExposable exposable = obj as IExposable;
                byte[] xmlData = ScribeUtil.WriteExposable(exposable);
                LogXML(log, xmlData);
                data.WritePrefixedBytes(xmlData);
            }
        );

        // Multiplayer.API.ISyncSimple serialization
        // todo null handling for ISyncSimple?
        Multiplayer.serialization.AddSerializationHook(
            syncType => typeof(ISyncSimple).IsAssignableFrom(syncType.type),
            (data, _) =>
            {
                ushort typeIndex = data.ReadUShort();
                var objType = ApiSerialization.syncSimples[typeIndex];
                var obj = MpUtil.NewObjectNoCtor(objType);
                foreach (var field in AccessTools.GetDeclaredFields(objType))
                    field.SetValue(obj, SyncSerialization.ReadSyncObject(data, field.FieldType));
                return obj;
            },
            (data, obj, _) =>
            {
                data.WriteUShort((ushort)ApiSerialization.syncSimples.FindIndex(obj!.GetType()));
                foreach (var field in AccessTools.GetDeclaredFields(obj.GetType()))
                    SyncSerialization.WriteSyncObject(data, field.GetValue(obj), field.FieldType);
            }
        );

        ImplSerialization.Init();
        CompSerialization.Init();
        ApiSerialization.Init();
        DefSerialization.Init();
    }

    internal static T GetAnyParent<T>(Thing thing) where T : class
    {
        if (thing is T t)
            return t;

        for (var parentHolder = thing.ParentHolder; parentHolder != null; parentHolder = parentHolder.ParentHolder)
            if (parentHolder is T t2)
                return t2;

        return null;
    }

    internal static Type[] supportedThingHolders =
    {
        typeof(Map),
        typeof(Thing),
        typeof(ThingComp),
        typeof(WorldObject),
        typeof(WorldObjectComp)
    };

    internal static string ThingHolderString(Thing thing)
    {
        StringBuilder builder = new StringBuilder(thing.ToString());

        for (var parentHolder = thing.ParentHolder; parentHolder != null; parentHolder = parentHolder.ParentHolder)
        {
            builder.Insert(0, "=>");
            builder.Insert(0, parentHolder.ToString());
        }

        return builder.ToString();
    }

    internal static void GetImpl(object obj, IList<Type> impls, out Type type, out int index)
    {
        type = null;
        index = -1;

        if (obj == null) return;

        for (int i = 0; i < impls.Count; i++)
        {
            if (impls[i].IsInstanceOfType(obj))
            {
                type = impls[i];
                index = i;
                break;
            }
        }
    }

    private static void LogXML(SyncLogger log, byte[] xmlData)
    {
        if (log == null) return;

        var reader = XmlReader.Create(new MemoryStream(xmlData));

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                string name = reader.Name;
                if (reader.GetAttribute("IsNull") == "True")
                    name += " (IsNull)";

                if (reader.IsEmptyElement)
                    log.Node(name);
                else
                    log.Enter(name);
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                log.Exit();
            }
            else if (reader.NodeType == XmlNodeType.Text)
            {
                log.AppendToCurrentName($": {reader.Value}");
            }
        }
    }
}
