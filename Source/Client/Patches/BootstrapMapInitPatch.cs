using HarmonyLib;
using Verse;

namespace Multiplayer.Client;

[HarmonyPatch(typeof(MapComponentUtility), nameof(MapComponentUtility.FinalizeInit))]
static class BootstrapMapInitPatch
{
    static void Postfix(Map map)
    {
        if (!BootstrapConfiguratorWindow.AwaitingBootstrapMapInit || BootstrapConfiguratorWindow.Instance == null)
            return;

        OnMainThread.Enqueue(() => BootstrapConfiguratorWindow.Instance.OnBootstrapMapInitialized());
    }
}