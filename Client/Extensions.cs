using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Verse;

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

        // sets the current Faction.OfPlayer
        // applies faction's map components if map not null
        public static void PushFaction(this Map map, Faction faction)
        {
            faction = FactionContext.Push(faction);
            if (faction == null) return;

            Multiplayer.WorldComp?.SetFaction(faction);
            map?.GetComponent<MultiplayerMapComp>().SetFaction(faction);
        }

        public static void PushFaction(this Map map, int factionId)
        {
            Faction faction = Find.FactionManager.AllFactionsListForReading.Find(f => f.loadID == factionId);
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
            return Find.FactionManager.AllFactionsListForReading.Find(f => f.loadID == cmd.factionId);
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

        public static MpContext MpContext(this ByteReader data) => (MpContext)(data.context ?? (data.context = new MpContext()));
        public static MpContext MpContext(this ByteWriter data) => (MpContext)(data.context ?? (data.context = new MpContext()));

        public static Map ContextMap(this ByteReader data) => data.MpContext().map;
        public static Map ContextMap(this ByteWriter data) => data.MpContext().map;
        public static void ContextMap(this ByteReader data, Map val) => data.MpContext().map = val;
        public static void ContextMap(this ByteWriter data, Map val) => data.MpContext().map = val;

        public static Faction ContextFaction(this ByteReader data) => data.MpContext().faction;
        public static Faction ContextFaction(this ByteWriter data) => data.MpContext().faction;
        public static void ContextFaction(this ByteReader data, Faction val) => data.MpContext().faction = val;
        public static void ContextFaction(this ByteWriter data, Faction val) => data.MpContext().faction = val;

    }

}
