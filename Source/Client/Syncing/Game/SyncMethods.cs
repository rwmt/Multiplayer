using HarmonyLib;
using Multiplayer.API;
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
            SyncMethod.Register(typeof(Pawn), nameof(Pawn.Name)).ExposeParameter(0)
                .SetPostInvoke((pawn, _) => ((Pawn)pawn).babyNamingDeadline = -1); // If a newborn was named then mark it as no longer needing to be named
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
            SyncMethod.Register(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.DoBeginResearch))
                .TransformTarget(Serializer.SimpleReader(() => new MainTabWindow_Research()));

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
            SyncMethod.Register(typeof(PawnColumnWorker_Sterilize), nameof(PawnColumnWorker_Sterilize.SetValue)).CancelIfAnyArgNull(); // Will sync even without this, but this will set the column to dirty
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
                    .Except(typeof(CompActivable_RocketswarmLauncher)) // Skip it, as all it does is open another targeter
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
            SyncMethod.Register(typeof(Verb_FirefoamPop), nameof(Verb_FirefoamPop.Pop));
            SyncMethod.Register(typeof(Verb_DeployToxPack), nameof(Verb_DeployToxPack.TryDeploy));

            // Dialog_NodeTree
            Sync.RegisterSyncDialogNodeTree(typeof(IncidentWorker_CaravanMeeting), nameof(IncidentWorker_CaravanMeeting.TryExecuteWorker));
            Sync.RegisterSyncDialogNodeTree(typeof(IncidentWorker_CaravanDemand), nameof(IncidentWorker_CaravanDemand.TryExecuteWorker));

            SyncMethod.Register(typeof(CompAnimalPenMarker), nameof(CompAnimalPenMarker.RemoveForceDisplayedAnimal));
            SyncMethod.Register(typeof(CompAnimalPenMarker), nameof(CompAnimalPenMarker.AddForceDisplayedAnimal));
            SyncMethod.Register(typeof(CompAutoCut), nameof(CompAutoCut.DesignatePlantsToCut));
            SyncMethod.Lambda(typeof(Plant), nameof(Plant.GetGizmos), 0);

            SyncMethod.Register(typeof(ShipJob_Wait), nameof(ShipJob_Wait.Launch)).ExposeParameter(1); // Launch the (Royalty) shuttle

            var TransferableOneWaySerializer = Serializer.New(
                (TransferableOneWay t, object target, object[] args) =>
                    (((ITab_ContentsTransporter)target).Transporter, t.AnyThing.thingIDNumber),
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
                .TransformTarget(Serializer.SimpleReader(() => Find.Storyteller.difficulty));

            SyncMethod.Register(typeof(IdeoDevelopmentUtility), nameof(IdeoDevelopmentUtility.ApplyChangesToIdeo))
                .ExposeParameter(1);

            // A lot of dev mode gizmos
            SyncMethod.Register(typeof(CompPawnSpawnOnWakeup), nameof(CompPawnSpawnOnWakeup.Spawn)).SetDebugOnly();
            SyncMethod.Lambda(typeof(Building_FermentingBarrel), nameof(Building_FermentingBarrel.GetGizmos), 0).SetDebugOnly(); // Set progress to 1
            SyncMethod.Lambda(typeof(Building_FermentingBarrel), nameof(Building_FermentingBarrel.GetGizmos), 1).SetDebugOnly(); // Fill
            SyncMethod.Lambda(typeof(CompBandNode), nameof(CompBandNode.CompGetGizmosExtra), 8).SetDebugOnly(); // Complete tuning
            SyncMethod.Lambda(typeof(CompCauseGameCondition_ForceWeather), nameof(CompCauseGameCondition_ForceWeather.CompGetGizmosExtra), 0).SetDebugOnly(); // Change to next weather
            SyncMethod.Lambda(typeof(CompCauseGameCondition_PsychicEmanation), nameof(CompCauseGameCondition_PsychicEmanation.CompGetGizmosExtra), 0).SetDebugOnly(); // Change gender
            SyncMethod.Lambda(typeof(CompCauseGameCondition_PsychicEmanation), nameof(CompCauseGameCondition_PsychicEmanation.CompGetGizmosExtra), 1).SetDebugOnly(); // Increase intensity
            SyncMethod.Lambda(typeof(CompCauseGameCondition_PsychicSuppression), nameof(CompCauseGameCondition_PsychicSuppression.CompGetGizmosExtra), 0).SetDebugOnly(); // Change gender
            SyncMethod.Register(typeof(CompCauseGameCondition_TemperatureOffset), nameof(CompCauseGameCondition_TemperatureOffset.SetTemperatureOffset)).SetDebugOnly();
            SyncMethod.Register(typeof(CompDamageOnInterval), nameof(CompDamageOnInterval.Damage)).SetDebugOnly();
            SyncMethod.Lambda(typeof(CompDeepDrill), nameof(CompDeepDrill.CompGetGizmosExtra), 0).SetDebugOnly();
            SyncMethod.Lambda(typeof(CompDissolution), nameof(CompDissolution.CompGetGizmosExtra), 0).SetDebugOnly(); // Dissolution event
            SyncMethod.Lambda(typeof(CompDissolution), nameof(CompDissolution.CompGetGizmosExtra), 1).SetDebugOnly(); // Dissolution event until destroyed
            SyncMethod.Lambda(typeof(CompDissolution), nameof(CompDissolution.CompGetGizmosExtra), 2).SetDebugOnly(); // Dissolution progress +25%
            SyncMethod.Lambda(typeof(CompEggContainer), nameof(CompEggContainer.CompGetGizmosExtra), 0).SetDebugOnly(); // Fill with eggs
            SyncMethod.Lambda(typeof(CompExplosive), nameof(CompExplosive.CompGetGizmosExtra), 0).SetDebugOnly(); // Trigger countdown
            SyncMethod.Lambda(typeof(CompHackable), nameof(CompHackable.CompGetGizmosExtra), 0).SetDebugOnly(); // Hack +10%
            SyncMethod.Lambda(typeof(CompHackable), nameof(CompHackable.CompGetGizmosExtra), 1).SetDebugOnly(); // Complete hack
            SyncMethod.Register(typeof(CompPolluteOverTime), nameof(CompPolluteOverTime.Pollute)).SetDebugOnly();
            SyncMethod.Register(typeof(CompPollutionPump), nameof(CompPollutionPump.Pump)).SetDebugOnly();
            SyncMethod.Lambda(typeof(CompProjectileInterceptor), nameof(CompProjectileInterceptor.CompGetGizmosExtra), 0).SetDebugOnly(); // Reset cooldown
            SyncMethod.Lambda(typeof(CompProjectileInterceptor), nameof(CompProjectileInterceptor.CompGetGizmosExtra), 2).SetDebugOnly(); // Toggle intercept non-hostile
            SyncMethod.Lambda(typeof(CompReloadable), nameof(CompReloadable.CompGetWornGizmosExtra), 0).SetDebugOnly(); // Reload to full
            SyncMethod.Lambda(typeof(CompScanner), nameof(CompScanner.CompGetGizmosExtra), 0).SetDebugOnly(); // Find now
            SyncMethod.Lambda(typeof(CompTerrainPump), nameof(CompTerrainPump.CompGetGizmosExtra), 0).SetDebugOnly(); // Progress 1 day
            SyncMethod.Register(typeof(CompToxifier), nameof(CompToxifier.PolluteNextCell)).SetDebugOnly();
            SyncMethod.Lambda(typeof(MinifiedTree), nameof(MinifiedThing.GetGizmos), 0).SetDebugOnly(); // Destroy
            SyncMethod.Lambda(typeof(MinifiedTree), nameof(MinifiedThing.GetGizmos), 1).SetDebugOnly(); // Die in 1 hour
            SyncMethod.Lambda(typeof(MinifiedTree), nameof(MinifiedThing.GetGizmos), 2).SetDebugOnly(); // Die in 1 day

            SyncMethod.Register(typeof(Blueprint_Build), nameof(Blueprint_Build.ChangeStyleOfAllSelected)).SetContext(SyncContext.MapSelected);
            SyncMethod.Lambda(typeof(CompTurretGun), nameof(CompTurretGun.CompGetGizmosExtra), 1); // Toggle fire at will

            // Gene Assembler
            SyncMethod.Register(typeof(Building_GeneAssembler), nameof(Building_GeneAssembler.Start));
            SyncMethod.Register(typeof(Building_GeneAssembler), nameof(Building_GeneAssembler.Reset));
            SyncMethod.Register(typeof(Building_GeneAssembler), nameof(Building_GeneAssembler.Finish)).SetDebugOnly();

            // Gene Extractor
            SyncMethod.Register(typeof(Building_GeneExtractor), nameof(Building_GeneExtractor.Cancel));
            SyncMethod.Lambda(typeof(Building_GeneExtractor), nameof(Building_GeneExtractor.GetGizmos), 2); // Cancel load
            SyncMethod.Register(typeof(Building_GeneExtractor), nameof(Building_GeneExtractor.Finish)).SetDebugOnly();

            // Growth Vat
            SyncMethod.Lambda(typeof(Building_GrowthVat), nameof(Building_GrowthVat.GetGizmos), 1); // Cancel growth
            SyncMethod.Lambda(typeof(Building_GrowthVat), nameof(Building_GrowthVat.GetGizmos), 6); // Cancel load
            SyncMethod.Register(typeof(Building_GrowthVat), nameof(Building_GrowthVat.SelectEmbryo));
            SyncMethod.Lambda(typeof(Building_GrowthVat), nameof(Building_GrowthVat.GetGizmos), 3).SetDebugOnly(); // Advance 1 year
            SyncMethod.Lambda(typeof(Building_GrowthVat), nameof(Building_GrowthVat.GetGizmos), 4).SetDebugOnly(); // Advance gestation 1 day
            SyncMethod.Lambda(typeof(Building_GrowthVat), nameof(Building_GrowthVat.GetGizmos), 5).SetDebugOnly(); // Embryo birth now
            SyncMethod.Lambda(typeof(Building_GrowthVat), nameof(Building_GrowthVat.GetGizmos), 11).SetDebugOnly(); // Fill nutrition
            SyncMethod.Lambda(typeof(Building_GrowthVat), nameof(Building_GrowthVat.GetGizmos), 12).SetDebugOnly(); // Empty nutrition
            SyncMethod.Register(typeof(Hediff_VatLearning), nameof(Hediff_VatLearning.Learn)).SetDebugOnly(); // Called by Building_GrowthVat gizmo

            // Subcore Scanner
            SyncMethod.Lambda(typeof(Building_SubcoreScanner), nameof(Building_SubcoreScanner.GetGizmos), 1); // Initialize
            SyncMethod.Register(typeof(Building_SubcoreScanner), nameof(Building_SubcoreScanner.EjectContents)); // Cancel load
            SyncMethod.Lambda(typeof(Building_SubcoreScanner), nameof(Building_SubcoreScanner.GetGizmos), 6).SetDebugOnly(); // Enable/disable ingredients
            SyncMethod.Lambda(typeof(Building_SubcoreScanner), nameof(Building_SubcoreScanner.GetGizmos), 7).SetDebugOnly(); // Complete

            // Mechs
            // Charger
            SyncMethod.Lambda(typeof(Building_MechCharger), nameof(Building_MechCharger.GetGizmos), 0).SetDebugOnly(); // Waste 100%
            SyncMethod.Lambda(typeof(Building_MechCharger), nameof(Building_MechCharger.GetGizmos), 1).SetDebugOnly(); // Waste 25%
            SyncMethod.Lambda(typeof(Building_MechCharger), nameof(Building_MechCharger.GetGizmos), 2).SetDebugOnly(); // Waste 0%
            SyncMethod.Register(typeof(Building_MechCharger), nameof(Building_MechCharger.GenerateWastePack)).SetDebugOnly(); // Generate waste, lambdaOrdinal: 3
            SyncMethod.Lambda(typeof(Building_MechCharger), nameof(Building_MechCharger.GetGizmos), 4).SetDebugOnly(); // Charge 100%
            // Gestator
            SyncMethod.Lambda(typeof(Building_MechGestator), nameof(Building_MechGestator.GetGizmos), 0).SetDebugOnly(); // Generate 5 waste
            SyncMethod.Lambda(typeof(Building_MechGestator), nameof(Building_MechGestator.GetGizmos), 1).SetDebugOnly(); // Forming cycle +25%
            SyncMethod.Lambda(typeof(Building_MechGestator), nameof(Building_MechGestator.GetGizmos), 2).SetDebugOnly(); // Complete cycle
            SyncMethod.Register(typeof(Bill_Mech), nameof(Bill_Mech.ForceCompleteAllCycles)).SetDebugOnly(); // Called from Building_MechGestator.GetGizmos
            // Carrier
            SyncMethod.Register(typeof(CompMechCarrier), nameof(CompMechCarrier.TrySpawnPawns));
            SyncMethod.Lambda(typeof(CompMechCarrier), nameof(CompMechCarrier.CompGetGizmosExtra), 2).SetDebugOnly(); // Reset cooldown
            // Power Cell
            SyncMethod.Lambda(typeof(CompMechPowerCell), nameof(CompMechPowerCell.CompGetGizmosExtra), 0).SetDebugOnly(); // Power left 0%
            SyncMethod.Lambda(typeof(CompMechPowerCell), nameof(CompMechPowerCell.CompGetGizmosExtra), 1).SetDebugOnly(); // Power left 100%
            // Repairable
            SyncMethod.Lambda(typeof(CompMechRepairable), nameof(CompMechRepairable.CompGetGizmosExtra), 1); // Toggle auto repair

            // Atomizer
            SyncMethod.Lambda(typeof(CompAtomizer), nameof(CompAtomizer.CompGetGizmosExtra), 1); // Auto load
            SyncMethod.Register(typeof(CompAtomizer), nameof(CompAtomizer.EjectContents));
            SyncMethod.Register(typeof(CompAtomizer), nameof(CompAtomizer.DoAtomize)).SetDebugOnly();

            // Genepack
            SyncMethod.Lambda(typeof(Genepack), nameof(Genepack.GetGizmos), 1); // Auto load

            // Genepack Container
            SyncMethod.Register(typeof(CompGenepackContainer), nameof(CompGenepackContainer.EjectContents));
            SyncMethod.Lambda(typeof(CompGenepackContainer), nameof(CompGenepackContainer.CompGetGizmosExtra), 1).SetDebugOnly(); // Fill with new packs

            // Deathrest Gene
            SyncMethod.Register(typeof(Gene_Deathrest), nameof(Gene_Deathrest.Wake));
            SyncMethod.Lambda(typeof(Gene_Deathrest), nameof(Gene_Deathrest.GetGizmos), 2); // Auto wake
            SyncMethod.Lambda(typeof(Gene_Deathrest), nameof(Gene_Deathrest.GetGizmos), 3).SetDebugOnly(); // Wake and apply bonuses

            // Baby feeding
            SyncMethod.Register(typeof(Pawn_MindState), nameof(Pawn_MindState.SetAutofeeder)); // Called from ITab_Pawn_Feeding.GenerateFloatMenuOption

            // HealthCardUtility
            // Previously we synced the delegate which created the bill, but it has side effects to it.
            // It can display confirmation like royal implant (no longer used?) or implanting IUD (if it would terminate pregnancy).
            // On top of that, in case of implanting the Xenogerm recipe, it will open a dialog with list of available options.
            SyncMethod.Register(typeof(HealthCardUtility), nameof(HealthCardUtility.CreateSurgeryBill));
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
            // If Pawn_JobTracker.TryTakeOrderedJob is synced directly, JobMaker.MakeJob for its job is called in interface code (outside of a synced method)
            // UniqueIDs assigned in the interface are always negative and specific to a client
            // This assigns the job a proper id after the code is synced and no longer in the interface (Multiplayer.ExecutingCmds is true)

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
        static void FixIdeoAfterCopy(Ideo __instance, Ideo ideo)
        {
            if (Multiplayer.ExecutingCmds)
            {
                // Fix ids for precepts generated by the fluid ideo reforming UI
                foreach (var precept in ideo.PreceptsListForReading)
                    if (precept.ID < 0)
                        precept.ID = Find.UniqueIDsManager.GetNextPreceptID();

                ideo.development.ideo = ideo;
                ideo.style.ideo = ideo;
            }
        }

        // Syncing the method directly would end up trying to sync it every single tick, as the method is called for every single ThingDef.
        // This will only call the synced method only if there is any need for it in the first place.
        [MpPrefix(typeof(Pawn_FoodRestrictionTracker), nameof(Pawn_FoodRestrictionTracker.SetBabyFoodAllowed))]
        static bool PreSetBabyFoodAllowed(Pawn_FoodRestrictionTracker __instance, ThingDef food, bool allowed)
        {
            // Let the method run normally if not in MP or executing commands
            if (Multiplayer.Client == null || Multiplayer.ExecutingCmds)
                return true;

            // Ignore if def is not a baby food, as there's no point to let the method run
            if (!ITab_Pawn_Feeding.BabyConsumableFoods.Contains(food))
                return false;

            // Sync the call if the method would do anything: allowed baby foods not set up, the def is not in the dictionary, or the value is changed
            if (__instance.allowedBabyFoodTypes == null || !__instance.allowedBabyFoodTypes.TryGetValue(food, out var current) || current != allowed)
                SyncedSetBabyFoodAllowed(__instance, food, allowed);

            // Don't let the method run, as we'll call it through synced method - also ignore setting the value if it wasn't changed at all
            return false;
        }

        [SyncMethod]
        static void SyncedSetBabyFoodAllowed(Pawn_FoodRestrictionTracker tracker, ThingDef food, bool allowed)
            => tracker.SetBabyFoodAllowed(food, allowed);

        [MpPrefix(typeof(ITab_ContentsGenepackHolder), nameof(ITab_ContentsGenepackHolder.DoRow))]
        static void PreGenepackHolderDoRow(Genepack genepack, CompGenepackContainer container, bool insideContainer, ref int __state)
        {
            // Checkbox to autoload only displayed if it's not inside of the container already
            if (Multiplayer.Client == null || insideContainer)
                return;

            __state = container.leftToLoad.IndexOf(genepack);
        }

        [MpPostfix(typeof(ITab_ContentsGenepackHolder), nameof(ITab_ContentsGenepackHolder.DoRow))]
        static void PostGenepackHolderDoRow(Genepack genepack, CompGenepackContainer container, bool insideContainer, int __state)
        {
            if (Multiplayer.Client == null || insideContainer)
                return;

            var listPosition = container.leftToLoad.IndexOf(genepack);

            // No change in state, do nothing
            if (listPosition == __state)
                return;

            if (listPosition >= 0)
            {
                genepack.targetContainer = null;
                container.leftToLoad.Remove(genepack);
                SyncDesiredGenepackState(genepack, container, true);
            }
            else
            {
                genepack.targetContainer = container.parent;
                container.leftToLoad.Insert(__state, genepack); // Keep the order
                SyncDesiredGenepackState(genepack, container, false);
            }
        }

        [SyncMethod]
        static void SyncDesiredGenepackState(Genepack genepack, CompGenepackContainer container, bool shouldLoad)
        {
            if (shouldLoad)
            {
                if (container.CanLoadMore)
                {
                    genepack.targetContainer = container.parent;
                    container.leftToLoad.Add(genepack);
                }
            }
            else
            {
                genepack.targetContainer = null;
                container.leftToLoad.Remove(genepack);
            }
        }
    }

}
