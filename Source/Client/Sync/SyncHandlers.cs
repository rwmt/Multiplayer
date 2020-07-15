using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;
using Verse.AI;
using static Verse.Widgets;

namespace Multiplayer.Client
{
    public static class SyncHandlers
    {
        public static void Init()
        {
            RuntimeHelpers.RunClassConstructor(typeof(SyncPatches).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(SyncFieldsPatches).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(SyncDelegates).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(SyncThingFilters).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(SyncActions).TypeHandle);
            //RuntimeHelpers.RunClassConstructor(typeof(SyncResearch).TypeHandle);

            Sync.ApplyWatchFieldPatches(typeof(SyncFieldsPatches));
        }
    }

    public static class SyncFieldsPatches
    {
        public static ISyncField SyncMedCare = Sync.Field(typeof(Pawn), "playerSettings", "medCare");
        public static ISyncField SyncSelfTend = Sync.Field(typeof(Pawn), "playerSettings", "selfTend");
        public static ISyncField SyncHostilityResponse = Sync.Field(typeof(Pawn), "playerSettings", "hostilityResponse");
        public static ISyncField SyncFollowDrafted = Sync.Field(typeof(Pawn), "playerSettings", "followDrafted");
        public static ISyncField SyncFollowFieldwork = Sync.Field(typeof(Pawn), "playerSettings", "followFieldwork");
        public static ISyncField SyncInteractionMode = Sync.Field(typeof(Pawn), "guest", "interactionMode");
        public static ISyncField SyncBeCarried = Sync.Field(typeof(Pawn), "health", "beCarriedByCaravanIfSick");
        public static ISyncField SyncPsychicEntropyLimit = Sync.Field(typeof(Pawn), "psychicEntropy", "limitEntropyAmount");
        public static ISyncField SyncPsychicEntropyTargetFocus = Sync.Field(typeof(Pawn), "psychicEntropy", "targetPsyfocus").SetBufferChanges();

        public static ISyncField SyncGodMode = Sync.Field(null, "Verse.DebugSettings/godMode").SetDebugOnly();
        public static ISyncField SyncResearchProject = Sync.Field(null, "Verse.Find/ResearchManager/currentProj");
        public static ISyncField SyncUseWorkPriorities = Sync.Field(null, "Verse.Current/Game/playSettings", "useWorkPriorities").PostApply(UseWorkPriorities_PostApply);
        public static ISyncField SyncAutoHomeArea = Sync.Field(null, "Verse.Current/Game/playSettings", "autoHomeArea");
        public static ISyncField SyncAutoRebuild = Sync.Field(null, "Verse.Current/Game/playSettings", "autoRebuild");
        public static SyncField[] SyncDefaultCare = Sync.Fields(
            null,
            "Verse.Current/Game/playSettings",
            "defaultCareForColonyHumanlike",
            "defaultCareForColonyPrisoner",
            "defaultCareForColonyAnimal",
            "defaultCareForNeutralAnimal",
            "defaultCareForNeutralFaction",
            "defaultCareForHostileFaction"
        ).SetBufferChanges();

        public static ISyncField SyncQuestDismissed = Sync.Field(typeof(Quest), nameof(Quest.dismissed));
        public static ISyncField SyncFactionAcceptRoyalFavor = Sync.Field(typeof(Faction), nameof(Faction.allowRoyalFavorRewards));
        public static ISyncField SyncFactionAcceptGoodwill = Sync.Field(typeof(Faction), nameof(Faction.allowGoodwillRewards));

        public static SyncField[] SyncThingFilterHitPoints =
            Sync.FieldMultiTarget(Sync.thingFilterTarget, "AllowedHitPointsPercents").SetBufferChanges();

        public static SyncField[] SyncThingFilterQuality =
            Sync.FieldMultiTarget(Sync.thingFilterTarget, "AllowedQualityLevels").SetBufferChanges();

        public static ISyncField SyncBillSuspended = Sync.Field(typeof(Bill), "suspended");
        public static ISyncField SyncIngredientSearchRadius = Sync.Field(typeof(Bill), "ingredientSearchRadius").SetBufferChanges();
        public static ISyncField SyncBillSkillRange = Sync.Field(typeof(Bill), "allowedSkillRange").SetBufferChanges();

        public static ISyncField SyncBillIncludeZone = Sync.Field(typeof(Bill_Production), "includeFromZone");
        public static ISyncField SyncBillIncludeHpRange = Sync.Field(typeof(Bill_Production), "hpRange").SetBufferChanges();
        public static ISyncField SyncBillIncludeQualityRange = Sync.Field(typeof(Bill_Production), "qualityRange").SetBufferChanges();
        public static ISyncField SyncBillPawnRestriction = Sync.Field(typeof(Bill), "pawnRestriction");

        public static ISyncField SyncZoneLabel = Sync.Field(typeof(Zone), "label");

        public static SyncField[] SyncBillProduction = Sync.Fields(
            typeof(Bill_Production),
            null,
            "repeatMode",
            "repeatCount",
            "targetCount",
            "pauseWhenSatisfied",
            "unpauseWhenYouHave"
        );

        public static SyncField[] SyncBillIncludeCriteria = Sync.Fields(
            typeof(Bill_Production),
            null,
            "includeEquipped",
            "includeTainted",
            "limitToAllowedStuff"
        );

        public static SyncField[] SyncDrugPolicyEntry = Sync.Fields(
            typeof(DrugPolicy),
            "entriesInt/[]",
            "allowedForAddiction",
            "allowedForJoy",
            "allowScheduled",
            "takeToInventory"
        );

        public static SyncField[] SyncDrugPolicyEntryBuffered = Sync.Fields(
            typeof(DrugPolicy),
            "entriesInt/[]",
            "daysFrequency",
            "onlyIfMoodBelow",
            "onlyIfJoyBelow"
        ).SetBufferChanges();

        public static ISyncField SyncTradeableCount = Sync.Field(typeof(MpTransferableReference), "CountToTransfer").SetBufferChanges().PostApply(TransferableCount_PostApply);

        // 1
        public static ISyncField SyncBillPaused = Sync.Field(typeof(Bill_Production), nameof(Bill_Production.paused)).SetBufferChanges().SetVersion(1);

        // 2
        public static ISyncField SyncOutfitLabel = Sync.Field(typeof(Outfit), "label").SetBufferChanges().SetVersion(2);
        public static ISyncField SyncDrugPolicyLabel = Sync.Field(typeof(DrugPolicy), "label").SetBufferChanges().SetVersion(2);
        public static ISyncField SyncFoodRestrictionLabel = Sync.Field(typeof(FoodRestriction), "label").SetBufferChanges().SetVersion(2);
        public static ISyncField SyncStorytellerDef = Sync.Field(typeof(Storyteller), "def").SetHostOnly().PostApply(StorytellerDef_Post).SetVersion(2);
        public static ISyncField SyncStorytellerDifficulty = Sync.Field(typeof(Storyteller), "difficulty").SetHostOnly().PostApply(StorytellerDifficutly_Post).SetVersion(2);

        [MpPrefix(typeof(StorytellerUI), nameof(StorytellerUI.DrawStorytellerSelectionInterface))]
        static void ChangeStoryteller()
        {
            SyncStorytellerDef.Watch(Find.Storyteller);
            SyncStorytellerDifficulty.Watch(Find.Storyteller);
        }

        static void StorytellerDef_Post(object target, object value)
        {
            Find.Storyteller.Notify_DefChanged();

            foreach (var comp in Multiplayer.game.asyncTimeComps)
            {
                comp.storyteller.def = Find.Storyteller.def;
                comp.storyteller.Notify_DefChanged();
            }
        }

        static void StorytellerDifficutly_Post(object target, object value)
        {
            foreach (var comp in Multiplayer.game.asyncTimeComps)
                comp.storyteller.difficulty = Find.Storyteller.difficulty;
        }

        [MpPrefix(typeof(HealthCardUtility), "DrawOverviewTab")]
        static void HealthCardUtility(Pawn pawn)
        {
            if (pawn.playerSettings != null)
            {
                SyncMedCare.Watch(pawn);
                SyncSelfTend.Watch(pawn);
            }
        }

        [MpPrefix(typeof(ITab_Pawn_Visitor), "FillTab")]
        static void ITab_Pawn_Visitor(ITab __instance)
        {
            Pawn pawn = __instance.SelPawn;
            SyncMedCare.Watch(pawn);
            SyncInteractionMode.Watch(pawn);
        }

        [MpPrefix(typeof(MainTabWindow_Quests), nameof(MainTabWindow_Quests.DoDismissButton))]
        static void MainTabWindow_Quests__DoDismissButtonPatch(Quest ___selected)
        {
            SyncQuestDismissed.Watch(___selected);
        }

        [MpPrefix(typeof(Dialog_RewardPrefsConfig), nameof(Dialog_RewardPrefsConfig.DoWindowContents))]
        static void Dialog_RewardPrefsConfigPatches()
        {
            IEnumerable<Faction> visibleInViewOrder = Find.FactionManager.AllFactionsVisibleInViewOrder;
            foreach (Faction faction in visibleInViewOrder) {
                SyncFactionAcceptRoyalFavor.Watch(faction);
                SyncFactionAcceptGoodwill.Watch(faction);
            }
        }

        [MpPostfix(typeof(Dialog_BillConfig), nameof(Dialog_BillConfig.GeneratePawnRestrictionOptions))]
        static IEnumerable<DropdownMenuElement<Pawn>> BillPawnRestrictions_Postfix(IEnumerable<DropdownMenuElement<Pawn>> __result, Bill ___bill)
        {
            return WatchDropdowns(() => SyncBillPawnRestriction.Watch(___bill), __result);
        }

        [MpPostfix(typeof(HostilityResponseModeUtility), nameof(HostilityResponseModeUtility.DrawResponseButton_GenerateMenu))]
        static IEnumerable<DropdownMenuElement<HostilityResponseMode>> HostilityResponse_Postfix(IEnumerable<DropdownMenuElement<HostilityResponseMode>> __result, Pawn p)
        {
            return WatchDropdowns(() => SyncHostilityResponse.Watch(p), __result);
        }

        [MpPostfix(typeof(MedicalCareUtility), nameof(MedicalCareUtility.MedicalCareSelectButton_GenerateMenu))]
        static IEnumerable<DropdownMenuElement<MedicalCareCategory>> MedicalCare_Postfix(IEnumerable<DropdownMenuElement<MedicalCareCategory>> __result, Pawn p)
        {
            return WatchDropdowns(() => SyncMedCare.Watch(p), __result);
        }

        [MpPostfix(typeof(Dialog_BillConfig), nameof(Dialog_BillConfig.GenerateStockpileInclusion))]
        static IEnumerable<DropdownMenuElement<Zone_Stockpile>> BillIncludeZone_Postfix(IEnumerable<DropdownMenuElement<Zone_Stockpile>> __result, Bill ___bill)
        {
            return WatchDropdowns(() => SyncBillIncludeZone.Watch(___bill), __result);
        }

        [MpPrefix(typeof(TrainingCardUtility), nameof(TrainingCardUtility.DrawTrainingCard))]
        static void PawnSettingFollowWatch(Pawn pawn)
        {
            SyncFollowDrafted.Watch(pawn);
            SyncFollowFieldwork.Watch(pawn);
        }

        [MpPrefix(typeof(Dialog_MedicalDefaults), "DoWindowContents")]
        static void MedicalDefaults()
        {
            SyncDefaultCare.Watch();
        }

        [MpPrefix(typeof(PsychicEntropyGizmo), nameof(PsychicEntropyGizmo.GizmoOnGUI))]
        static void PsychicEntropyLimiterToggle(PsychicEntropyGizmo __instance)
        {
            if (__instance?.tracker?.Pawn != null) {
                SyncPsychicEntropyLimit.Watch(__instance.tracker.Pawn);
                SyncPsychicEntropyTargetFocus.Watch(__instance.tracker.pawn);
            }
        }

        [MpPrefix(typeof(Widgets), "CheckboxLabeled")]
        static void CheckboxLabeled()
        {
            // Watched here to get reset asap and not trigger any side effects
            if (SyncMarkers.manualPriorities)
                SyncUseWorkPriorities.Watch();
        }

        [MpPrefix(typeof(PlaySettings), "DoPlaySettingsGlobalControls")]
        static void PlaySettingsControls()
        {
            SyncAutoHomeArea.Watch();
            SyncAutoRebuild.Watch();
        }

        [MpPrefix(typeof(ThingFilterUI), "DrawHitPointsFilterConfig")]
        static void ThingFilterHitPoints()
        {
            SyncThingFilterHitPoints.Watch(SyncMarkers.ThingFilterOwner);
        }

        [MpPrefix(typeof(ThingFilterUI), "DrawQualityFilterConfig")]
        static void ThingFilterQuality()
        {
            SyncThingFilterQuality.Watch(SyncMarkers.ThingFilterOwner);
        }

        [MpPrefix(typeof(Bill), "DoInterface")]
        static void BillInterfaceCard(Bill __instance)
        {
            SyncBillSuspended.Watch(__instance);
            SyncBillSkillRange.Watch(__instance);
            SyncIngredientSearchRadius.Watch(__instance);

            SyncBillProduction.Watch(__instance);
        }

        [MpPrefix(typeof(Dialog_BillConfig), "DoWindowContents")]
        static void DialogBillConfig(Dialog_BillConfig __instance)
        {
            Bill_Production bill = __instance.bill;

            SyncBillSuspended.Watch(bill);
            SyncBillSkillRange.Watch(bill);
            SyncIngredientSearchRadius.Watch(bill);

            SyncBillProduction.Watch(bill);

            if (bill.recipe.ProducedThingDef != null)
            {
                SyncBillIncludeCriteria.Watch(bill);
                SyncBillIncludeHpRange.Watch(bill);
                SyncBillIncludeQualityRange.Watch(bill);
            }
        }

        [MpPrefix(typeof(BillRepeatModeUtility), "<>c__DisplayClass0_0", "<MakeConfigFloatMenu>b__0")]
        [MpPrefix(typeof(BillRepeatModeUtility), "<>c__DisplayClass0_0", "<MakeConfigFloatMenu>b__1")]
        [MpPrefix(typeof(BillRepeatModeUtility), "<>c__DisplayClass0_0", "<MakeConfigFloatMenu>b__2")]
        static void BillRepeatMode(object __instance)
        {
            SyncBillProduction.Watch(__instance.GetPropertyOrField("bill"));
        }

        [MpPrefix(typeof(ITab_Bills), "TabUpdate")]
        static void BillIngredientSearchRadius(ITab_Bills __instance)
        {
            // Apply the buffered value for smooth rendering (doesn't actually have to sync anything here)
            if (__instance.mouseoverBill is Bill mouseover)
                SyncIngredientSearchRadius.Watch(mouseover);
        }

        [MpPrefix(typeof(Dialog_BillConfig), "WindowUpdate")]
        static void BillIngredientSearchRadius(Dialog_BillConfig __instance)
        {
            SyncIngredientSearchRadius.Watch(__instance.bill);
        }

        [MpPrefix(typeof(Dialog_ManageDrugPolicies), "DoPolicyConfigArea")]
        static void DialogManageDrugPolicies(Dialog_ManageDrugPolicies __instance)
        {
            DrugPolicy policy = __instance.SelectedPolicy;
            for (int i = 0; i < policy.Count; i++)
            {
                SyncDrugPolicyEntry.Watch(policy, i);
                SyncDrugPolicyEntryBuffered.Watch(policy, i);
            }
        }

        [MpPrefix(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.ToggleGodMode))]
        [MpPrefix(typeof(Prefs), "set_DevMode")]
        static void SetGodMode()
        {
            SyncGodMode.Watch();
        }

        [MpPrefix(typeof(MainTabWindow_Research), "DrawLeftRect")]
        static void ResearchTab()
        {
            SyncResearchProject.Watch();
        }

        [MpPrefix(typeof(Dialog_RenameZone), "SetName")]
        static void RenameZone(Dialog_RenameZone __instance)
        {
            SyncZoneLabel.Watch(__instance.zone);
        }

        [MpPrefix(typeof(TransferableUIUtility), "DoCountAdjustInterface")]
        static void TransferableAdjustTo(Transferable trad)
        {
            var session = MpTradeSession.current ??
                (Multiplayer.Client != null ? Multiplayer.WorldComp.splitSession : null) ??
                (ISessionWithTransferables)MpFormingCaravanWindow.drawing?.Session ??
                MpLoadTransportersWindow.drawing?.Session;
            if (session != null)
                SyncTradeableCount.Watch(new MpTransferableReference(session, trad));
        }

        [MpPrefix(typeof(WITab_Caravan_Health), nameof(WITab_Caravan_Health.DoRow), new[] { typeof(Rect), typeof(Pawn) })]
        static void CaravanHealthDoRow(Pawn p)
        {
            SyncBeCarried.Watch(p);
        }

        [MpPrefix(typeof(Bill), nameof(Bill.DoInterface))]
        [MpPrefix(typeof(Bill_Production), nameof(Bill_Production.ShouldDoNow))]
        static void WatchBillPaused(Bill __instance)
        {
            if (__instance is Bill_Production)
                SyncBillPaused.Watch(__instance);
        }

        [MpPrefix(typeof(Dialog_ManageOutfits), "DoNameInputRect")]
        [MpPrefix(typeof(Dialog_ManageDrugPolicies), "DoNameInputRect")]
        [MpPrefix(typeof(Dialog_ManageFoodRestrictions), "DoNameInputRect")]
        static void WatchPolicyLabels()
        {
            if (SyncMarkers.dialogOutfit != null)
                SyncOutfitLabel.Watch(SyncMarkers.dialogOutfit);

            if (SyncMarkers.drugPolicy != null)
                SyncDrugPolicyLabel.Watch(SyncMarkers.drugPolicy);

            if (SyncMarkers.foodRestriction != null)
                SyncFoodRestrictionLabel.Watch(SyncMarkers.foodRestriction);
        }

        static void UseWorkPriorities_PostApply(object target, object value)
        {
            // From MainTabWindow_Work.DoManualPrioritiesCheckbox
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                if (pawn.Faction == Faction.OfPlayer && pawn.workSettings != null)
                    pawn.workSettings.Notify_UseWorkPrioritiesChanged();
        }

        static void TransferableCount_PostApply(object target, object value)
        {
            var tr = (MpTransferableReference)target;
            if (tr != null)
                tr.session.Notify_CountChanged(tr.transferable);
        }

        static IEnumerable<DropdownMenuElement<T>> WatchDropdowns<T>(Action watchAction, IEnumerable<DropdownMenuElement<T>> dropdowns)
        {
            foreach (var entry in dropdowns)
            {
                if (entry.option.action != null)
                    entry.option.action = (Sync.FieldWatchPrefix + watchAction + entry.option.action + Sync.FieldWatchPostfix);
                yield return entry;
            }
        }
    }

    public class MpTransferableReference
    {
        public ISessionWithTransferables session;
        public Transferable transferable;

        public MpTransferableReference(ISessionWithTransferables session, Transferable transferable)
        {
            this.session = session;
            this.transferable = transferable;
        }

        public int CountToTransfer
        {
            get => transferable.CountToTransfer;
            set => transferable.CountToTransfer = value;
        }

        public override int GetHashCode() => transferable.GetHashCode();
        public override bool Equals(object obj) => obj is MpTransferableReference tr && tr.transferable == transferable;
    }

    public interface ISessionWithTransferables
    {
        int SessionId { get; }

        Transferable GetTransferableByThingId(int thingId);

        void Notify_CountChanged(Transferable tr);
    }

    public static class SyncPatches
    {
        static SyncPatches()
        {
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
                void RegisterIfDeclaredByType(Type type, string methodName)
                {
                    var assign = AccessTools.Method(type, methodName);
                    if (assign.DeclaringType == type) {
                        Sync.RegisterSyncMethod(assign).CancelIfAnyArgNull();
                    }
                }

                foreach (var type in typeof(CompAssignableToPawn).AllSubtypesAndSelf()) {
                    RegisterIfDeclaredByType(type, nameof(CompAssignableToPawn.TryAssignPawn));
                    RegisterIfDeclaredByType(type, nameof(CompAssignableToPawn.TryUnassignPawn));
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

            SyncMethod.Register(typeof(Building_OrbitalTradeBeacon), nameof(Building_OrbitalTradeBeacon.MakeMatchingStockpile));
            SyncMethod.Register(typeof(Building_SunLamp), nameof(Building_SunLamp.MakeMatchingGrowZone));
            SyncMethod.Register(typeof(Building_ShipComputerCore), nameof(Building_ShipComputerCore.TryLaunch));
            SyncMethod.Register(typeof(CompPower), nameof(CompPower.TryManualReconnect));
            SyncMethod.Register(typeof(CompTempControl), nameof(CompTempControl.InterfaceChangeTargetTemperature));
            SyncMethod.Register(typeof(CompTransporter), nameof(CompTransporter.CancelLoad), new SyncType[0]);
            SyncMethod.Register(typeof(StorageSettings), nameof(StorageSettings.CopyFrom)).ExposeParameter(0);
            SyncMethod.Register(typeof(Command_SetTargetFuelLevel), "<ProcessInput>b__2_2"); // Set target fuel level from Dialog_Slider
            SyncMethod.Register(typeof(ITab_Pawn_Gear), nameof(ITab_Pawn_Gear.InterfaceDrop)).SetContext(SyncContext.MapSelected | SyncContext.QueueOrder_Down).CancelIfAnyArgNull().CancelIfNoSelectedMapObjects();
            SyncMethod.Register(typeof(ITab_Pawn_Gear), nameof(ITab_Pawn_Gear.InterfaceIngest)).SetContext(SyncContext.MapSelected | SyncContext.QueueOrder_Down).CancelIfAnyArgNull().CancelIfNoSelectedMapObjects();

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

            SyncMethod.Register(typeof(Command_Ability), nameof(Command_Ability.ProcessInput)); // self cast psychic abilities
            SyncMethod.Register(typeof(Verb_CastAbility), nameof(Verb_CastAbility.OrderForceTarget)); // single target Psychic abilities
            SyncMethod.Register(typeof(CompAbilityEffect_WithDest), nameof(CompAbilityEffect_WithDest.OrderForceTarget)); // Psychic abilities with a source + destination, such as Skip (warp enemy to target place)
            SyncMethod.Register(typeof(RoyalTitlePermitWorker_CallAid), nameof(RoyalTitlePermitWorker_CallAid.CallAid_NewTemp)).CancelIfAnyArgNull();
            SyncMethod.Register(typeof(CompAbilityEffect_StartSpeech), nameof(CompAbilityEffect_StartSpeech.Apply)); // Royal Pawn: Give Speech button

            // 1
            SyncMethod.Register(typeof(TradeRequestComp), nameof(TradeRequestComp.Fulfill)).CancelIfAnyArgNull().SetVersion(1);

            // 2
            SyncMethod.Register(typeof(CompLaunchable), nameof(CompLaunchable.TryLaunch)).ExposeParameter(1).SetVersion(2);
            SyncMethod.Register(typeof(OutfitForcedHandler), nameof(OutfitForcedHandler.Reset)).SetVersion(2);
            SyncMethod.Register(typeof(Pawn_StoryTracker), nameof(Pawn_StoryTracker.Title)).SetVersion(2);

            // 3
            SyncMethod.Register(typeof(ShipUtility), nameof(ShipUtility.StartupHibernatingParts)).CancelIfAnyArgNull().SetVersion(3);
        }

        static SyncField SyncTimetable = Sync.Field(typeof(Pawn), "timetable", "times");

        [MpPrefix(typeof(PawnColumnWorker_CopyPasteTimetable), nameof(PawnColumnWorker_CopyPasteTimetable.PasteTo))]
        static bool PastePawnTimetable(Pawn p)
        {
            return !SyncTimetable.DoSync(p, PawnColumnWorker_CopyPasteTimetable.clipboard);
        }

        [MpPrefix(typeof(StorageSettingsClipboard), nameof(StorageSettingsClipboard.Copy))]
        static void StorageSettingsClipboardCopy_Prefix() => Multiplayer.dontSync = true;

        [MpPostfix(typeof(StorageSettingsClipboard), nameof(StorageSettingsClipboard.Copy))]
        static void StorageSettingsClipboardCopy_Postfix() => Multiplayer.dontSync = false;

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

        [MpPrefix(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
        static void TryTakeOrderedJob_Prefix(Job job)
        {
            if (Multiplayer.ExecutingCmds && job.loadID < 0)
            {
                job.loadID = Find.UniqueIDsManager.GetNextJobID();

                if (job.def == JobDefOf.TradeWithPawn && TickPatch.currentExecutingCmdIssuedBySelf)
                    ShowTradingWindow.tradeJobStartedByMe = job.loadID;
            }
        }

        [MpPrefix(typeof(BillStack), nameof(BillStack.AddBill))]
        static void AddBill_Prefix(Bill bill)
        {
            if (Multiplayer.ExecutingCmds && bill.loadID < 0)
                bill.loadID = Find.UniqueIDsManager.GetNextBillID();
        }
    }

    public static class SyncThingFilters
    {
        static SyncMethod[] SyncThingFilterAllowThing = Sync.MethodMultiTarget(Sync.thingFilterTarget, "SetAllow", new SyncType[] { typeof(ThingDef), typeof(bool) });
        static SyncMethod[] SyncThingFilterAllowSpecial = Sync.MethodMultiTarget(Sync.thingFilterTarget, "SetAllow", new SyncType[] { typeof(SpecialThingFilterDef), typeof(bool) });
        static SyncMethod[] SyncThingFilterAllowStuffCategory = Sync.MethodMultiTarget(Sync.thingFilterTarget, "SetAllow", new SyncType[] { typeof(StuffCategoryDef), typeof(bool) });

        [MpPrefix(typeof(ThingFilter), "SetAllow", new[] { typeof(StuffCategoryDef), typeof(bool) })]
        static bool ThingFilter_SetAllow(StuffCategoryDef cat, bool allow)
        {
            return !SyncThingFilterAllowStuffCategory.DoSync(SyncMarkers.ThingFilterOwner, cat, allow);
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", new[] { typeof(SpecialThingFilterDef), typeof(bool) })]
        static bool ThingFilter_SetAllow(SpecialThingFilterDef sfDef, bool allow)
        {
            return !SyncThingFilterAllowSpecial.DoSync(SyncMarkers.ThingFilterOwner, sfDef, allow);
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", new[] { typeof(ThingDef), typeof(bool) })]
        static bool ThingFilter_SetAllow(ThingDef thingDef, bool allow)
        {
            return !SyncThingFilterAllowThing.DoSync(SyncMarkers.ThingFilterOwner, thingDef, allow);
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", new[] { typeof(ThingCategoryDef), typeof(bool), typeof(IEnumerable<ThingDef>), typeof(IEnumerable<SpecialThingFilterDef>) })]
        static bool ThingFilter_SetAllow(ThingCategoryDef categoryDef, bool allow)
        {
            if (!Multiplayer.ShouldSync || SyncMarkers.ThingFilterOwner == null) return true;

            if (SyncMarkers.tabStorage != null)
                ThingFilter_AllowCategory_HelperStorage(SyncMarkers.tabStorage, categoryDef, allow);
            else if (SyncMarkers.billConfig != null)
                ThingFilter_AllowCategory_HelperBill(SyncMarkers.billConfig, categoryDef, allow);
            else if (SyncMarkers.dialogOutfit != null)
                ThingFilter_AllowCategory_HelperOutfit(SyncMarkers.dialogOutfit, categoryDef, allow);
            else if (SyncMarkers.foodRestriction != null)
                ThingFilter_AllowCategory_HelperFood(SyncMarkers.foodRestriction, categoryDef, allow);

            return false;
        }

        [MpPrefix(typeof(ThingFilter), "SetDisallowAll")]
        static bool ThingFilter_SetDisallowAll()
        {
            if (!Multiplayer.ShouldSync || SyncMarkers.ThingFilterOwner == null) return true;

            if (SyncMarkers.tabStorage != null)
                ThingFilter_DisallowAll_HelperStorage(SyncMarkers.tabStorage);
            else if (SyncMarkers.billConfig != null)
                ThingFilter_DisallowAll_HelperBill(SyncMarkers.billConfig);
            else if (SyncMarkers.dialogOutfit != null)
                ThingFilter_DisallowAll_HelperOutfit(SyncMarkers.dialogOutfit);
            else if (SyncMarkers.foodRestriction != null)
                ThingFilter_DisallowAll_HelperFood(SyncMarkers.foodRestriction);

            return false;
        }

        [MpPrefix(typeof(ThingFilter), "SetAllowAll")]
        static bool ThingFilter_SetAllowAll()
        {
            if (!Multiplayer.ShouldSync || SyncMarkers.ThingFilterOwner == null) return true;

            if (SyncMarkers.tabStorage != null)
                ThingFilter_AllowAll_HelperStorage(SyncMarkers.tabStorage);
            else if (SyncMarkers.billConfig != null)
                ThingFilter_AllowAll_HelperBill(SyncMarkers.billConfig);
            else if (SyncMarkers.dialogOutfit != null)
                ThingFilter_AllowAll_HelperOutfit(SyncMarkers.dialogOutfit);
            else if (SyncMarkers.foodRestriction != null)
                ThingFilter_AllowAll_HelperFood(SyncMarkers.foodRestriction);

            return false;
        }

        private static IEnumerable<SpecialThingFilterDef> OutfitSpecialFilters => SpecialThingFilterDefOf.AllowNonDeadmansApparel.ToEnumerable();

        private static IEnumerable<SpecialThingFilterDef> FoodSpecialFilters => SpecialThingFilterDefOf.AllowFresh.ToEnumerable();

        [SyncMethod]
        static void ThingFilter_DisallowAll_HelperStorage(IStoreSettingsParent storage) => storage.GetStoreSettings().filter.SetDisallowAll(null, null);

        [SyncMethod]
        static void ThingFilter_DisallowAll_HelperBill(Bill bill) => bill.ingredientFilter.SetDisallowAll(null, bill.recipe.forceHiddenSpecialFilters);

        [SyncMethod]
        static void ThingFilter_DisallowAll_HelperOutfit(Outfit outfit) => outfit.filter.SetDisallowAll(null, OutfitSpecialFilters);

        [SyncMethod]
        static void ThingFilter_DisallowAll_HelperFood(FoodRestriction food) => food.filter.SetDisallowAll(null, FoodSpecialFilters);

        [SyncMethod]
        static void ThingFilter_AllowAll_HelperStorage(IStoreSettingsParent storage) => storage.GetStoreSettings().filter.SetAllowAll(storage.GetParentStoreSettings()?.filter);

        [SyncMethod]
        static void ThingFilter_AllowAll_HelperBill(Bill bill) => bill.ingredientFilter.SetAllowAll(bill.recipe.fixedIngredientFilter);

        [SyncMethod]
        static void ThingFilter_AllowAll_HelperOutfit(Outfit outfit) => outfit.filter.SetAllowAll(Dialog_ManageOutfits.apparelGlobalFilter);

        [SyncMethod]
        static void ThingFilter_AllowAll_HelperFood(FoodRestriction food) => food.filter.SetAllowAll(Dialog_ManageFoodRestrictions.foodGlobalFilter);

        [SyncMethod]
        static void ThingFilter_AllowCategory_HelperStorage(IStoreSettingsParent storage, ThingCategoryDef categoryDef, bool allow) => ThingFilter_AllowCategory_Helper(storage.GetStoreSettings().filter, categoryDef, allow, storage.GetParentStoreSettings()?.filter, null, null);

        [SyncMethod]
        static void ThingFilter_AllowCategory_HelperBill(Bill bill, ThingCategoryDef categoryDef, bool allow) => ThingFilter_AllowCategory_Helper(bill.ingredientFilter, categoryDef, allow, bill.recipe.fixedIngredientFilter, null, bill.recipe.forceHiddenSpecialFilters);

        [SyncMethod]
        static void ThingFilter_AllowCategory_HelperOutfit(Outfit outfit, ThingCategoryDef categoryDef, bool allow) => ThingFilter_AllowCategory_Helper(outfit.filter, categoryDef, allow, Dialog_ManageOutfits.apparelGlobalFilter, null, OutfitSpecialFilters);

        [SyncMethod]
        static void ThingFilter_AllowCategory_HelperFood(FoodRestriction food, ThingCategoryDef categoryDef, bool allow) => ThingFilter_AllowCategory_Helper(food.filter, categoryDef, allow, Dialog_ManageFoodRestrictions.foodGlobalFilter, null, FoodSpecialFilters);

        static void ThingFilter_AllowCategory_Helper(ThingFilter filter, ThingCategoryDef categoryDef, bool allow, ThingFilter parentfilter, IEnumerable<ThingDef> forceHiddenDefs, IEnumerable<SpecialThingFilterDef> forceHiddenFilters)
        {
            Listing_TreeThingFilter listing = new Listing_TreeThingFilter(filter, parentfilter, forceHiddenDefs, forceHiddenFilters, null);
            listing.CalculateHiddenSpecialFilters(new TreeNode_ThingCategory(categoryDef));
            filter.SetAllow(categoryDef, allow, forceHiddenDefs, listing.hiddenSpecialFilters);
        }
    }

    static class SyncActions
    {
        static SyncAction<FloatMenuOption, WorldObject, Caravan, object> SyncWorldObjCaravanMenus;
        static SyncAction<FloatMenuOption, WorldObject, IEnumerable<IThingHolder>, CompLaunchable> SyncTransportPodMenus;

        static SyncActions()
        {
            SyncWorldObjCaravanMenus = RegisterActions((WorldObject obj, Caravan c) => obj.GetFloatMenuOptions(c), o => ref o.action);
            SyncWorldObjCaravanMenus.PatchAll(nameof(WorldObject.GetFloatMenuOptions));

            SyncTransportPodMenus = RegisterActions((WorldObject obj, IEnumerable<IThingHolder> p, CompLaunchable r) => obj.GetTransportPodsFloatMenuOptions(p, r), o => ref o.action);
            SyncTransportPodMenus.PatchAll(nameof(WorldObject.GetTransportPodsFloatMenuOptions));
        }

        static SyncAction<T, A, B, object> RegisterActions<T, A, B>(Func<A, B, IEnumerable<T>> func, ActionGetter<T> actionGetter)
        {
            return RegisterActions<T, A, B, object>((a, b, c) => func(a, b), actionGetter);
        }

        static SyncAction<T, A, B, C> RegisterActions<T, A, B, C>(Func<A, B, C, IEnumerable<T>> func, ActionGetter<T> actionGetter)
        {
            var sync = new SyncAction<T, A, B, C>(func, actionGetter);
            Sync.handlers.Add(sync);

            return sync;
        }

        public static Dictionary<MethodBase, ISyncAction> syncActions = new Dictionary<MethodBase, ISyncAction>();
        public static bool wantOriginal;
        private static bool syncingActions; // Prevents from running on base methods

        public static void SyncAction_Prefix(ref bool __state)
        {
            __state = syncingActions;
            syncingActions = true;
        }

        public static void SyncAction1_Postfix(object __instance, object __0, ref object __result, MethodBase __originalMethod, bool __state)
        {
            SyncAction2_Postfix(__instance, __0, null, ref __result, __originalMethod, __state);
        }

        public static void SyncAction2_Postfix(object __instance, object __0, object __1, ref object __result, MethodBase __originalMethod, bool __state)
        {
            if (!__state)
            {
                syncingActions = false;
                if (Multiplayer.ShouldSync && !wantOriginal && !syncingActions)
                    __result = syncActions[__originalMethod].DoSync(__instance, __0, __1);
            }
        }
    }

    public static class SyncDelegates
    {
        static SyncDelegates()
        {
            SyncContext mouseKeyContext = SyncContext.QueueOrder_Down | SyncContext.MapMouseCell;

            SyncDelegate.Register(typeof(FloatMenuMakerMap), "<>c__DisplayClass9_0", "<GotoLocationOption>b__0").CancelIfAnyFieldNull().SetContext(mouseKeyContext);  // Goto
            SyncDelegate.Register(typeof(FloatMenuMakerMap), "<>c__DisplayClass5_1", "<AddHumanlikeOrders>b__0").CancelIfAnyFieldNull().SetContext(mouseKeyContext);   // Arrest
            SyncDelegate.Register(typeof(FloatMenuMakerMap), "<>c__DisplayClass5_6", "<AddHumanlikeOrders>b__4").CancelIfAnyFieldNull().SetContext(mouseKeyContext);   // Rescue
            SyncDelegate.Register(typeof(FloatMenuMakerMap), "<>c__DisplayClass5_6", "<AddHumanlikeOrders>b__5").CancelIfAnyFieldNull().SetContext(mouseKeyContext);   // Capture
            SyncDelegate.Register(typeof(FloatMenuMakerMap), "<>c__DisplayClass5_8", "<AddHumanlikeOrders>b__6").CancelIfAnyFieldNull().SetContext(mouseKeyContext);   // Carry to cryptosleep casket

            SyncDelegate.Register(typeof(HealthCardUtility), "<>c__DisplayClass26_0", "<GenerateSurgeryOption>b__1").CancelIfAnyFieldNull(without: "part");      // Add medical bill
            SyncDelegate.Register(typeof(Command_SetPlantToGrow), "<>c__DisplayClass5_0", "<ProcessInput>b__2");                                                // Set plant to grow
            SyncDelegate.Register(typeof(Building_Bed), "<>c__DisplayClass39_0", "<ToggleForPrisonersByInterface>b__0").RemoveNullsFromLists("bedsToAffect");    // Toggle bed for prisoners
            SyncDelegate.Register(typeof(ITab_Bills), "<>c__DisplayClass10_0", "<FillTab>b__1").SetContext(SyncContext.MapSelected).CancelIfNoSelectedObjects(); // Add bill

            SyncDelegate.Register(typeof(CompLongRangeMineralScanner), "<>c__DisplayClass7_0", "<CompGetGizmosExtra>b__1").SetContext(SyncContext.MapSelected); // Select mineral to scan for

            SyncMethod.Register(typeof(CompFlickable), "<CompGetGizmosExtra>b__20_1"); // Toggle flick designation
            SyncMethod.Register(typeof(Pawn_PlayerSettings), "<GetGizmos>b__31_1");    // Toggle release animals
            SyncMethod.Register(typeof(Building_TurretGun), "<GetGizmos>b__59_2");     // Toggle turret hold fire
            SyncMethod.Register(typeof(Building_Trap), "<GetGizmos>b__23_1");          // Toggle trap auto-rearm
            SyncMethod.Register(typeof(Building_Door), "<GetGizmos>b__57_1");          // Toggle door hold open
            SyncMethod.Register(typeof(Zone_Growing), "<GetGizmos>b__13_1");           // Toggle zone allow sow

            SyncMethod.Register(typeof(PriorityWork), "<GetGizmos>b__17_0");                // Clear prioritized work
            SyncMethod.Register(typeof(Building_TurretGun), "<GetGizmos>b__59_1");          // Reset forced target
            SyncMethod.Register(typeof(UnfinishedThing), "<GetGizmos>b__27_0");             // Cancel unfinished thing
            SyncMethod.Register(typeof(CompTempControl), "<CompGetGizmosExtra>b__8_2");     // Reset temperature

            SyncDelegate.Register(typeof(CompTargetable), "<>c__DisplayClass6_0", "<SelectedUseOption>b__0");           // Use targetable

            SyncDelegate.Register(typeof(Designator), "<>c__DisplayClass30_1", "<get_RightClickFloatMenuOptions>b__0"); // Designate all
            SyncDelegate.Register(typeof(Designator), "<>c__DisplayClass30_2", "<get_RightClickFloatMenuOptions>b__1"); // Remove all designations

            SyncDelegate.Register(typeof(CaravanAbandonOrBanishUtility), "<>c__DisplayClass0_0", "<TryAbandonOrBanishViaInterface>b__1").CancelIfAnyFieldNull();      // Abandon caravan thing
            SyncDelegate.Register(typeof(CaravanAbandonOrBanishUtility), "<>c__DisplayClass1_0", "<TryAbandonOrBanishViaInterface>b__0").CancelIfAnyFieldNull();      // Abandon caravan transferable
            SyncDelegate.Register(typeof(CaravanAbandonOrBanishUtility), "<>c__DisplayClass2_0", "<TryAbandonSpecificCountViaInterface>b__0").CancelIfAnyFieldNull(); // Abandon thing specific count
            SyncDelegate.Register(typeof(CaravanAbandonOrBanishUtility), "<>c__DisplayClass3_0", "<TryAbandonSpecificCountViaInterface>b__0").CancelIfAnyFieldNull(); // Abandon transferable specific count

            SyncDelegate.Register(typeof(CaravanVisitUtility), "<>c__DisplayClass2_0", "<TradeCommand>b__0").CancelIfAnyFieldNull();     // Caravan trade with settlement
            SyncDelegate.Register(typeof(FactionGiftUtility), "<>c__DisplayClass1_0", "<OfferGiftsCommand>b__0").CancelIfAnyFieldNull(); // Caravan offer gifts

            SyncDelegate.Register(typeof(Building_Bed), "<>c__DisplayClass41_0", "<GetFloatMenuOptions>b__0").CancelIfAnyFieldNull(); // Use medical bed

            SyncMethod.Register(typeof(CompRefuelable), "<CompGetGizmosExtra>b__42_1"); // Toggle Auto-refuel
            SyncMethod.Register(typeof(CompRefuelable), "<CompGetGizmosExtra>b__42_2").SetDebugOnly(); // Set fuel to 0
            SyncMethod.Register(typeof(CompRefuelable), "<CompGetGizmosExtra>b__42_3").SetDebugOnly(); // Set fuel to 0.1
            SyncMethod.Register(typeof(CompRefuelable), "<CompGetGizmosExtra>b__42_4").SetDebugOnly(); // Set fuel to max

            SyncMethod.Register(typeof(CompShuttle), "<CompGetGizmosExtra>b__39_1"); // Toggle autoload
            SyncMethod.Register(typeof(CompShuttle), "<CompGetGizmosExtra>b__39_2"); // Send shuttle
            SyncMethod.Register(typeof(MonumentMarker), "<GetGizmos>b__28_1"); // Build Monument Quest - Monument Marker: cancel/remove marker
            SyncDelegate.Register(typeof(MonumentMarker), "<>c__DisplayClass28_0", "<GetGizmos>b__3"); // Build Monument Quest - Monument Marker: place blueprints
            SyncMethod.Register(typeof(MonumentMarker), "<GetGizmos>b__28_4"); // Build Monument Quest - Monument Marker: dev build all

            SyncDelegate.Register(typeof(ITab_ContentsTransporter), "<>c__DisplayClass7_0", "<DoItemsLists>b__0").SetContext(SyncContext.MapSelected); // Discard loaded thing
        }

        [MpPrefix(typeof(FormCaravanComp), "<>c__DisplayClass9_0", "<GetGizmos>b__0")]
        static bool GizmoFormCaravan(MapParent ___mapParent)
        {
            if (Multiplayer.Client == null) return true;
            GizmoFormCaravan(___mapParent.Map, false);
            return false;
        }

        [MpPrefix(typeof(FormCaravanComp), "<>c__DisplayClass9_0", "<GetGizmos>b__1")]
        static bool GizmoRefomCaravan(MapParent ___mapParent)
        {
            if (Multiplayer.Client == null) return true;
            GizmoFormCaravan(___mapParent.Map, true);
            return false;
        }

        private static void GizmoFormCaravan(Map map, bool reform)
        {
            var comp = map.MpComp();

            if (comp.caravanForming != null)
                comp.caravanForming.OpenWindow();
            else
                CreateCaravanFormingSession(comp, reform);
        }

        [SyncMethod]
        private static void CreateCaravanFormingSession(MultiplayerMapComp comp, bool reform)
        {
            comp.CreateCaravanFormingSession(reform, null, false);

            if (TickPatch.currentExecutingCmdIssuedBySelf)
            {
                MapAsyncTimeComp.keepTheMap = true;
                Current.Game.CurrentMap = comp.map;
                Find.World.renderer.wantedMode = WorldRenderMode.None;
            }
        }

        [MpPostfix(typeof(CaravanVisitUtility), nameof(CaravanVisitUtility.TradeCommand))]
        static void ReopenTradingWindowLocally(Caravan caravan, Command __result)
        {
            var original = ((Command_Action)__result).action;

            ((Command_Action)__result).action = () =>
            {
                if (Multiplayer.Client != null && Multiplayer.WorldComp.trading.Any(t => t.trader == CaravanVisitUtility.SettlementVisitedNow(caravan)))
                {
                    Find.WindowStack.Add(new TradingWindow());
                    return;
                }

                original();
            };
        }
    }

    public static class SyncMarkers
    {
        public static bool manualPriorities;
        public static bool researchToil;

        public static IStoreSettingsParent tabStorage;
        public static Bill billConfig;
        public static Outfit dialogOutfit;
        public static DrugPolicy drugPolicy;
        public static FoodRestriction foodRestriction;

        public static object ThingFilterOwner => tabStorage ?? billConfig ?? dialogOutfit ?? (object)foodRestriction;

        [MpPrefix(typeof(MainTabWindow_Work), "DoManualPrioritiesCheckbox")]
        static void ManualPriorities_Prefix() => manualPriorities = true;

        [MpPostfix(typeof(MainTabWindow_Work), "DoManualPrioritiesCheckbox")]
        static void ManualPriorities_Postfix() => manualPriorities = false;

        [MpPrefix(typeof(JobDriver_Research), "<>c__DisplayClass6_0", "<MakeNewToils>b__0")]
        static void ResearchToil_Prefix() => researchToil = true;

        [MpPostfix(typeof(JobDriver_Research), "<>c__DisplayClass6_0", "<MakeNewToils>b__0")]
        static void ResearchToil_Postfix() => researchToil = false;

        [MpPrefix(typeof(ITab_Storage), "FillTab")]
        static void TabStorageFillTab_Prefix(ITab_Storage __instance) => tabStorage = __instance.SelStoreSettingsParent;

        [MpPostfix(typeof(ITab_Storage), "FillTab")]
        static void TabStorageFillTab_Postfix() => tabStorage = null;

        [MpPrefix(typeof(Dialog_BillConfig), "DoWindowContents")]
        static void BillConfig_Prefix(Dialog_BillConfig __instance) => billConfig = __instance.bill;

        [MpPostfix(typeof(Dialog_BillConfig), "DoWindowContents")]
        static void BillConfig_Postfix() => billConfig = null;

        [MpPrefix(typeof(Dialog_ManageOutfits), "DoWindowContents")]
        static void ManageOutfit_Prefix(Dialog_ManageOutfits __instance) => dialogOutfit = __instance.SelectedOutfit;

        [MpPostfix(typeof(Dialog_ManageOutfits), "DoWindowContents")]
        static void ManageOutfit_Postfix() => dialogOutfit = null;

        [MpPrefix(typeof(Dialog_ManageDrugPolicies), "DoWindowContents")]
        static void ManageDrugPolicy_Prefix(Dialog_ManageDrugPolicies __instance) => drugPolicy = __instance.SelectedPolicy;

        [MpPostfix(typeof(Dialog_ManageOutfits), "DoWindowContents")]
        static void ManageDrugPolicy_Postfix() => drugPolicy = null;

        [MpPrefix(typeof(Dialog_ManageFoodRestrictions), "DoWindowContents")]
        static void ManageFoodRestriction_Prefix(Dialog_ManageFoodRestrictions __instance) => foodRestriction = __instance.SelectedFoodRestriction;

        [MpPostfix(typeof(Dialog_ManageFoodRestrictions), "DoWindowContents")]
        static void ManageFoodRestriction_Postfix() => foodRestriction = null;
    }

    // Currently unused
    public static class SyncResearch
    {
        private static Dictionary<int, float> localResearch = new Dictionary<int, float>();

        //[MpPrefix(typeof(ResearchManager), nameof(ResearchManager.ResearchPerformed))]
        static bool ResearchPerformed_Prefix(float amount, Pawn researcher)
        {
            if (Multiplayer.Client == null || !SyncMarkers.researchToil)
                return true;

            // todo only faction leader
            if (Faction.OfPlayer == Multiplayer.RealPlayerFaction)
            {
                float current = localResearch.GetValueSafe(researcher.thingIDNumber);
                localResearch[researcher.thingIDNumber] = current + amount;
            }

            return false;
        }

        // Set by faction context
        public static ResearchSpeed researchSpeed;
        public static ISyncField SyncResearchSpeed =
            Sync.Field(null, "Multiplayer.Client.SyncResearch/researchSpeed/[]").SetBufferChanges().InGameLoop();

        public static void ConstantTick()
        {
            if (localResearch.Count == 0) return;

            Sync.FieldWatchPrefix();

            foreach (int pawn in localResearch.Keys.ToList())
            {
                SyncResearchSpeed.Watch(null, pawn);
                researchSpeed[pawn] = localResearch[pawn];
                localResearch[pawn] = 0;
            }

            Sync.FieldWatchPostfix();
        }
    }

    public class ResearchSpeed : IExposable
    {
        public Dictionary<int, float> data = new Dictionary<int, float>();

        public float this[int pawnId]
        {
            get => data.TryGetValue(pawnId, out float speed) ? speed : 0f;

            set
            {
                if (value == 0) data.Remove(pawnId);
                else data[pawnId] = value;
            }
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref data, "data", LookMode.Value, LookMode.Value);
        }
    }

}
