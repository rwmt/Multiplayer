using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Multiplayer.Client;

public class SharedCrossRefs : LoadedObjectDirectory
{
    // Used in CrossRefs patches
    public HashSet<string> tempKeys = new();

    // Mod-owned calls must be idempotent even when the base method's Harmony
    // prefix isn't reached. Loading and spawning can register the same object.
    public new void RegisterLoaded(ILoadReferenceable reffable)
    {
        if (reffable == null) return;

        var key = reffable.GetUniqueLoadID();
        if (allObjectsByLoadID.ContainsKey(key)) return;

        if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
            tempKeys.Add(key);

        allObjectsByLoadID.Add(key, reffable);
    }

    public void Unregister(ILoadReferenceable reffable)
    {
        allObjectsByLoadID.Remove(reffable.GetUniqueLoadID());
    }

    public void UnregisterAllTemp()
    {
        foreach (var key in tempKeys)
            allObjectsByLoadID.Remove(key);

        tempKeys.Clear();
    }

    public List<ILoadReferenceable> UnregisterAllFrom(Map map)
    {
        var unregistered = new List<ILoadReferenceable>();

        foreach (var val in allObjectsByLoadID.Values.ToArray())
        {
            if (val is Thing thing && thing.Map == map ||
                val is PassingShip ship && ship.Map == map ||
                val is Bill bill && bill.Map == map
               )
            {
                Unregister(val);
                unregistered.Add(val);
            }
        }

        return unregistered;
    }

    public void Reregister(List<ILoadReferenceable> items)
    {
        foreach (var item in items)
            allObjectsByLoadID[item.GetUniqueLoadID()] = item;
    }
}
