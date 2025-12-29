using HarmonyLib;
using Verse;

namespace Multiplayer.Client
{
    /// <summary>
    /// Robust bootstrap trigger: Root_Play.Start is called when the game fully transitions into Playing.
    /// This is a better signal than ExecuteWhenFinished which can run before ProgramState switches.
    /// </summary>
    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Start))]
    static class BootstrapRootPlayPatch
    {
        static void Postfix()
        {
            var inst = BootstrapConfiguratorWindow.Instance;
            if (inst == null)
                return;

            // If bootstrap flow is active, this is the perfect time to arm map init.
            inst.TryArmAwaitingBootstrapMapInit_FromRootPlay();
        }
    }
}
