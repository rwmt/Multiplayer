using System;
using Multiplayer.Client.Factions;
using RimWorld;

namespace Multiplayer.Client.Util;

public class MpScope : IDisposable
{
    private Action end;

    public void Dispose()
    {
        end();
    }

    public static MpScope Create(Action begin, Action end)
    {
        begin();
        return new MpScope { end = end  };
    }

    // Applies the faction to Faction.OfPlayer, the world data and every map (like a tick does), not only Faction.OfPlayer
    public static MpScope PushFaction(Faction f)
    {
        return Create(() => FactionExtensions.PushFaction(null, f, force: true), () => FactionExtensions.PopFaction());
    }

    // For simulation code running outside of ticks and commands (e.g. long events queued by commands), where the
    // current faction data is the local player's. Ends by re-applying the local player's faction everywhere.
    public static MpScope PushFactionOutsideContext(Faction f)
    {
        return Create(() => FactionExtensions.PushFaction(null, f, force: true), FactionExtensions.PopToLocalFaction);
    }
}
