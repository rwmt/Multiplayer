using HarmonyLib;
using Verse;

namespace Multiplayer.Client;

[HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Update))]
static class BootstrapRootPlayUpdatePatch
{
    private static int nextCheckFrame;
    private const int CheckEveryFrames = 10;

    static void Postfix()
    {
        var window = BootstrapConfiguratorWindow.Instance;
        if (window == null)
            return;

        if (UnityEngine.Time.frameCount < nextCheckFrame)
            return;

        nextCheckFrame = UnityEngine.Time.frameCount + CheckEveryFrames;
        window.TryArmAwaitingBootstrapMapInit_FromRootPlayUpdate();
    }
}