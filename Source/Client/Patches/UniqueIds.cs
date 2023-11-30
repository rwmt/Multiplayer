using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(UniqueIDsManager))]
    [HarmonyPatch(nameof(UniqueIDsManager.GetNextID))]
    public static class UniqueIdsPatch
    {
        // Start at -2 because -1 is sometimes used as the uninitialized marker
        private static int localIds = -2;

        static bool Prefix()
        {
            return Multiplayer.Client == null || (!Multiplayer.InInterface && Current.ProgramState != ProgramState.Entry);
        }

        static void Postfix(ref int __result)
        {
            if (Multiplayer.Client == null) return;

            if (Multiplayer.InInterface || Current.ProgramState == ProgramState.Entry)
                __result = localIds--;
        }
    }

    [HarmonyPatch]
    static class CancelReinitializationDuringLoading
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(OutfitDatabase), nameof(OutfitDatabase.GenerateStartingOutfits));
            yield return AccessTools.Method(typeof(DrugPolicyDatabase), nameof(DrugPolicyDatabase.GenerateStartingDrugPolicies));
            yield return AccessTools.Method(typeof(FoodRestrictionDatabase), nameof(FoodRestrictionDatabase.GenerateStartingFoodRestrictions));
        }

        static bool Prefix() => Scribe.mode != LoadSaveMode.LoadingVars;
    }

    [HarmonyPatch(typeof(OutfitDatabase), nameof(OutfitDatabase.MakeNewOutfit))]
    static class OutfitUniqueIdPatch
    {
        static void Postfix(Outfit __result)
        {
            if (Multiplayer.Ticking || Multiplayer.ExecutingCmds)
                __result.uniqueId = Find.UniqueIDsManager.GetNextThingID();
        }
    }

    [HarmonyPatch(typeof(DrugPolicyDatabase), nameof(DrugPolicyDatabase.MakeNewDrugPolicy))]
    static class DrugPolicyUniqueIdPatch
    {
        static void Postfix(DrugPolicy __result)
        {
            if (Multiplayer.Ticking || Multiplayer.ExecutingCmds)
                __result.uniqueId = Find.UniqueIDsManager.GetNextThingID();
        }
    }

    [HarmonyPatch(typeof(FoodRestrictionDatabase), nameof(FoodRestrictionDatabase.MakeNewFoodRestriction))]
    static class FoodRestrictionUniqueIdPatch
    {
        static void Postfix(FoodRestriction __result)
        {
            if (Multiplayer.Ticking || Multiplayer.ExecutingCmds)
                __result.id = Find.UniqueIDsManager.GetNextThingID();
        }
    }

    [HarmonyPatch]
    static class MessagesMarker
    {
        public static bool? historical;

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Messages), nameof(Messages.Message), new[] { typeof(string), typeof(MessageTypeDef), typeof(bool) });
            yield return AccessTools.Method(typeof(Messages), nameof(Messages.Message), new[] { typeof(string), typeof(LookTargets), typeof(MessageTypeDef), typeof(bool) });
        }

        static void Prefix(bool historical) => MessagesMarker.historical = historical;
        static void Postfix() => historical = null;
    }

    [HarmonyPatch(typeof(UniqueIDsManager), nameof(UniqueIDsManager.GetNextMessageID))]
    static class NextMessageIdPatch
    {
        static int nextUniqueUnhistoricalMessageId = -1;

        static bool Prefix() => !MessagesMarker.historical.HasValue || MessagesMarker.historical.Value;

        static void Postfix(ref int __result)
        {
            if (MessagesMarker.historical.HasValue && !MessagesMarker.historical.Value)
                __result = nextUniqueUnhistoricalMessageId--;
        }
    }

    [HarmonyPatch(typeof(Archive), nameof(Archive.Add))]
    static class ArchiveAddPatch
    {
        static bool Prefix(IArchivable archivable)
        {
            if (Multiplayer.Client == null) return true;

            // Negative id means they are interface-only
            if (archivable is Message { ID: < 0 })
                return false;

            if (archivable is Letter { ID: < 0 })
                return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(CrossRefHandler), nameof(CrossRefHandler.Clear))]
    static class CrossRefHandler_Clear_Patch
    {
        static void Prefix(CrossRefHandler __instance, bool errorIfNotEmpty)
        {
            // If called from CrossRefHandler.ResolveAllCrossReferences and exposing during playtime, fix object ids
            if (errorIfNotEmpty && ScribeUtil.loading)
            {
                foreach (var key in ScribeUtil.sharedCrossRefs.tempKeys)
                {
                    var obj = ScribeUtil.sharedCrossRefs.allObjectsByLoadID[key];
                    if (obj is Thing { thingIDNumber: < 0 } t)
                        t.thingIDNumber = Find.UniqueIDsManager.GetNextThingID();
                    else if (obj is Gene { loadID: < 0 } g)
                        g.loadID = Find.UniqueIDsManager.GetNextGeneID();
                    else if (obj is Hediff { loadID: < 0 } h)
                        h.loadID = Find.UniqueIDsManager.GetNextHediffID();
                }
            }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.IDNumberFromThingID))]
    static class HandleNegativeThingIdWhenLoading
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                // Handle negative thing ids when loading
                // These come from getting a unique id in the interface and are fixed (replaced) later when necessary
                if (inst.operand == "\\d+$")
                    inst.operand = "-?\\d+$";

                yield return inst;
            }
        }
    }

}
