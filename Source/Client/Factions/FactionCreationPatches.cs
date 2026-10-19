using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Factions;

[HarmonyPatch(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.DoWindowContents))]
static class ConfigureStartingPawns_DoWindowContents_Patch
{
    static void Prefix(ref ProgramState __state)
    {
        __state = Current.ProgramState;
        Current.programStateInt = ProgramState.Entry;
    }

    static void Finalizer(ProgramState __state)
    {
        Current.programStateInt = __state;
    }
}

[HarmonyPatch(typeof(StartingPawnUtility), nameof(StartingPawnUtility.RandomizePawn))]
static class StartingPawnUtility_RandomizePawn_Patch
{
    static void Prefix(ref ProgramState __state)
    {
        // todo is this compatible with 1.5's "Create new wanderers?"
        __state = Current.ProgramState;
        Current.programStateInt = ProgramState.Entry;
    }

    static void Finalizer(ProgramState __state)
    {
        Current.programStateInt = __state;
    }
}

[HarmonyPatch(typeof(LifeStageWorker_HumanlikeAdult), nameof(LifeStageWorker_HumanlikeAdult.Notify_LifeStageStarted))]
static class LifeStageWorker_Patch
{
    static bool Prefix()
    {
        // Corresponds to "Current.ProgramState == ProgramState.Playing" check in Notify_LifeStageStarted
        return !ScribeUtil.loading;
    }
}

// Starting pawns are sent without an ideoligion (FactionCreator.WriteStartingPawn), CreateFaction gives them the new
// faction's. Pawn_IdeoTracker's PostLoadInit would otherwise assign a random fallback one.
[HarmonyPatch(typeof(Pawn), nameof(Pawn.ShouldHaveIdeo), MethodType.Getter)]
static class NoFallbackIdeoForStartingPawnPatch
{
    static bool Prefix(ref bool __result)
    {
        if (!FactionCreator.readingStartingPawn) return true;

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(StartingPawnUtility), nameof(StartingPawnUtility.GetGenerationRequest))]
static class StartingPawnUtility_GetGenerationRequest_Patch
{
    static void Postfix(ref PawnGenerationRequest __result)
    {
        if (Multiplayer.Client != null)
            __result.CanGeneratePawnRelations = false;
    }
}

[HarmonyPatch(typeof(StartingPawnUtility), nameof(StartingPawnUtility.DefaultStartingPawnRequest), MethodType.Getter)]
static class StartingPawnUtility_DefaultStartingPawnRequest_Patch
{
    static void Postfix(ref PawnGenerationRequest __result)
    {
        if (Multiplayer.Client != null)
            __result.CanGeneratePawnRelations = false;
    }
}
