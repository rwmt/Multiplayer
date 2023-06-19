using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client;

public static class ThingsById
{
    public static Dictionary<int, Thing> thingsById = new();

    public static void Register(Thing t)
    {
        thingsById[t.thingIDNumber] = t;
    }

    public static void Unregister(Thing t)
    {
        thingsById.Remove(t.thingIDNumber);
    }

    public static void UnregisterAllFrom(Map map)
    {
        thingsById.RemoveAll(kv => kv.Value.Map == map);
    }
}
