using HarmonyLib;
using RimWorld;

namespace Multiplayer.Client.Patches
{
    // Dialog_CreateXenogerm doesn't override DoWindowContents
    [HarmonyPatch(typeof(GeneCreationDialogBase), nameof(GeneCreationDialogBase.DoWindowContents))]
    static class GeneAssembler_CloseOnDestroyed
    {
        static bool Prefix(GeneCreationDialogBase __instance)
        {
            // The gene assembler got destroyed (in multiplayer, the gene assembler dialog is not pausing)
            if (__instance is Dialog_CreateXenogerm { geneAssembler: { Spawned: false } })
            {
                __instance.Close();
                return false;
            }

            return true;
        }
    }
}
