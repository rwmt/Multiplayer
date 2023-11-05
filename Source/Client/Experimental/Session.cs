using Multiplayer.Client.Persistent;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Experimental;

// Abstract class for ease of updating API - making sure that adding more methods or properties to the
// interface won't cause issues with other mods by implementing them here as virtual methods.
public abstract class Session : ISession
{
    // Use internal to prevent mods from easily modifying it?
    protected int sessionId;
    // Should it be virtual?
    public int SessionId
    {
        get => sessionId;
        set => sessionId = value;
    }

    public virtual bool IsSessionValid => true;

    // For subclasses implementing IExplosableSession
    public virtual void ExposeData()
    {
        Scribe_Values.Look(ref sessionId, "sessionId");
    }

    public virtual void PostAddSession()
    {
    }

    public virtual void PostRemoveSession()
    {
    }

    protected static void SwitchToMapOrWorld(Map map)
    {
        if (map == null)
        {
            Find.World.renderer.wantedMode = WorldRenderMode.Planet;
        }
        else
        {
            if (WorldRendererUtility.WorldRenderedNow) CameraJumper.TryHideWorld();
            Current.Game.CurrentMap = map;
        }
    }

    public abstract Map Map { get; }
    public abstract bool IsCurrentlyPausing(Map map);
    public abstract FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry);
}
