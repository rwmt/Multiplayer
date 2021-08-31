using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Patches;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    public static class SyncMethods
    {
        static SyncField SyncTimetable;

        public static void Init()
        {
            SyncTimetable = Sync.Field(typeof(Pawn), nameof(Pawn.timetable), nameof(Pawn_TimetableTracker.times));

            SyncMethod.Register(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted));
            SyncMethod.Register(typeof(Pawn_DraftController), nameof(Pawn_DraftController.FireAtWill));
            SyncMethod.Register(typeof(Pawn_DrugPolicyTracker), nameof(Pawn_DrugPolicyTracker.CurrentPolicy)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(Pawn_OutfitTracker), nameof(Pawn_OutfitTracker.CurrentOutfit)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(Pawn_FoodRestrictionTracker), nameof(Pawn_FoodRestrictionTracker.CurrentFoodRestriction)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.AreaRestriction));
            SyncMethod.Register(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.Master));
            SyncMethod.Register(typeof(Pawn), nameof(Pawn.Name)).ExposeParameter(0);
            SyncMethod.Register(typeof(StorageSettings), nameof(StorageSettings.Priority));
            SyncMethod.Register(typeof(CompForbiddable), nameof(CompForbiddable.Forbidden));

            SyncMethod.Register(typeof(Pawn_TimetableTracker), nameof(Pawn_TimetableTracker.SetAssignment));
            SyncMethod.Register(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority));
            SyncMethod.Register(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob)).SetContext(SyncContext.QueueOrder_Down).ExposeParameter(0);
            SyncMethod.Register(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJobPrioritizedWork)).SetContext(SyncContext.QueueOrder_Down).ExposeParameter(0);
            SyncMethod.Register(typeof(Pawn_TrainingTracker), nameof(Pawn_TrainingTracker.SetWantedRecursive));
            SyncMethod.Register(typeof(Zone), nameof(Zone.Delete));
            SyncMethod.Register(typeof(BillStack), nameof(BillStack.AddBill)).ExposeParameter(0); // Only used for pasting
            SyncMethod.Register(typeof(BillStack), nameof(BillStack.Delete)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(BillStack), nameof(BillStack.Reorder)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(Bill_Production), nameof(Bill_Production.SetStoreMode));
            SyncMethod.Register(typeof(Building_TurretGun), nameof(Building_TurretGun.OrderAttack));
            SyncMethod.Register(typeof(Building_TurretGun), nameof(Building_TurretGun.ExtractShell));
            SyncMethod.Register(typeof(Area), nameof(Area.Invert));
            SyncMethod.Register(typeof(Area), nameof(Area.Delete));
            SyncMethod.Register(typeof(Area_Allowed), nameof(Area_Allowed.SetLabel));
            SyncMethod.Register(typeof(AreaManager), nameof(AreaManager.TryMakeNewAllowed));

            SyncMethod.Register(typeof(DrugPolicyDatabase), nameof(DrugPolicyDatabase.MakeNewDrugPolicy));
            SyncMethod.Register(typeof(DrugPolicyDatabase), nameof(DrugPolicyDatabase.TryDelete)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(OutfitDatabase), nameof(OutfitDatabase.MakeNewOutfit));
            SyncMethod.Register(typeof(OutfitDatabase), nameof(OutfitDatabase.TryDelete)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(FoodRestrictionDatabase), nameof(FoodRestrictionDatabase.MakeNewFoodRestriction));
            SyncMethod.Register(typeof(FoodRestrictionDatabase), nameof(FoodRestrictionDatabase.TryDelete)).CancelIfAnyArgNull();

            SyncMethod.Register(typeof(Building_Bed), nameof(Building_Bed.Medical));

            {
                var methodNames = new [] {
                    nameof(CompAssignableToPawn.TryAssignPawn),
                    nameof(CompAssignableToPawn.TryUnassignPawn),
                };

                var methods = typeof(CompAssignableToPawn).AllSubtypesAndSelf()
                    .SelectMany(t => methodNames.Select(n => t.GetMethod(n, AccessTools.allDeclared)))
                    .AllNotNull();

                foreach (var method in methods) {
                    Sync.RegisterSyncMethod(method).CancelIfAnyArgNull();
                }
            }

            SyncMethod.Register(typeof(PawnColumnWorker_Designator), nameof(PawnColumnWorker_Designator.SetValue)).CancelIfAnyArgNull(); // Virtual but currently not overriden by any subclasses
            SyncMethod.Register(typeof(PawnColumnWorker_FollowDrafted), nameof(PawnColumnWorker_FollowDrafted.SetValue)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(PawnColumnWorker_FollowFieldwork), nameof(PawnColumnWorker_FollowFieldwork.SetValue)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(CompGatherSpot), nameof(CompGatherSpot.Active));
            SyncMethod.Register(typeof(Building_BlastingCharge), nameof(Building_BlastingCharge.Command_Detonate));

            SyncMethod.Register(typeof(Building_Grave), nameof(Building_Grave.EjectContents));
            SyncMethod.Register(typeof(Building_Casket), nameof(Building_Casket.EjectContents));
            SyncMethod.Register(typeof(Building_CryptosleepCasket), nameof(Building_CryptosleepCasket.EjectContents));
            SyncMethod.Register(typeof(Building_AncientCryptosleepCasket), nameof(Building_AncientCryptosleepCasket.EjectContents));
            SyncMethod.Register(typeof(Building_Crate), nameof(Building_Crate.EjectContents));

            SyncMethod.Register(typeof(Building_OrbitalTradeBeacon), nameof(Building_OrbitalTradeBeacon.MakeMatchingStockpile));
            SyncMethod.Register(typeof(Building_SunLamp), nameof(Building_SunLamp.MakeMatchingGrowZone));
            SyncMethod.Register(typeof(Building_ShipComputerCore), nameof(Building_ShipComputerCore.TryLaunch));
            SyncMethod.Register(typeof(CompPower), nameof(CompPower.TryManualReconnect));
            SyncMethod.Register(typeof(CompTempControl), nameof(CompTempControl.InterfaceChangeTargetTemperature));
            SyncMethod.Register(typeof(CompTransporter), nameof(CompTransporter.CancelLoad), new SyncType[0]);
            SyncMethod.Register(typeof(StorageSettings), nameof(StorageSettings.CopyFrom)).ExposeParameter(0);
            SyncMethod.Lambda(typeof(Command_SetTargetFuelLevel), nameof(Command_SetTargetFuelLevel.ProcessInput), 2); // Set target fuel level from Dialog_Slider
            SyncMethod.Register(typeof(ITab_Pawn_Gear), nameof(ITab_Pawn_Gear.InterfaceDrop)).SetContext(SyncContext.MapSelected | SyncContext.QueueOrder_Down).CancelIfAnyArgNull().CancelIfNoSelectedMapObjects();
            SyncMethod.Register(typeof(FoodUtility), nameof(FoodUtility.IngestFromInventoryNow)).SetContext(SyncContext.MapSelected | SyncContext.QueueOrder_Down).CancelIfAnyArgNull().CancelIfNoSelectedMapObjects();

            SyncMethod.Register(typeof(Caravan_PathFollower), nameof(Caravan_PathFollower.Paused));
            SyncMethod.Register(typeof(CaravanFormingUtility), nameof(CaravanFormingUtility.StopFormingCaravan)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(CaravanFormingUtility), nameof(CaravanFormingUtility.RemovePawnFromCaravan)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(CaravanFormingUtility), nameof(CaravanFormingUtility.LateJoinFormingCaravan)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(SettleInEmptyTileUtility), nameof(SettleInEmptyTileUtility.Settle)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(SettlementAbandonUtility), nameof(SettlementAbandonUtility.Abandon)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(WorldSelector), nameof(WorldSelector.AutoOrderToTileNow)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(CaravanMergeUtility), nameof(CaravanMergeUtility.TryMergeSelectedCaravans)).SetContext(SyncContext.WorldSelected);
            SyncMethod.Register(typeof(PawnBanishUtility), nameof(PawnBanishUtility.Banish)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(SettlementUtility), nameof(SettlementUtility.Attack)).CancelIfAnyArgNull();

            SyncMethod.Register(typeof(WITab_Caravan_Gear), nameof(WITab_Caravan_Gear.TryEquipDraggedItem)).SetContext(SyncContext.WorldSelected).CancelIfNoSelectedWorldObjects().CancelIfAnyArgNull();
            SyncMethod.Register(typeof(WITab_Caravan_Gear), nameof(WITab_Caravan_Gear.MoveDraggedItemToInventory)).SetContext(SyncContext.WorldSelected).CancelIfNoSelectedWorldObjects();

            SyncMethod.Register(typeof(InstallBlueprintUtility), nameof(InstallBlueprintUtility.CancelBlueprintsFor)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(Command_LoadToTransporter), nameof(Command_LoadToTransporter.ProcessInput));

            SyncMethod.Register(typeof(Quest), nameof(Quest.Accept));
            SyncMethod.Register(typeof(PatchQuestChoices), nameof(PatchQuestChoices.Choose));

            {
                var methods = typeof(ITargetingSource).AllImplementing()
                    .Where(t => t.Assembly == typeof(Game).Assembly)
                    .Select(t => t.GetMethod(nameof(ITargetingSource.OrderForceTarget), AccessTools.allDeclared))
                    .AllNotNull();

                foreach (var method in methods) {
                    Sync.RegisterSyncMethod(method);
                }
            }

            SyncMethod.Register(typeof(RoyalTitlePermitWorker_CallLaborers), nameof(RoyalTitlePermitWorker_CallLaborers.CallLaborers));
            SyncMethod.Register(typeof(RoyalTitlePermitWorker_DropResources), nameof(RoyalTitlePermitWorker_DropResources.CallResourcesToCaravan));

            SyncMethod.Register(typeof(Pawn_RoyaltyTracker), nameof(Pawn_RoyaltyTracker.AddPermit));
            SyncMethod.Register(typeof(Pawn_RoyaltyTracker), nameof(Pawn_RoyaltyTracker.RefundPermits));
            SyncMethod.Register(typeof(Pawn_RoyaltyTracker), nameof(Pawn_RoyaltyTracker.SetTitle)); // Used for title renouncing
            SyncMethod.Register(typeof(Pawn_RoyaltyTracker), nameof(Pawn_RoyaltyTracker.ResetPermitsAndPoints)); // Used for title renouncing

            SyncMethod.Register(typeof(MonumentMarker), nameof(MonumentMarker.PlaceAllBlueprints));
            SyncMethod.Register(typeof(MonumentMarker), nameof(MonumentMarker.PlaceBlueprintsSimilarTo)).ExposeParameter(0);

            SyncMethod.Register(typeof(Ability), nameof(Ability.QueueCastingJob), new SyncType[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) });
            SyncMethod.Register(typeof(Ability), nameof(Ability.QueueCastingJob), new SyncType[] { typeof(GlobalTargetInfo) });

            // 1
            SyncMethod.Register(typeof(TradeRequestComp), nameof(TradeRequestComp.Fulfill)).CancelIfAnyArgNull().SetVersion(1);

            // 2
            SyncMethod.Register(typeof(CompLaunchable), nameof(CompLaunchable.TryLaunch)).ExposeParameter(1).SetVersion(2);
            SyncMethod.Register(typeof(OutfitForcedHandler), nameof(OutfitForcedHandler.Reset)).SetVersion(2);
            SyncMethod.Register(typeof(Pawn_StoryTracker), nameof(Pawn_StoryTracker.Title)).SetVersion(2);

            // 3
            SyncMethod.Register(typeof(ShipUtility), nameof(ShipUtility.StartupHibernatingParts)).CancelIfAnyArgNull().SetVersion(3);

            SyncMethod.Register(typeof(Verb_SmokePop), nameof(Verb_SmokePop.Pop));
            SyncMethod.Register(typeof(Verb_DeployBroadshield), nameof(Verb_DeployBroadshield.Deploy));

            // Dialog_NodeTree
            Sync.RegisterSyncDialogNodeTree(typeof(IncidentWorker_CaravanMeeting), nameof(IncidentWorker_CaravanMeeting.TryExecuteWorker));
            Sync.RegisterSyncDialogNodeTree(typeof(IncidentWorker_CaravanDemand), nameof(IncidentWorker_CaravanDemand.TryExecuteWorker));

            SyncMethod.Register(typeof(CompAnimalPenMarker), nameof(CompAnimalPenMarker.RemoveForceDisplayedAnimal));
            SyncMethod.Register(typeof(CompAnimalPenMarker), nameof(CompAnimalPenMarker.AddForceDisplayedAnimal));
            SyncMethod.Register(typeof(CompAnimalPenMarker), nameof(CompAnimalPenMarker.DesignatePlantsToCut));

            SyncMethod.Register(typeof(ShipJob_Wait), nameof(ShipJob_Wait.Launch)).ExposeParameter(1); // Launch the (Royalty) shuttle

            var TransferableOneWaySerializer = Serializer.New(
                (TransferableOneWay t, object target, object[] args) =>
                    ((target as ITab_ContentsTransporter).Transporter, t.AnyThing.thingIDNumber),
                data =>
                    data.Transporter.leftToLoad.Find(t => t.things.Any(thing => thing.thingIDNumber == data.thingIDNumber))
            );

            SyncMethod.Register(typeof(ITab_ContentsTransporter), nameof(ITab_ContentsTransporter.OnDropThing)).SetContext(SyncContext.MapSelected); // overriden ITab_ContentsBase.OnDropThing
            SyncMethod.Register(typeof(ITab_ContentsTransporter), nameof(ITab_ContentsTransporter.OnDropToLoadThing))
                .TransformArgument(0, TransferableOneWaySerializer)
                .SetContext(SyncContext.MapSelected)
                .CancelIfAnyArgNull();

            SyncMethod.Register(typeof(Precept_Ritual), nameof(Precept_Ritual.ShowRitualBeginWindow));

            // Inventory (medicine) stock up
            SyncMethod.Register(typeof(Pawn_InventoryStockTracker), nameof(Pawn_InventoryStockTracker.SetCountForGroup));
            SyncMethod.Register(typeof(Pawn_InventoryStockTracker), nameof(Pawn_InventoryStockTracker.SetThingForGroup));

            // Used by "Set to standard playstyle" in storyteller settings
            SyncMethod.Register(typeof(Difficulty), nameof(Difficulty.CopyFrom))
                .SetHostOnly()
                .TransformTarget(Serializer.New(
                    (Difficulty d, object target, object[] args) => (object)null,
                    d => Find.Storyteller.difficulty
                ));

            SyncMethod.Register(typeof(IdeoDevelopmentUtility), nameof(IdeoDevelopmentUtility.ApplyChangesToIdeo))
                .ExposeParameter(1);
        }

        [MpPrefix(typeof(PawnColumnWorker_CopyPasteTimetable), nameof(PawnColumnWorker_CopyPasteTimetable.PasteTo))]
        static bool PastePawnTimetable(Pawn p)
        {
            return !SyncTimetable.DoSync(p, PawnColumnWorker_CopyPasteTimetable.clipboard);
        }

        [MpPrefix(typeof(StorageSettingsClipboard), nameof(StorageSettingsClipboard.Copy))]
        static void StorageSettingsClipboardCopy_Prefix() => Multiplayer.dontSync = true;

        [MpPostfix(typeof(StorageSettingsClipboard), nameof(StorageSettingsClipboard.Copy))]
        static void StorageSettingsClipboardCopy_Postfix() => Multiplayer.dontSync = false;

        [MpTranspiler(typeof(CompPlantable), nameof(CompPlantable.BeginTargeting), lambdaOrdinal: 0)]
        static IEnumerable<CodeInstruction> CompPlantableTranspiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                // this.plantCells.Add(t.Cell) => CompPlantable_AddCell(t.Cell, this)
                if (inst.operand == typeof(List<IntVec3>).GetMethod("Add"))
                {
                    // Load this
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    // Consume cell and this
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SyncMethods), nameof(CompPlantable_AddCell)));
                    // Pop the list
                    yield return new CodeInstruction(OpCodes.Pop);
                    continue;
                }

                yield return inst;
            }
        }

        [SyncMethod]
        static void CompPlantable_AddCell(IntVec3 newValue, CompPlantable plantable) =>
            plantable.plantCells.Add(newValue);

        // ===== CALLBACKS =====

        [MpPostfix(typeof(DrugPolicyDatabase), nameof(DrugPolicyDatabase.MakeNewDrugPolicy))]
        static void MakeNewDrugPolicy_Postfix(DrugPolicy __result)
        {
            var dialog = GetDialogDrugPolicies();
            if (__result != null && dialog != null && TickPatch.currentExecutingCmdIssuedBySelf)
                dialog.SelectedPolicy = __result;
        }

        [MpPostfix(typeof(OutfitDatabase), nameof(OutfitDatabase.MakeNewOutfit))]
        static void MakeNewOutfit_Postfix(Outfit __result)
        {
            var dialog = GetDialogOutfits();
            if (__result != null && dialog != null && TickPatch.currentExecutingCmdIssuedBySelf)
                dialog.SelectedOutfit = __result;
        }

        [MpPostfix(typeof(FoodRestrictionDatabase), nameof(FoodRestrictionDatabase.MakeNewFoodRestriction))]
        static void MakeNewFood_Postfix(FoodRestriction __result)
        {
            var dialog = GetDialogFood();
            if (__result != null && dialog != null && TickPatch.currentExecutingCmdIssuedBySelf)
                dialog.SelectedFoodRestriction = __result;
        }

        [MpPostfix(typeof(DrugPolicyDatabase), nameof(DrugPolicyDatabase.TryDelete))]
        static void TryDeleteDrugPolicy_Postfix(DrugPolicy policy, AcceptanceReport __result)
        {
            var dialog = GetDialogDrugPolicies();
            if (__result.Accepted && dialog != null && dialog.SelectedPolicy == policy)
                dialog.SelectedPolicy = null;
        }

        [MpPostfix(typeof(OutfitDatabase), nameof(OutfitDatabase.TryDelete))]
        static void TryDeleteOutfit_Postfix(Outfit outfit, AcceptanceReport __result)
        {
            var dialog = GetDialogOutfits();
            if (__result.Accepted && dialog != null && dialog.SelectedOutfit == outfit)
                dialog.SelectedOutfit = null;
        }

        [MpPostfix(typeof(FoodRestrictionDatabase), nameof(FoodRestrictionDatabase.TryDelete))]
        static void TryDeleteFood_Postfix(FoodRestriction foodRestriction, AcceptanceReport __result)
        {
            var dialog = GetDialogFood();
            if (__result.Accepted && dialog != null && dialog.SelectedFoodRestriction == foodRestriction)
                dialog.SelectedFoodRestriction = null;
        }

        static Dialog_ManageDrugPolicies GetDialogDrugPolicies() => Find.WindowStack?.WindowOfType<Dialog_ManageDrugPolicies>();
        static Dialog_ManageOutfits GetDialogOutfits() => Find.WindowStack?.WindowOfType<Dialog_ManageOutfits>();
        static Dialog_ManageFoodRestrictions GetDialogFood() => Find.WindowStack?.WindowOfType<Dialog_ManageFoodRestrictions>();

        [MpPostfix(typeof(WITab_Caravan_Gear), nameof(WITab_Caravan_Gear.TryEquipDraggedItem))]
        static void TryEquipDraggedItem_Postfix(WITab_Caravan_Gear __instance)
        {
            __instance.droppedDraggedItem = false;
            __instance.draggedItem = null;
        }

        [MpPostfix(typeof(WITab_Caravan_Gear), nameof(WITab_Caravan_Gear.MoveDraggedItemToInventory))]
        static void MoveDraggedItemToInventory_Postfix(WITab_Caravan_Gear __instance)
        {
            __instance.droppedDraggedItem = false;
            __instance.draggedItem = null;
        }

        public static int tradeJobStartedByMe = -1;
        public static int stylingStationJobStartedByMe = -1;

        [MpPrefix(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
        static void TryTakeOrderedJob_Prefix(Job job)
        {
            // If Pawn_JobTracker.TryTakeOrderedJob is synced directly its job is created in interface code
            // UniqueIDs assigned in the interface are always negative and specific to a client
            // This assigns the job a proper id

            if (Multiplayer.ExecutingCmds && job.loadID < 0)
            {
                job.loadID = Find.UniqueIDsManager.GetNextJobID();

                if (!TickPatch.currentExecutingCmdIssuedBySelf)
                    return;

                if (job.def == JobDefOf.TradeWithPawn || job.def == JobDefOf.UseCommsConsole)
                    tradeJobStartedByMe = job.loadID;

                if (job.def == JobDefOf.OpenStylingStationDialog)
                    stylingStationJobStartedByMe = job.loadID;
            }
        }

        [MpPrefix(typeof(BillStack), nameof(BillStack.AddBill))]
        static void AddBill_Prefix(Bill bill)
        {
            // See the comments about job ids above
            if (Multiplayer.ExecutingCmds && bill.loadID < 0)
                bill.loadID = Find.UniqueIDsManager.GetNextBillID();
        }

        [MpPostfix(typeof(Ideo), nameof(Ideo.CopyTo))]
        static void GenerateNewPreceptIds(Ideo __instance)
        {
            foreach (var precept in __instance.precepts)
                precept.ID = Find.UniqueIDsManager.GetNextPreceptID();
        }
    }

}
