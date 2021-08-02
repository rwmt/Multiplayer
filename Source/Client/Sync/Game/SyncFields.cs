using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using static Verse.Widgets;

namespace Multiplayer.Client
{
    public static class SyncFields
    {
        public static ISyncField SyncMedCare;
        public static ISyncField SyncSelfTend;
        public static ISyncField SyncHostilityResponse;
        public static ISyncField SyncFollowDrafted;
        public static ISyncField SyncFollowFieldwork;
        public static ISyncField SyncInteractionMode;
        public static ISyncField SyncSlaveInteractionMode;
        public static ISyncField SyncIdeoForConversion;
        public static ISyncField SyncBeCarried;
        public static ISyncField SyncPsychicEntropyLimit;
        public static ISyncField SyncPsychicEntropyTargetFocus;

        public static ISyncField SyncGodMode;
        public static ISyncField SyncResearchProject;
        public static ISyncField SyncUseWorkPriorities;
        public static ISyncField SyncAutoHomeArea;
        public static ISyncField SyncAutoRebuild;
        public static SyncField[] SyncDefaultCare;

        public static ISyncField SyncQuestDismissed;
        public static ISyncField SyncFactionAcceptRoyalFavor;
        public static ISyncField SyncFactionAcceptGoodwill;

        public static SyncField[] SyncThingFilterHitPoints;
        public static SyncField[] SyncThingFilterQuality;

        public static ISyncField SyncBillSuspended;
        public static ISyncField SyncIngredientSearchRadius;
        public static ISyncField SyncBillSkillRange;

        public static ISyncField SyncBillIncludeZone;
        public static ISyncField SyncBillIncludeHpRange;
        public static ISyncField SyncBillIncludeQualityRange;
        public static ISyncField SyncBillPawnRestriction;

        public static ISyncField SyncZoneLabel;

        public static SyncField[] SyncBillProduction;
        public static SyncField[] SyncBillIncludeCriteria;

        public static SyncField[] SyncDrugPolicyEntry;
        public static SyncField[] SyncDrugPolicyEntryBuffered;

        public static ISyncField SyncTradeableCount;

        // 1
        public static ISyncField SyncBillPaused;

        // 2
        public static ISyncField SyncOutfitLabel;
        public static ISyncField SyncDrugPolicyLabel;
        public static ISyncField SyncFoodRestrictionLabel;
        public static ISyncField SyncStorytellerDef;
        public static ISyncField SyncStorytellerDifficulty;

        public static ISyncField SyncAnimalPenAutocut;

        public static SyncField[] SyncAutoSlaughter;
		
        public static ISyncField SyncDryadCaste;
        public static ISyncField SyncDesiredTreeConnectionStrength;
        public static ISyncField SyncPlantableTargetCell;

        public static ISyncField SyncNeuralSuperchargerMode;

        public static void Init()
        {
            SyncMedCare = Sync.Field(typeof(Pawn), "playerSettings", "medCare");
            SyncSelfTend = Sync.Field(typeof(Pawn), "playerSettings", "selfTend");
            SyncHostilityResponse = Sync.Field(typeof(Pawn), "playerSettings", "hostilityResponse");
            SyncFollowDrafted = Sync.Field(typeof(Pawn), "playerSettings", "followDrafted");
            SyncFollowFieldwork = Sync.Field(typeof(Pawn), "playerSettings", "followFieldwork");
            SyncInteractionMode = Sync.Field(typeof(Pawn), "guest", "interactionMode");
            SyncSlaveInteractionMode = Sync.Field(typeof(Pawn), nameof(Pawn.guest), nameof(Pawn_GuestTracker.slaveInteractionMode));
            SyncIdeoForConversion = Sync.Field(typeof(Pawn), nameof(Pawn.guest), nameof(Pawn_GuestTracker.ideoForConversion));
            SyncBeCarried = Sync.Field(typeof(Pawn), "health", "beCarriedByCaravanIfSick");
            SyncPsychicEntropyLimit = Sync.Field(typeof(Pawn), "psychicEntropy", "limitEntropyAmount");
            SyncPsychicEntropyTargetFocus = Sync.Field(typeof(Pawn), "psychicEntropy", "targetPsyfocus").SetBufferChanges();

            SyncGodMode = Sync.Field(null, "Verse.DebugSettings/godMode").SetDebugOnly();
            SyncResearchProject = Sync.Field(null, "Verse.Find/ResearchManager/currentProj");
            SyncUseWorkPriorities = Sync.Field(null, "Verse.Current/Game/playSettings", "useWorkPriorities").PostApply(UseWorkPriorities_PostApply);
            SyncAutoHomeArea = Sync.Field(null, "Verse.Current/Game/playSettings", "autoHomeArea");
            SyncAutoRebuild = Sync.Field(null, "Verse.Current/Game/playSettings", "autoRebuild");

            SyncDefaultCare = Sync.Fields(
                null,
                "Verse.Current/Game/playSettings",
                "defaultCareForColonyHumanlike",
                "defaultCareForColonyPrisoner",
                "defaultCareForColonyAnimal",
                "defaultCareForNeutralAnimal",
                "defaultCareForNeutralFaction",
                "defaultCareForHostileFaction"
            ).SetBufferChanges();

            SyncQuestDismissed = Sync.Field(typeof(Quest), nameof(Quest.dismissed));
            SyncFactionAcceptRoyalFavor = Sync.Field(typeof(Faction), nameof(Faction.allowRoyalFavorRewards));
            SyncFactionAcceptGoodwill = Sync.Field(typeof(Faction), nameof(Faction.allowGoodwillRewards));

            var thingFilterTarget = new MultiTarget() { { SyncThingFilters.ThingFilterTarget, "Data/Filter" } };
            SyncThingFilterHitPoints = Sync.FieldMultiTarget(thingFilterTarget, "AllowedHitPointsPercents").SetBufferChanges();
            SyncThingFilterQuality = Sync.FieldMultiTarget(thingFilterTarget, "AllowedQualityLevels").SetBufferChanges();

            SyncBillSuspended = Sync.Field(typeof(Bill), "suspended");
            SyncIngredientSearchRadius = Sync.Field(typeof(Bill), "ingredientSearchRadius").SetBufferChanges();
            SyncBillSkillRange = Sync.Field(typeof(Bill), "allowedSkillRange").SetBufferChanges();

            SyncBillIncludeZone = Sync.Field(typeof(Bill_Production), "includeFromZone");
            SyncBillIncludeHpRange = Sync.Field(typeof(Bill_Production), "hpRange").SetBufferChanges();
            SyncBillIncludeQualityRange = Sync.Field(typeof(Bill_Production), "qualityRange").SetBufferChanges();
            SyncBillPawnRestriction = Sync.Field(typeof(Bill), "pawnRestriction");

            SyncZoneLabel = Sync.Field(typeof(Zone), "label");

            SyncBillProduction = Sync.Fields(
                typeof(Bill_Production),
                null,
                "repeatMode",
                "repeatCount",
                "targetCount",
                "pauseWhenSatisfied",
                "unpauseWhenYouHave"
            );

            SyncBillIncludeCriteria = Sync.Fields(
                typeof(Bill_Production),
                null,
                "includeEquipped",
                "includeTainted",
                "limitToAllowedStuff"
            );

             SyncDrugPolicyEntry = Sync.Fields(
                typeof(DrugPolicy),
                "entriesInt/[]",
                "allowedForAddiction",
                "allowedForJoy",
                "allowScheduled",
                "takeToInventory"
            );

            SyncDrugPolicyEntryBuffered = Sync.Fields(
                typeof(DrugPolicy),
                "entriesInt/[]",
                "daysFrequency",
                "onlyIfMoodBelow",
                "onlyIfJoyBelow"
            ).SetBufferChanges();

            // This depends on the order of AutoSlaughterManager.configs being the same on all clients
            // It's initialized using DefDatabase<ThingDef>.AllDefs which shouldn't cause problems
            SyncAutoSlaughter = Sync.Fields(
                typeof(AutoSlaughterManager),
                "configs/[]",
                "maxTotal",
                "maxMales",
                "maxMalesYoung",
                "maxFemales",
                "maxFemalesYoung",
                "allowSlaughterPregnant"
            ).PostApply(Autoslaughter_PostApply);

            SyncTradeableCount = Sync.Field(typeof(MpTransferableReference), "CountToTransfer").SetBufferChanges().PostApply(TransferableCount_PostApply);

            // 1
            SyncBillPaused = Sync.Field(typeof(Bill_Production), nameof(Bill_Production.paused)).SetBufferChanges().SetVersion(1);

            // 2
            SyncOutfitLabel = Sync.Field(typeof(Outfit), "label").SetBufferChanges().SetVersion(2);
            SyncDrugPolicyLabel = Sync.Field(typeof(DrugPolicy), "label").SetBufferChanges().SetVersion(2);
            SyncFoodRestrictionLabel = Sync.Field(typeof(FoodRestriction), "label").SetBufferChanges().SetVersion(2);
            SyncStorytellerDef = Sync.Field(typeof(Storyteller), "def").SetHostOnly().PostApply(StorytellerDef_Post).SetVersion(2);
            SyncStorytellerDifficulty = Sync.Field(typeof(Storyteller), "difficulty").SetHostOnly().PostApply(StorytellerDifficutly_Post).SetVersion(2);

            SyncDryadCaste = Sync.Field(typeof(CompTreeConnection), nameof(CompTreeConnection.desiredMode));
            SyncDesiredTreeConnectionStrength = Sync.Field(typeof(CompTreeConnection), nameof(CompTreeConnection.desiredConnectionStrength));
            SyncPlantableTargetCell = Sync.Field(typeof(CompPlantable), nameof(CompPlantable.plantCell));

            SyncAnimalPenAutocut = Sync.Field(typeof(CompAnimalPenMarker), nameof(CompAnimalPenMarker.autoCut));

            SyncNeuralSuperchargerMode = Sync.Field(typeof(CompNeuralSupercharger), nameof(CompNeuralSupercharger.autoUseMode));
        }

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
            SyncSlaveInteractionMode.Watch(pawn);
            SyncIdeoForConversion.Watch(pawn);
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
            SyncThingFilterHitPoints.Watch(SyncMarkers.DrawnThingFilter);
        }

        [MpPrefix(typeof(ThingFilterUI), "DrawQualityFilterConfig")]
        static void ThingFilterQuality()
        {
            SyncThingFilterQuality.Watch(SyncMarkers.DrawnThingFilter);
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
            // Apply the buffered value for smooth rendering
            // (the actual syncing happens in BillIngredientSearchRadius below)
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

        [MpPrefix(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.DrawButtons))]
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
                (ISessionWithTransferables)CaravanFormingProxy.drawing?.Session ??
                TransporterLoadingProxy.drawing?.Session;
            if (session != null)
                SyncTradeableCount.Watch(new MpTransferableReference(session, trad));
        }

        [MpPrefix(typeof(WITab_Caravan_Health), nameof(WITab_Caravan_Health.DoRow), new[] { typeof(Rect), typeof(Pawn) })]
        static void CaravanHealthDoRow(Pawn p)
        {
            SyncBeCarried.Watch(p);
        }

        [MpPrefix(typeof(ITab_PenAutoCut), nameof(ITab_PenAutoCut.DrawAutoCutOptions))]
        static void DrawAutoCutOptions(CompAnimalPenMarker marker)
        {
            SyncAnimalPenAutocut.Watch(marker);
        }

        [MpPrefix(typeof(Dialog_AutoSlaughter), nameof(Dialog_AutoSlaughter.DoAnimalRow))]
        static void Dialog_AutoSlaughter_Row(Map map, AutoSlaughterConfig config)
        {
            SyncAutoSlaughter.Watch(map.autoSlaughterManager, map.autoSlaughterManager.configs.IndexOf(config));
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
                SyncOutfitLabel.Watch(SyncMarkers.dialogOutfit.Outfit);

            if (SyncMarkers.drugPolicy != null)
                SyncDrugPolicyLabel.Watch(SyncMarkers.drugPolicy);

            if (SyncMarkers.foodRestriction != null)
                SyncFoodRestrictionLabel.Watch(SyncMarkers.foodRestriction.Food);
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
			
        [MpPrefix(typeof(Gizmo_PruningConfig), nameof(Gizmo_PruningConfig.DrawBar))]
        static void WatchTreeConnectionStrength(Gizmo_PruningConfig __instance)
        {
            SyncDesiredTreeConnectionStrength.Watch(__instance.connection);
		}
			
        [MpPrefix(typeof(CompPlantable), "<BeginTargeting>b__9_0")]
        static void WatchPlantableTargetCell(CompPlantable __instance)
        {
            // Sync cell to plant if it didn't require confirmation
            // This can't be synced like the other two methods related to planting, as it has more code attached to it that we don't want to sync
            SyncPlantableTargetCell.Watch(__instance);
        }

        [MpPrefix(typeof(Dialog_ChangeDryadCaste), nameof(Dialog_ChangeDryadCaste.StartChange))]
        static void WatchDryadCaste(Dialog_ChangeDryadCaste __instance)
        {
            SyncDryadCaste.Watch(__instance.treeConnection);
        }
		
        static void Autoslaughter_PostApply(object target, object value)
        {
            Multiplayer.MapContext.autoSlaughterManager.Notify_ConfigChanged();
        }

        // Neural supercharger auto use mode syncing
        [MpPrefix(typeof(Command_SetNeuralSuperchargerAutoUse), "<ProcessInput>b__11_0")] // Set to nobody being allowed to use
        [MpPrefix(typeof(Command_SetNeuralSuperchargerAutoUse), "<ProcessInput>b__11_1")] // Set to use for pawns based on their beliefs
        [MpPrefix(typeof(Command_SetNeuralSuperchargerAutoUse), "<ProcessInput>b__11_2")] // Set to use for everyone
        static void WatchNeuralSuperchargerMode(Command_SetNeuralSuperchargerAutoUse __instance)
        {
            SyncNeuralSuperchargerMode.Watch(__instance.comp);
		}
    }

}
