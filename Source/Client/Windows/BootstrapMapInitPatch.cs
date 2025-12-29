using HarmonyLib;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(MapComponentUtility), nameof(MapComponentUtility.FinalizeInit))]
    static class BootstrapMapInitPatch
    {
        static void Postfix(Map map)
        {
            // Check if we're waiting for bootstrap map initialization
            if (BootstrapConfiguratorWindow.AwaitingBootstrapMapInit && 
                BootstrapConfiguratorWindow.Instance != null)
            {
                // Trigger save sequence on main thread
                OnMainThread.Enqueue(() =>
                {
                    BootstrapConfiguratorWindow.Instance.OnBootstrapMapInitialized();
                });
            }
        }
    }
}
