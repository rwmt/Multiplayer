using Multiplayer.API;
using Multiplayer.Common;

namespace Multiplayer.Client;

public static class SyncSerialization
{
    public static T ReadSync<T>(ByteReader data)
    {
        return Multiplayer.serialization.ReadSync<T>(data);
    }

    public static object ReadSyncObject(ByteReader data, SyncType syncType)
    {
        return Multiplayer.serialization.ReadSyncObject(data, syncType);
    }

    public static void WriteSync<T>(ByteWriter data, T obj)
    {
        Multiplayer.serialization.WriteSync(data, obj);
    }

    public static void WriteSyncObject(ByteWriter data, object obj, SyncType syncType)
    {
        Multiplayer.serialization.WriteSyncObject(data, obj, syncType);
    }
}
