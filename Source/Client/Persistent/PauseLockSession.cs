using Multiplayer.Client.Experimental;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Persistent;

// Used for pause locks. Pause locks should become obsolete and this should become unused,
// but pause locks are kept for backwards compatibility. WIP.
public class PauseLockSession : Session
{
    public override Map Map => null;

    public override bool IsCurrentlyPausing(Map map)
    {
        throw new System.NotImplementedException();
    }

    public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry)
    {
        return null;
    }
}
