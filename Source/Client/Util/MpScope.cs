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
}
