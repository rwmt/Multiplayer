using System;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client.Saving
{
    // Semi-persistence is the middle ground between lack of persistence and full persistence:
    // - Non-persistent data:
    //      Mainly data in caches
    //      Reset/removed during reloading (f.e. when creating a join point)
    // - Semi-persistent data:
    //      Things like ritual sessions and per player god mode status
    //      Serialized into binary using the Sync system
    //      Session-bound: survives a reload, lost when the server is closed
    // - Persistent data:
    //      Serialized into XML using RimWorld's Scribe system
    //      Save-bound: survives a server restart
    public static class SemiPersistent
    {
        public static byte[] WriteSemiPersistent()
        {
            var writer = new ByteWriter();

            try
            {
                var gameWriter = new ByteWriter();
                Multiplayer.GameComp.WriteSemiPersistent(gameWriter);
                writer.WritePrefixedBytes(gameWriter.ToArray());
            }
            catch (Exception e)
            {
                Log.Error($"Exception writing semi-persistent data for game: {e}");
            }

            writer.WriteInt32(Find.Maps.Count);
            foreach (var map in Find.Maps)
            {
                try
                {
                    var mapWriter = new ByteWriter();
                    map.MpComp().WriteSemiPersistent(mapWriter);

                    writer.WriteInt32(map.uniqueID);
                    writer.WritePrefixedBytes(mapWriter.ToArray());
                }
                catch (Exception e)
                {
                    Log.Error($"Exception writing semi-persistent data for map {map}: {e}");
                }
            }

            return writer.ToArray();
        }

        public static void ReadSemiPersistent(byte[] data)
        {
            if (data.Length == 0) return;

            var reader = new ByteReader(data);
            var gameData = reader.ReadPrefixedBytes();

            try
            {
                Multiplayer.GameComp.ReadSemiPersistent(new ByteReader(gameData));
            }
            catch (Exception e)
            {
                Log.Error($"Exception reading semi-persistent data for game: {e}");
            }

            var mapCount = reader.ReadInt32();
            for (int i = 0; i < mapCount; i++)
            {
                var mapId = reader.ReadInt32();
                var mapData = reader.ReadPrefixedBytes();
                var map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);

                if (map == null)
                {
                    Log.Warning($"Multiplayer: Couldn't find map with id {mapId} while reading semi-persistent data.");
                    continue;
                }

                try
                {
                    var mapReader = new ByteReader(mapData);
                    mapReader.MpContext().map = map;
                    map.MpComp().ReadSemiPersistent(mapReader);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception reading semi-persistent data for map {map}: {e}");
                }
            }
        }
    }
}
