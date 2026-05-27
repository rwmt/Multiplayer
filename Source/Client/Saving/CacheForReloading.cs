using System;
using Verse;

namespace Multiplayer.Client
{
    public enum ReloadOptimizationMode
    {
        None,
        DeferFactionMapDrawerRebuildForSnapshot,
    }

    public static class CacheForReloading
    {
        public static ReloadCacheScope Begin(bool enabled)
        {
            return Begin(enabled ? ReloadOptimizationMode.DeferFactionMapDrawerRebuildForSnapshot : ReloadOptimizationMode.None);
        }

        public static ReloadCacheScope Begin(ReloadOptimizationMode mode)
        {
            return new ReloadCacheScope(mode);
        }

        internal static bool ShouldDeferFactionMapDrawerRebuild(ReloadOptimizationMode mode)
        {
            return mode == ReloadOptimizationMode.DeferFactionMapDrawerRebuildForSnapshot;
        }

        internal static void Complete(ReloadOptimizationMode mode)
        {
            if (ShouldDeferFactionMapDrawerRebuild(mode))
                RegenerateMapDrawers();
        }

        internal static void RegenerateMapDrawers()
        {
            foreach (var map in Find.Maps)
                map.mapDrawer.RegenerateEverythingNow();
        }

        public readonly struct ReloadCacheScope : IDisposable
        {
            internal ReloadCacheScope(ReloadOptimizationMode mode)
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
