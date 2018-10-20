extern alias zip;

using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;
using zip::Ionic.Zip;

namespace Multiplayer.Client
{
    public static class Extensions
    {
        public static IEnumerable<Type> AllSubtypesAndSelf(this Type t)
        {
            return t.AllSubclasses().Concat(t);
        }

        public static IEnumerable<Type> AllImplementing(this Type t)
        {
            return from x in GenTypes.AllTypes where t.IsAssignableFrom(x) select x;
        }

        // Sets the current Faction.OfPlayer
        // Applies faction's world components
        // Applies faction's map components if map not null
        public static void PushFaction(this Map map, Faction faction)
        {
            faction = FactionContext.Push(faction);
            if (faction == null) return;

            Multiplayer.WorldComp?.SetFaction(faction);
            map?.GetComponent<MultiplayerMapComp>().SetFaction(faction);
        }

        public static void PushFaction(this Map map, int factionId)
        {
            Faction faction = Find.FactionManager.GetById(factionId);
            map.PushFaction(faction);
        }

        public static void PopFaction()
        {
            PopFaction(null);
        }

        public static void PopFaction(this Container<Map> c)
        {
            PopFaction(c.Value);
        }

        public static void PopFaction(this Map map)
        {
            Faction faction = FactionContext.Pop();
            if (faction == null) return;

            Multiplayer.WorldComp?.SetFaction(faction);
            map?.GetComponent<MultiplayerMapComp>().SetFaction(faction);
        }

        public static Map GetMap(this ScheduledCommand cmd)
        {
            if (cmd.mapId == ScheduledCommand.Global) return null;
            return Find.Maps.Find(map => map.uniqueID == cmd.mapId);
        }

        public static Faction GetFaction(this ScheduledCommand cmd)
        {
            if (cmd.factionId == ScheduledCommand.NoFaction) return null;
            return Find.FactionManager.GetById(cmd.factionId);
        }

        public static void RemoveAll<K, V>(this Dictionary<K, V> dict, Func<K, V, bool> predicate)
        {
            dict.RemoveAll(p => predicate(p.Key, p.Value));
        }

        public static MapAsyncTimeComp AsyncTime(this Map map)
        {
            return map.GetComponent<MapAsyncTimeComp>();
        }

        public static MultiplayerMapComp MpComp(this Map map)
        {
            return map.GetComponent<MultiplayerMapComp>();
        }

        public static T ThingReplacement<T>(this Map map, T thing) where T : Thing
        {
            foreach (Thing t in map.thingGrid.ThingsListAtFast(thing.positionInt))
                if (t.thingIDNumber == thing.thingIDNumber)
                    return (T)t;

            return null;
        }

        public static void Insert<T>(this List<T> list, int index, params T[] items)
        {
            list.InsertRange(index, items);
        }

        public static Rect Down(this Rect rect, float y)
        {
            rect.y += y;
            return rect;
        }

        public static Rect Up(this Rect rect, float y)
        {
            rect.y -= y;
            return rect;
        }

        public static Rect Right(this Rect rect, float x)
        {
            rect.x += x;
            return rect;
        }

        public static Faction GetById(this FactionManager manager, int factionId)
        {
            return manager.AllFactionsListForReading.Find(f => f.loadID == factionId);
        }

        public static void SendCommand(this IConnection conn, CommandType type, int mapId, byte[] data)
        {
            ByteWriter writer = new ByteWriter();
            writer.WriteInt32(Convert.ToInt32(type));
            writer.WriteInt32(mapId);
            writer.WritePrefixedBytes(data);

            conn.Send(Packets.Client_Command, writer.GetArray());
        }

        public static void SendCommand(this IConnection conn, CommandType type, int mapId, params object[] data)
        {
            SendCommand(conn, type, mapId, ByteWriter.GetBytes(data));
        }

        public static MpContext MpContext(this ByteReader data)
        {
            if (data.context == null)
                data.context = new MpContext();
            return data.context as MpContext;
        }

        public static MpContext MpContext(this ByteWriter data)
        {
            if (data.context == null)
                data.context = new MpContext();
            return data.context as MpContext;
        }

        public static int Hash(this StackTrace trace)
        {
            int hash = 0;
            foreach (StackFrame frame in trace.GetFrames())
                hash = Gen.HashCombineInt(hash, frame.GetMethod().MetadataToken);

            return hash;
        }

        public static byte[] GetBytes(this ZipEntry entry)
        {
            MemoryStream stream = new MemoryStream();
            entry.Extract(stream);
            return stream.ToArray();
        }

        public static bool IsCompilerGenerated(this Type type)
        {
            while (type != null)
            {
                if (type.HasAttribute<CompilerGeneratedAttribute>()) return true;
                type = type.DeclaringType;
            }

            return false;
        }

        public static void RemoveNulls(this IList list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                if (list[i] == null)
                    list.RemoveAt(i);
            }
        }

    }

}
