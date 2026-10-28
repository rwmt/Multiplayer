using System.Linq;
using HarmonyLib;
using Multiplayer.Client.Factions;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Patches;

// Multifaction: ideo and age ticking run in the world tick, whose context is
// the spectator faction - so every vanilla `Faction.OfPlayer`-gated feature
// in those paths silently never fires for anyone: date obligations
// (festivals/celebrations), the small-ideo ObligationsActive fallback, role
// activity letters, funeral obligations for caravan/cross-map deaths, and
// growth-moment letters for caravan children (auto-rolled instead). Push the
// right player faction's context around each entry point; letters then also
// route to that faction instead of being dropped for everyone.
static class IdeoContextUtil
{
    // Deterministic "whose ideo is this": the lowest-loadID ownable player
    // faction following it (identical on all clients)
    public static Faction PrimaryPlayerFollower(Ideo ideo)
    {
        if (ideo == null)
            return null;

        return Find.FactionManager.AllFactionsListForReading
            .Where(f => QuestFactionOwnership.IsOwnablePlayerFaction(f) && f.ideos != null && f.ideos.Has(ideo))
            .OrderBy(f => f.loadID)
            .FirstOrDefault();
    }

    public static bool PushIfOwnable(Faction faction)
    {
        if (!QuestFactionOwnership.IsOwnablePlayerFaction(faction) || faction == Faction.OfPlayer)
            return false;

        ((Map)null).PushFaction(faction);
        return true;
    }
}

[HarmonyPatch(typeof(Ideo), nameof(Ideo.IdeoTick))]
static class IdeoTickContextPatch
{
    static void Prefix(Ideo __instance, ref bool __state)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return;

        __state = IdeoContextUtil.PushIfOwnable(IdeoContextUtil.PrimaryPlayerFollower(__instance));
    }

    static void Finalizer(bool __state)
    {
        if (__state)
            FactionExtensions.PopFaction();
    }
}

// Death/corpse-destroyed obligation triggers gate on the dead pawn's home
// faction being Faction.OfPlayer - push it so caravan and cross-map deaths
// still produce funerals
[HarmonyPatch]
static class IdeoMemberNotifyContextPatch
{
    static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Ideo), nameof(Ideo.Notify_MemberDied));
        yield return AccessTools.Method(typeof(Ideo), nameof(Ideo.Notify_MemberCorpseDestroyed));
    }

    static void Prefix(Pawn member, ref bool __state)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction || member == null)
            return;

        __state = IdeoContextUtil.PushIfOwnable(member.HomeFaction);
    }

    static void Finalizer(bool __state)
    {
        if (__state)
            FactionExtensions.PopFaction();
    }
}

// Growth moments: a caravan child's birthday ticks under the spectator, so
// the letter branch's `pawn.Faction != Faction.OfPlayer` check fails and the
// trait/passions are rolled silently with no player choice
[HarmonyPatch(typeof(Pawn_AgeTracker), "BirthdayBiological")]
static class BirthdayBiologicalContextPatch
{
    static void Prefix(Pawn_AgeTracker __instance, ref bool __state)
    {
        if (Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return;

        __state = IdeoContextUtil.PushIfOwnable(__instance.pawn.Faction);
    }

    static void Finalizer(bool __state)
    {
        if (__state)
            FactionExtensions.PopFaction();
    }
}
