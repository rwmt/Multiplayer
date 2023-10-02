using System;
using Verse;

namespace Multiplayer.Client;

public class ExposeActor : IExposable
{
    private Action action;

    private ExposeActor(Action action)
    {
        this.action = action;
    }

    public void ExposeData()
    {
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            action();
    }

    // This depends on the fact that the implementation of HashSet RimWorld currently uses
    // "preserves" insertion order (as long as elements are only added and not removed
    // [which is the case for Scribe managers])
    public static void Register(Action action)
    {
        if (Scribe.mode == LoadSaveMode.LoadingVars)
            Scribe.loader.initer.RegisterForPostLoadInit(new ExposeActor(action));
    }
}
