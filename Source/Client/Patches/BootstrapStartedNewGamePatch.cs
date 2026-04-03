using HarmonyLib;
using Verse;

namespace Multiplayer.Client;

[HarmonyPatch(typeof(GameComponentUtility), nameof(GameComponentUtility.StartedNewGame))]
static class BootstrapStartedNewGamePatch
{
    static void Postfix()
    {
        var window = BootstrapConfiguratorWindow.Instance;
        if (window == null)
            return;

        BootstrapConfiguratorWindow.AwaitingBootstrapMapInit = true;
        OnMainThread.Enqueue(window.OnBootstrapMapInitialized);
    }
}