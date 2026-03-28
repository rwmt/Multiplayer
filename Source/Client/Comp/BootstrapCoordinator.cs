using Verse;

namespace Multiplayer.Client.Comp;

public class BootstrapCoordinator : GameComponent
{
    private int nextCheckTick;
    private const int CheckIntervalTicks = 15;

    public BootstrapCoordinator(Game game)
    {
    }

    public override void GameComponentTick()
    {
        base.GameComponentTick();

        var window = BootstrapConfiguratorWindow.Instance;
        if (window == null)
            return;

        if (Find.TickManager != null && Find.TickManager.TicksGame < nextCheckTick)
            return;

        if (Find.TickManager != null)
            nextCheckTick = Find.TickManager.TicksGame + CheckIntervalTicks;

        window.BootstrapCoordinatorTick();
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref nextCheckTick, "mp_bootstrap_nextCheckTick", 0);
    }
}