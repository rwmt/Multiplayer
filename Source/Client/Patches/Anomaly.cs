using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(AnomalyUtility), nameof(AnomalyUtility.TryGetNearbyUnseenCell))]
    static class AnomalyUtility_TryGetNearbyUnseenCell
    {
        // Discards the result from CurrentViewRect and calls EmptyCellRect
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var target = AccessTools.PropertyGetter(typeof(CameraDriver), nameof(CameraDriver.CurrentViewRect));
            var replace = AccessTools.Method(typeof(AnomalyUtility_TryGetNearbyUnseenCell), nameof(EmptyCellRect));

            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.operand as MethodInfo == target)
                {
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Call, replace);
                }
            }
        }

        static CellRect EmptyCellRect()
        {
            return Multiplayer.Client != null ? CellRect.Empty : Find.CameraDriver.CurrentViewRect;
        }

        // Seed per pawn so that multiple calls in one tick don't all pick the same cell
        static void Prefix(Pawn pawn, ref bool __state)
        {
            if (Multiplayer.Client == null) return;
            Rand.PushState(Gen.HashCombineInt(pawn?.thingIDNumber ?? 0, Find.TickManager.TicksAbs));
            __state = true;
        }

        static void Finalizer(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    // #959: vanilla checks whether the local camera sees the cell (map == Find.CurrentMap && view.Contains).
    // An empty view makes the check camera independent; covers the container escape in UnnaturalCorpseTracker.
    [HarmonyPatch(typeof(AnomalyUtility), nameof(AnomalyUtility.IsValidUnseenCell))]
    static class AnomalyUtility_IsValidUnseenCell
    {
        static void Prefix(ref CellRect view)
        {
            if (Multiplayer.Client != null)
                view = CellRect.Empty;
        }
    }

    // #959: the corpse teleports/disappears only when the local camera doesn't see it.
    // Keep the reservation check (shared state), treat the corpse as never seen by a camera.
    [HarmonyPatch(typeof(UnnaturalCorpse), nameof(UnnaturalCorpse.IsOutsideView))]
    static class UnnaturalCorpse_IsOutsideView
    {
        static bool Prefix(UnnaturalCorpse __instance, ref bool __result)
        {
            if (Multiplayer.Client == null)
                return true;

            __result = !(__instance.SpawnedOrAnyParentSpawned && ReservedByAnyPlayer(__instance));
            return false;
        }

        // Faction.OfPlayer is the spectator when ticking a map no player faction owns (multifaction), so check the
        // reservations of every player faction
        static bool ReservedByAnyPlayer(UnnaturalCorpse corpse)
        {
            var reservations = corpse.MapHeld.reservationManager;
            foreach (var faction in Find.FactionManager.AllFactionsListForReading)
                if (faction.IsPlayer && reservations.IsReservedByAnyoneOf(corpse, faction))
                    return true;
            return false;
        }
    }

    // #855: vanilla moves the offering out of the invoker's inventory while enumerating it; moving a whole stack
    // throws (collection modified) and the psychic ritual never completes. Same logic over a snapshot, holding no
    // more than the required count.
    [HarmonyPatch(typeof(PsychicRitualToil_InvokeHorax), nameof(PsychicRitualToil_InvokeHorax.HoldRequiredOfferings))]
    static class PsychicRitualToil_InvokeHorax_HoldRequiredOfferings
    {
        static bool Prefix(PsychicRitualToil_InvokeHorax __instance, PsychicRitual psychicRitual)
        {
            if (Multiplayer.Client == null)
                return true;

            var offering = __instance.requiredOffering;
            if (offering == null)
                return false;

            int required = Mathf.CeilToInt(offering.GetBaseCount());

            foreach (Pawn pawn in psychicRitual.assignments.AssignedPawns(__instance.invokerRole))
            {
                foreach (Thing thing in pawn.inventory.GetDirectlyHeldThings().ToList())
                {
                    int missing = required - HeldOfferingCount(pawn, offering);
                    if (missing <= 0)
                        break;

                    if (offering.filter.Allows(thing))
                        pawn.inventory.innerContainer.TryTransferToContainer(thing, pawn.carryTracker.innerContainer, missing);
                }
            }

            return false;
        }

        static int HeldOfferingCount(Pawn pawn, IngredientCount offering)
        {
            int count = 0;
            foreach (Thing held in pawn.carryTracker.innerContainer)
                if (offering.filter.Allows(held))
                    count += held.stackCount;
            return count;
        }
    }
}