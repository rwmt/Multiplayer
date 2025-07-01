using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Patches;

[HarmonyPatch(typeof(PawnColumnWorker_Designator))]
[HarmonyPatch(nameof(PawnColumnWorker_Designator.DesignationConfirmed))]
public static class PreventPawnTableDesignationErrors
{
    static bool Prefix(PawnColumnWorker_Designator __instance, Pawn pawn)
    {
        // Cancel execution if designation already exists to prevent
        // errors about trying to double-add designations.
        return !__instance.GetValue(pawn);
    }
}

[HarmonyPatch(typeof(PawnColumnWorker_Sterilize), nameof(PawnColumnWorker_Sterilize.AddSterilizeOperation))]
static class PreventPawnTableMultipleSterilizeOperations
{
    static bool Prefix(PawnColumnWorker_Sterilize __instance, Pawn animal)
    {
        // Cancel execution if any operations exist to prevent
        // queueing up multiple sterilize operations.
        return !__instance.SterilizeOperations(animal).Any();
    }
}
