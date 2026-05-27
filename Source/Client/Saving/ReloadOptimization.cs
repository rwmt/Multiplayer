using System;
using Verse;

namespace Multiplayer.Client
{
    public enum ReloadOptimizationMode
    {
        None,
        ForJoinPointSnapshot,
    }

    internal static class ReloadOptimization
    {
        public static ReloadOptimizationPlan PlanFor(ReloadOptimizationMode mode)
        {
            return mode switch
            {
                ReloadOptimizationMode.ForJoinPointSnapshot => new(
                    RegenerateMapDrawersWhenRestoringFaction: false,
                    RegenerateMapDrawersAfterSnapshot: true
                ),
                _ => new(
                    RegenerateMapDrawersWhenRestoringFaction: true,
                    RegenerateMapDrawersAfterSnapshot: false
                ),
            };
        }

        public static void Complete(ReloadOptimizationMode mode)
        {
            Complete(mode, RegenerateMapDrawers);
        }

        internal static void Complete(ReloadOptimizationMode mode, Action regenerateMapDrawers)
        {
            if (!PlanFor(mode).RegenerateMapDrawersAfterSnapshot)
                return;

            regenerateMapDrawers();
        }

        private static void RegenerateMapDrawers()
        {
            foreach (var map in Find.Maps)
                map.mapDrawer.RegenerateEverythingNow();
        }
    }

    internal readonly record struct ReloadOptimizationPlan(
        bool RegenerateMapDrawersWhenRestoringFaction,
        bool RegenerateMapDrawersAfterSnapshot
    );
}
