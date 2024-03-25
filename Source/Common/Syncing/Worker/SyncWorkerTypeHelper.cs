using System;

namespace Multiplayer.Client;

public static class SyncWorkerTypeHelper
{
    public new static Func<ushort, Type, Type> GetType = (_, _) => throw new Exception("No implementation");
    public static Func<Type, Type, ushort> GetTypeIndex = (_, _) => throw new Exception("No implementation");
}
