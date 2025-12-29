using HarmonyLib;
using Verse;

namespace Multiplayer.Client
{
    /// <summary>
    /// Reliable bootstrap arming: Root_Play.Update runs through the whole transition from MapInitializing to Playing.
    /// We use it to arm the map-init (FinalizeInit) trigger as soon as a map exists and ProgramState is Playing.
    /// </summary>
    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Update))]
    static class BootstrapRootPlayUpdatePatch
    {
        // Throttle checks to avoid per-frame overhead.
        private static int nextCheckFrame;
        private const int CheckEveryFrames = 10;

        static void Postfix()
        {
            // Only run while the bootstrap window exists.
            var win = BootstrapConfiguratorWindow.Instance;
            if (win == null)
                return;

            // Throttle.
            if (UnityEngine.Time.frameCount < nextCheckFrame)
                return;
            nextCheckFrame = UnityEngine.Time.frameCount + CheckEveryFrames;

            // Once we're playing and have a map, arm the FinalizeInit-based save flow.
            win.TryArmAwaitingBootstrapMapInit_FromRootPlayUpdate();
        }
    }
}
