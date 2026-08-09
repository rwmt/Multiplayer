using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Multiplayer.Client.Patches;

// Shared ideos keep "one church shares its state" semantics; these patches
// fix only the cross-faction pitfalls: reformation applies only for the
// ideo's primary follower faction, and ritual repeat-penalty windows are
// per (precept, faction) so one faction's ritual doesn't burn another's
// 20-day quality window. The shared field keeps vanilla non-MP behavior.

static class RitualPairs
{
    public static long Key(Precept precept, Faction faction) =>
        ((long)precept.Id << 32) | (uint)faction.loadID;

    public static bool Active =>
        Multiplayer.Client != null && Multiplayer.GameComp.multifaction;

    // Finish tick for the context faction's pair, or -1 (never performed)
    public static int PairFinishTick(Precept_Ritual ritual)
    {
        var faction = Faction.OfPlayer;
        if (faction == null)
            return -1;
        return Multiplayer.WorldComp.ritualLastFinished
            .TryGetValue(Key(ritual, faction), out var tick) ? tick : -1;
    }
}

[HarmonyPatch(typeof(IdeoDevelopmentUtility), nameof(IdeoDevelopmentUtility.ApplyChangesToIdeo))]
static class SharedIdeoReformationGate
{
    static bool Prefix(Ideo ideo)
    {
        if (!RitualPairs.Active)
            return true;

        var owner = IdeoContextUtil.PrimaryPlayerFollower(ideo);
        if (owner == null || Faction.OfPlayer == owner)
            return true;

        Log.Message($"MP: reform of shared ideo '{ideo?.name}' skipped - only its primary follower faction may reform it");
        return false;
    }
}

[HarmonyPatch(typeof(LordJob_Ritual), nameof(LordJob_Ritual.ApplyOutcome))]
static class RitualFinishRecordsPair
{
    static void Postfix(LordJob_Ritual __instance)
    {
        if (!RitualPairs.Active)
            return;

        var ritual = __instance.Ritual;
        // The shared field is stamped with the current tick only on the
        // success path - that stamp is the signal this run finished
        if (ritual == null || ritual.lastFinishedTick != GenTicks.TicksGame)
            return;

        if (__instance.lord?.faction is not { IsPlayer: true } faction)
            return;

        Multiplayer.WorldComp.ritualLastFinished[RitualPairs.Key(ritual, faction)] = GenTicks.TicksGame;
    }
}

[HarmonyPatch(typeof(Precept_Ritual), nameof(Precept_Ritual.TicksSinceLastPerformed), MethodType.Getter)]
static class TicksSinceLastPerformedPerFaction
{
    static bool Prefix(Precept_Ritual __instance, ref int __result)
    {
        if (!RitualPairs.Active)
            return true;

        __result = GenTicks.TicksGame - RitualPairs.PairFinishTick(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(Precept_Ritual), nameof(Precept_Ritual.RepeatPenaltyActive), MethodType.Getter)]
static class RepeatPenaltyActivePerFaction
{
    static bool Prefix(Precept_Ritual __instance, ref bool __result)
    {
        if (!RitualPairs.Active)
            return true;

        // Vanilla body against the pair's finish tick
        __result = __instance.isAnytime && __instance.def.useRepeatPenalty &&
                   RitualPairs.PairFinishTick(__instance) != -1 &&
                   __instance.TicksSinceLastPerformed < 1200000;
        return false;
    }
}
