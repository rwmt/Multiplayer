using HarmonyLib;
using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client;

public class ThingsById
{
    private Dictionary<int, Thing> thingsById = new();

    public void Register(Thing t) => thingsById[t.thingIDNumber] = t;

    public void Unregister(Thing t) => thingsById.Remove(t.thingIDNumber);

    public void UnregisterAllFrom(Map map) => thingsById.RemoveAll(kv => kv.Value.Map == map);

    public Thing GetValueSafe(int thingIDNumber) => thingsById.GetValueSafe(thingIDNumber);

    public Thing GetValue(int thingIdNumber) => thingsById[thingIdNumber];

    public bool TryGetValue(int thingIDNumber, out Thing thing) => thingsById.TryGetValue(thingIDNumber, out thing);

}
