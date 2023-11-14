using System.Collections.Generic;
using Multiplayer.API;
using Multiplayer.Client.Experimental;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Persistent;

// Used for pause locks. Pause locks should become obsolete and this should become unused,
// but pause locks are kept for backwards compatibility.
public class PauseLockSession : Session, ISessionWithCreationRestrictions
{
    public static List<PauseLockDelegate> pauseLocks = new();

    public override Map Map => null;

    public PauseLockSession(Map _) : base(null) { }

    public override bool IsCurrentlyPausing(Map map) => pauseLocks.Any(x => x(map));

    // Should we add some message explaining pause locks/having a list of pausing ones?
    public override FloatMenuOption GetBlockingWindowOptions(ColonistBar.Entry entry) => null;

    public bool CanExistWith(Session other) => other is not PauseLockSession;
}
