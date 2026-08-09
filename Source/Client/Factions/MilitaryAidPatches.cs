using HarmonyLib;
using Multiplayer.Client.AsyncTime;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Factions;

// Faction.lastMilitaryAidRequestTick lives on the NPC
// faction, so faction A calling military aid locks faction B out of that ally
// for a day. It is also a cross-clock stamp (absolute tick written
// under the comms console map's clock, read under whatever clock is installed
// at dialog build). Per-(player faction, NPC faction) stamps live on
// FactionWorldData, on the async world clock; the vanilla field stays written
// so SP is untouched.
//
// Sync-safety: the dialog is built inside the synced comms job on every
// client and NodeTreeDialogSync replays the chosen DiaOption BY INDEX, so
// this patch may change an option's disabled state but must never change
// whether an option appears. RequestMilitaryAidOption always returns exactly
// one option (enabled or disabled with "WaitTime"), and the field swap below
// cannot change that. The swap brackets only that builder and the finalizer
// restores unconditionally.
static class MilitaryAidPatches
{
    // Gated on CooldownClock.Active (all MP, not just multifaction): the
    // clock-skew half applies to single-faction async games too; there the
    // dict simply has one paying faction.
    public static System.Collections.Generic.Dictionary<int, int> ContextStamps()
    {
        var f = Faction.OfPlayer;
        if (f == null)
            return null;
        return Multiplayer.WorldComp.factionData.TryGetValue(f.loadID, out var data) ? data.militaryAidStamps : null;
    }
}

[HarmonyPatch(typeof(FactionDialogMaker), "CallForAid")]
static class MilitaryAidStampComms
{
    static void Postfix(Faction faction)
    {
        if (!CooldownClock.Active)
            return;
        var stamps = MilitaryAidPatches.ContextStamps();
        if (stamps != null)
            stamps[faction.loadID] = CooldownClock.Now;
    }
}

[HarmonyPatch(typeof(RoyalTitlePermitWorker_CallAid), "CallAid")]
static class MilitaryAidStampPermit
{
    static void Postfix(Faction faction)
    {
        if (!CooldownClock.Active)
            return;
        var stamps = MilitaryAidPatches.ContextStamps();
        if (stamps != null)
            stamps[faction.loadID] = CooldownClock.Now;
    }
}

[HarmonyPatch(typeof(FactionDialogMaker), "RequestMilitaryAidOption")]
static class MilitaryAidReadPerPair
{
    static void Prefix(Faction faction, ref int? __state)
    {
        if (!CooldownClock.Active)
            return;
        var stamps = MilitaryAidPatches.ContextStamps();
        if (stamps == null)
            return;

        __state = faction.lastMilitaryAidRequestTick;

        // Vanilla compares lastMilitaryAidRequestTick + 60000 against the
        // ambient TicksGame, so install a value that reproduces this pair's
        // world-clock remaining time under that comparison. No stamp for this
        // pair = available (any value older than a day works).
        faction.lastMilitaryAidRequestTick = stamps.TryGetValue(faction.loadID, out var stamp)
            ? Find.TickManager.TicksGame - (CooldownClock.Now - stamp)
            : -60000;
    }

    static void Finalizer(Faction faction, int? __state)
    {
        if (__state is { } saved)
            faction.lastMilitaryAidRequestTick = saved;
    }
}
