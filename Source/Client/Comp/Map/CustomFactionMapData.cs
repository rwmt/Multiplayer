using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client;

public class CustomFactionMapData : IExposable
{
    public Map map;
    public int factionId;

    public HashSet<Thing> claimed = new();
    public HashSet<Thing> unforbidden = new();

    // Loading ctor
    public CustomFactionMapData(Map map)
    {
        this.map = map;
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref factionId, "factionId");
        Scribe_Collections.Look(ref unforbidden, "unforbidden", LookMode.Reference);
    }

    public static CustomFactionMapData New(int factionId, Map map)
    {
        return new CustomFactionMapData(map) { factionId = factionId };
    }
}
