using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
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

        static void Prefix() => Rand.PushState(Find.TickManager.TicksAbs);
        static void Postfix() => Rand.PopState();
    }

    // #959: UnnaturalCorpse gates simulation decisions on the local camera,
    // forking corpse state between clients. Treat it as always unobserved in MP.
    [HarmonyPatch(typeof(AnomalyUtility), nameof(AnomalyUtility.IsValidUnseenCell))]
    static class AnomalyUtility_IsValidUnseenCell
    {
        // An empty rect makes the internal camera/CurrentMap check short-circuit
        // deterministically; covers the container-breakout call in UnnaturalCorpseTracker
        static void Prefix(ref CellRect view)
        {
            if (Multiplayer.Client != null)
                view = CellRect.Empty;
        }
    }

    // #855: vanilla mutates the inventory container it's enumerating, aborting the
    // toil so psychic ritual outcomes never fire. Replicated with a snapshot.
    [HarmonyPatch(typeof(PsychicRitualToil_InvokeHorax), nameof(PsychicRitualToil_InvokeHorax.HoldRequiredOfferings))]
    static class FixHoldRequiredOfferingsCollectionModified
    {
        static bool Prefix(PsychicRitualToil_InvokeHorax __instance, PsychicRitual psychicRitual)
        {
            if (__instance.requiredOffering == null)
                return false;

            foreach (var pawn in psychicRitual.assignments.AssignedPawns(__instance.invokerRole))
                foreach (var thing in pawn.inventory.GetDirectlyHeldThings().ToList())
                    if (__instance.requiredOffering.filter.Allows(thing))
                        pawn.inventory.innerContainer.TryTransferToContainer(thing, pawn.carryTracker.innerContainer,
                            Mathf.CeilToInt(__instance.requiredOffering.GetBaseCount()));

            return false;
        }
    }

    [HarmonyPatch(typeof(UnnaturalCorpse), nameof(UnnaturalCorpse.IsOutsideView))]
    static class UnnaturalCorpse_IsOutsideView
    {
        // Keep the (deterministic) reservation check, skip the camera checks
        static bool Prefix(UnnaturalCorpse __instance, ref bool __result)
        {
            if (Multiplayer.Client == null)
                return true;

            __result = !(__instance.SpawnedOrAnyParentSpawned &&
                         __instance.MapHeld.reservationManager.IsReservedByAnyoneOf(__instance, Faction.OfPlayer));
            return false;
        }
    }
}