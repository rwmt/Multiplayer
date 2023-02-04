using HarmonyLib;
using Multiplayer.Common;
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
        private static IdBlock currentBlock;

        public static IdBlock CurrentBlock
        {
            get => currentBlock;

            set
            {
                if (value != null && currentBlock != null && currentBlock != value)
                    Log.Warning("Reassigning the current id block!");
                currentBlock = value;
            }
        }

        // Start at -2 because -1 is sometimes used as the uninitialized marker
        private static int localIds = -2;

        static bool Prefix()
        {
            return Multiplayer.Client == null || !Multiplayer.InInterface;
        }

        static void Postfix(ref int __result)
        {
            if (Multiplayer.Client == null) return;

            /*IdBlock currentBlock = CurrentBlock;
            if (currentBlock == null)
            {
                __result = localIds--;
                if (!Multiplayer.ShouldSync)
                    Log.Warning("Tried to get a unique id without an id block set!");
                return;
            }

            __result = currentBlock.NextId();*/

            if (Multiplayer.InInterface)
            {
                __result = localIds--;
            }
            else
            {
                __result = Multiplayer.GlobalIdBlock.NextId();
            }

            //MpLog.Log("got new id " + __result);

            /*if (currentBlock.current > currentBlock.blockSize * 0.95f && !currentBlock.overflowHandled)
            {
                Multiplayer.Client.Send(Packets.Client_IdBlockRequest, CurrentBlock.mapId);
                currentBlock.overflowHandled = true;
            }*/
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
                __result.uniqueId = Multiplayer.GlobalIdBlock.NextId();
        }
    }

    [HarmonyPatch(typeof(DrugPolicyDatabase), nameof(DrugPolicyDatabase.MakeNewDrugPolicy))]
    static class DrugPolicyUniqueIdPatch
    {
        static void Postfix(DrugPolicy __result)
        {
            if (Multiplayer.Ticking || Multiplayer.ExecutingCmds)
                __result.uniqueId = Multiplayer.GlobalIdBlock.NextId();
        }
    }

    [HarmonyPatch(typeof(FoodRestrictionDatabase), nameof(FoodRestrictionDatabase.MakeNewFoodRestriction))]
    static class FoodRestrictionUniqueIdPatch
    {
        static void Postfix(FoodRestriction __result)
        {
            if (Multiplayer.Ticking || Multiplayer.ExecutingCmds)
                __result.id = Multiplayer.GlobalIdBlock.NextId();
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

}
