using Verse;

namespace Multiplayer.Client
{
    public enum ReloadOptimizationMode
    {
        None,
        DeferFactionMapDrawerRebuildForSnapshot,
    }

    internal static class ReloadOptimization
    {
        public static bool ShouldDeferFactionMapDrawerRebuild(ReloadOptimizationMode mode)
        {
            return mode == ReloadOptimizationMode.DeferFactionMapDrawerRebuildForSnapshot;
        }

        public static void Complete(ReloadOptimizationMode mode)
        {
            if (!ShouldDeferFactionMapDrawerRebuild(mode))
                return;

            foreach (var map in Find.Maps)
                map.mapDrawer.RegenerateEverythingNow();
        }
    }
}
