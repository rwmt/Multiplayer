using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Patches;

[HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.GetGizmos))]
static class DisableDraftGizmo
{
    static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn_DraftController __instance)
    {
        return __instance.pawn.Faction == Faction.OfPlayer ?
            gizmos :
            Enumerable.Empty<Gizmo>();
    }
}

// todo: needed for multifaction
/*[HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.CheckDefeated))]
static class CheckDefeatedPatch
{
    static bool Prefix()
    {
        return false;
    }
}

[HarmonyPatch(typeof(MapParent), nameof(MapParent.CheckRemoveMapNow))]
static class CheckRemoveMapNowPatch
{
    static bool Prefix()
    {
        return false;
    }
}*/
