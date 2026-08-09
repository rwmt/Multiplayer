using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Patches;

// PlayerItemAccessibilityUtility caches its accessible-things scan keyed on
// (tile, RealTime.frameCount). Frame counts are per-client machine state:
// whether a call rescans or reuses depends on the local framerate, and the
// scan consumes synced RNG (GenMath.RoundRandom on pawn leather amounts), so
// per-client cache collapse diverges the rand stream. The sim callers are
// quest generation: QuestNode_TradeRequest_GetRequestedThing's validator
// calls PossiblyAccessible + PlayerCanMake for EVERY def in
// ThingSetMakerUtility.allGeneratableItems (hundreds, 1-3 scans each), and
// QuestNode_Root_Beggars (5 defs) - and TradeRequest's TestRun runs during
// every random-quest selection (NaturalRandomQuestChooser evaluates all
// candidate roots), not just when it wins.
//
// The old fix forced a full rescan on every call. That made the draw count
// deterministic but turned each quest-fire tick into 300-700 full
// every-item-on-every-map scans: a multi-second freeze on every client at
// every random quest arrival. The requirement was never "always
// rescan", only "every client rescans the same number of times" - so in sim
// context, repeat calls collapse per (tile, TickPatch.Timer), identical on
// every client in every sim context: the first call each session-tick
// rescans (also flushing any content a UI call computed between ticks; sim
// ticks are atomic on the main thread, so UI calls cannot interleave
// mid-tick), repeats within the tick reuse it. Outside the sim, vanilla
// behavior is untouched.
[HarmonyPatch(typeof(PlayerItemAccessibilityUtility), nameof(PlayerItemAccessibilityUtility.CacheAccessibleThings))]
static class ItemAccessibilityCacheInvalidation
{
    private static (PlanetTile tile, int timer)? lastSimScan;

    static void Prefix(PlanetTile nearTile)
    {
        if (Multiplayer.Client == null)
            return;
        if (!Multiplayer.Ticking && !Multiplayer.ExecutingCmds)
            return;

        if (lastSimScan is { } last && last.tile == nearTile && last.timer == TickPatch.Timer)
        {
            // Same tile, same session tick: force vanilla's guard to pass so
            // the content computed earlier this tick is reused
            PlayerItemAccessibilityUtility.cachedAccessibleThingsForTile = nearTile;
            PlayerItemAccessibilityUtility.cachedAccessibleThingsForFrame = RealTime.frameCount;
        }
        else
        {
            // Frame key, not the tile: PlanetTile.Invalid is a legitimate
            // nearTile for map-less callers and would re-match; frameCount is
            // never -1
            PlayerItemAccessibilityUtility.cachedAccessibleThingsForFrame = -1;
            lastSimScan = (nearTile, TickPatch.Timer);
        }
    }

    // A forced reuse must never serve content from before a reload - the
    // cached Thing refs die at SaveAndReload. Called from ClearAllPatch.
    public static void Reset() => lastSimScan = null;
}
