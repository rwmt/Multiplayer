using RimWorld;
using Verse;

namespace Multiplayer.Client;

// Per-faction storage for RimWorld managers
public class FactionMapData : IExposable
{
    public Map map;
    public int factionId;

    // Saved
    public DesignationManager designationManager;
    public AreaManager areaManager;
    public ZoneManager zoneManager;

    // Not saved
    public HaulDestinationManager haulDestinationManager;
    public ListerHaulables listerHaulables;
    public ResourceCounter resourceCounter;
    public ListerFilthInHomeArea listerFilthInHomeArea;
    public ListerMergeables listerMergeables;

    private FactionMapData() { }

    // Loading ctor
    public FactionMapData(Map map)
    {
        this.map = map;

        haulDestinationManager = new HaulDestinationManager(map);
        listerHaulables = new ListerHaulables(map);
        resourceCounter = new ResourceCounter(map);
        listerFilthInHomeArea = new ListerFilthInHomeArea(map);
        listerMergeables = new ListerMergeables(map);
    }

    private FactionMapData(int factionId, Map map) : this(map)
    {
        this.factionId = factionId;

        designationManager = new DesignationManager(map);
        areaManager = new AreaManager(map);
        zoneManager = new ZoneManager(map);
    }

    public void ExposeData()
    {
        ExposeActor.Register(() => map.PushFaction(factionId));

        Scribe_Values.Look(ref factionId, "factionId");
        Scribe_Deep.Look(ref designationManager, "designationManager", map);
        Scribe_Deep.Look(ref areaManager, "areaManager", map);
        Scribe_Deep.Look(ref zoneManager, "zoneManager", map);

        ExposeActor.Register(() => map.PopFaction());
    }

    public static FactionMapData New(int factionId, Map map)
    {
        return new FactionMapData(factionId, map);
    }

    public static FactionMapData NewFromMap(Map map, int factionId)
    {
        return new FactionMapData(map)
        {
            factionId = factionId,

            designationManager = map.designationManager,
            areaManager = map.areaManager,
            zoneManager = map.zoneManager,

            haulDestinationManager = map.haulDestinationManager,
            listerHaulables = map.listerHaulables,
            resourceCounter = map.resourceCounter,
            listerFilthInHomeArea = map.listerFilthInHomeArea,
            listerMergeables = map.listerMergeables,
        };
    }
}
