using System;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client.Saving
{
    // Session data is the middle ground between no persistence and full persistence:
    // - Non-persistent data:
    //      Mainly data in caches
    //      Reset/removed during reloading (f.e. when creating a join point)
    // - Session data:
    //      Things like ritual sessions and per player god mode status
    //      Serialized into binary using the Sync system
    //      Session-bound: survives a reload, lost when the server is closed
    // - Persistent data:
    //      Serialized into XML using RimWorld's Scribe system
    //      Save-bound: survives a server restart
    public static class SessionData
    {
        public static byte[] WriteSessionData()
        {
            var writer = new ByteWriter();

            try
            {
                var gameWriter = new ByteWriter();
                Multiplayer.GameComp.WriteSessionData(gameWriter);
                writer.WritePrefixedBytes(gameWriter.ToArray());
            }
            catch (Exception e)
            {
                Log.Error($"Exception writing session data for game: {e}");
            }

            try
            {
                var worldWriter = new ByteWriter();
                Multiplayer.WorldComp.WriteSemiPersistent(worldWriter);
                writer.WritePrefixedBytes(worldWriter.ToArray());
            }
            catch (Exception e)
            {
                Log.Error($"Exception writing semi-persistent data for world: {e}");
            }

            writer.WriteInt32(Find.Maps.Count);
            foreach (var map in Find.Maps)
            {
                try
                {
                    var mapWriter = new ByteWriter();
                    map.MpComp().WriteSessionData(mapWriter);

                    writer.WriteInt32(map.uniqueID);
                    writer.WritePrefixedBytes(mapWriter.ToArray());
                }
                catch (Exception e)
                {
                    Log.Error($"Exception writing session data for map {map}: {e}");
                }
            }

            return writer.ToArray();
        }

        public static void ReadSessionData(byte[] data)
        {
            if (data.Length == 0) return;

            var reader = new ByteReader(data);
            var gameData = reader.ReadPrefixedBytes();

            try
            {
                Multiplayer.GameComp.ReadSessionData(new ByteReader(gameData));
            }
            catch (Exception e)
            {
                Log.Error($"Exception reading session data for game: {e}");
            }

            var worldData = reader.ReadPrefixedBytes();

            try
            {
                Multiplayer.WorldComp.ReadSemiPersistent(new ByteReader(worldData));
            }
            catch (Exception e)
            {
                Log.Error($"Exception reading semi-persistent data for world: {e}");
            }

            var mapCount = reader.ReadInt32();
            for (int i = 0; i < mapCount; i++)
            {
                var mapId = reader.ReadInt32();
                var mapData = reader.ReadPrefixedBytes();
                var map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);

                if (map == null)
                {
                    Log.Warning($"Multiplayer: Couldn't find map with id {mapId} while reading session data.");
                    continue;
                }

                try
                {
                    var mapReader = new ByteReader(mapData);
                    mapReader.MpContext().map = map;
                    map.MpComp().ReadSessionData(mapReader);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception reading session data for map {map}: {e}");
                }
            }
        }
    }
}
