using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client
{

    public class ClientSyncOpinion
    {
        public bool isLocalClientsOpinion;

        public int startTick;
        public List<uint> commandRandomStates = new();
        public List<uint> worldRandomStates = new();
        public List<MapRandomStateData> mapStates = new();

        public List<StackTraceLogItem> desyncStackTraces = new();
        public List<int> desyncStackTraceHashes = new();
        public bool simulating;

        public ClientSyncOpinion(int startTick)
        {
            this.startTick = startTick;
        }

        public string CheckForDesync(ClientSyncOpinion other)
        {
            // if (!mapStates.Select(m => m.mapId).SequenceEqual(other.mapStates.Select(m => m.mapId)))
            //     return "Map instances don't match";

            foreach (var g in
                     from map1 in mapStates
                     join map2 in other.mapStates on map1.mapId equals map2.mapId
                     select (map1, map2))
            {
                if (!g.map1.randomStates.SequenceEqual(g.map2.randomStates))
                    return $"Wrong random state on map {g.map1.mapId}";
            }

            if (!worldRandomStates.SequenceEqual(other.worldRandomStates))
                return "Wrong random state for the world";

            if (!commandRandomStates.SequenceEqual(other.commandRandomStates))
                return "Random state from commands doesn't match";

            if (!simulating && !other.simulating && desyncStackTraceHashes.Any() && other.desyncStackTraceHashes.Any() && !desyncStackTraceHashes.SequenceEqual(other.desyncStackTraceHashes))
                return "Trace hashes don't match";

            return null;
        }

        public List<uint> GetRandomStatesForMap(int mapId)
        {
            var result = mapStates.Find(m => m.mapId == mapId);
            if (result != null) return result.randomStates;
            mapStates.Add(result = new MapRandomStateData(mapId));
            return result.randomStates;
        }

        public byte[] Serialize()
        {
            var writer = new ByteWriter();

            writer.WriteInt32(startTick);
            writer.WritePrefixedUInts(commandRandomStates);
            writer.WritePrefixedUInts(worldRandomStates);

            writer.WriteInt32(mapStates.Count);
            foreach (var map in mapStates)
            {
                writer.WriteInt32(map.mapId);
                writer.WritePrefixedUInts(map.randomStates);
            }

            writer.WritePrefixedInts(desyncStackTraceHashes);
            writer.WriteBool(simulating);

            return writer.ToArray();
        }

        public static ClientSyncOpinion Deserialize(ByteReader data)
        {
            var startTick = data.ReadInt32();

            var cmds = new List<uint>(data.ReadPrefixedUInts());
            var world = new List<uint>(data.ReadPrefixedUInts());

            var maps = new List<MapRandomStateData>();
            int mapCount = data.ReadInt32();
            for (int i = 0; i < mapCount; i++)
            {
                int mapId = data.ReadInt32();
                var mapData = new List<uint>(data.ReadPrefixedUInts());
                maps.Add(new MapRandomStateData(mapId) { randomStates = mapData });
            }

            var traceHashes = new List<int>(data.ReadPrefixedInts());
            var playing = data.ReadBool();

            return new ClientSyncOpinion(startTick)
            {
                commandRandomStates = cmds,
                worldRandomStates = world,
                mapStates = maps,
                desyncStackTraceHashes = traceHashes,
                simulating = playing
            };
        }

        public void TryMarkSimulating()
        {
            if (TickPatch.Simulating)
                simulating = true;
        }

        public string GetFormattedStackTracesForRange(int diffAt)
        {
            var start = Math.Max(0, diffAt - Multiplayer.settings.desyncTracesRadius);
            var end = diffAt + Multiplayer.settings.desyncTracesRadius;
            var traceId = start;

            return
                $"Trace count: {desyncStackTraces.Count}\nTrace of first desynced map random state:\n{diffAt} {desyncStackTraces.ElementAtOrDefault(diffAt)}" +
                "\n\nContext traces:\n" +
                desyncStackTraces
                .Skip(start)
                .Take(end - start)
                .Join(a => traceId++ + " " + a, "\n\n");
        }

        public void Clear()
        {
            for (int i = 0; i < desyncStackTraces.Count; i++)
                desyncStackTraces[i].ReturnToPool();
        }
    }
}
