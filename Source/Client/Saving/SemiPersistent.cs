using System;
using System.Linq;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client.Saving
{
    public static class SemiPersistent
    {
        public static byte[] WriteSemiPersistent()
        {
            var writer = new ByteWriter();

            writer.WriteInt32(Find.Maps.Count);
            foreach (var map in Find.Maps)
            {
                writer.WriteInt32(map.uniqueID);
                var mapWriter = new ByteWriter();

                try
                {
                    map.MpComp().WriteSemiPersistent(mapWriter);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception writing semi-persistent data for map {map}: {e}");
                }

                writer.WritePrefixedBytes(mapWriter.ToArray());
            }

            return writer.ToArray();
        }

        public static void ReadSemiPersistent(byte[] data)
        {
            if (data.Length == 0) return;

            var reader = new ByteReader(data);

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
                    map.MpComp().ReadSemiPersistent(new ByteReader(mapData));
                }
                catch (Exception e)
                {
                    Log.Error($"Exception reading semi-persistent data for map {map}: {e}");
                }
            }
        }
    }
}
