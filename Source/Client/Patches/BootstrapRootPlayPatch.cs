using HarmonyLib;
using Verse;

namespace Multiplayer.Client;

[HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Start))]
static class BootstrapRootPlayPatch
{
    static void Postfix()
    {
        BootstrapConfiguratorWindow.Instance?.TryArmAwaitingBootstrapMapInit_FromRootPlay();
    }
}