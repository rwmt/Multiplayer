using System;
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

    public static MpScope PushFaction(Faction f)
    {
        return Create(() => FactionContext.Push(f), () => FactionContext.Pop());
    }
}
