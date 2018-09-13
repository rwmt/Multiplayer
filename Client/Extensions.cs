using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
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

    }
}
