using Multiplayer.API;
using Multiplayer.Client.Persistent;
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
        public static ISyncField SyncGuiltAwaitingExecution;

        public static ISyncField SyncUseWorkPriorities;
        public static ISyncField SyncAutoHomeArea;
        public static ISyncField SyncAutoRebuild;
        public static SyncField[] SyncDefaultCare;

        public static ISyncField SyncQuestDismissed;
        public static ISyncField SyncFactionAcceptRoyalFavor;
        public static ISyncField SyncFactionAcceptGoodwill;

        public static ISyncField SyncThingFilterHitPoints;
        public static ISyncField SyncThingFilterQuality;

        public static ISyncField SyncBillPaused;
        public static ISyncField SyncBillSuspended;
        public static ISyncField SyncIngredientSearchRadius;
        public static ISyncField SyncBillSkillRange;

        public static ISyncField SyncBillIncludeHpRange;
        public static ISyncField SyncBillIncludeQualityRange;
        public static ISyncField SyncBillPawnRestriction;

        public static ISyncField SyncBillSlavesOnly;
        public static ISyncField SyncBillMechsOnly;
        public static ISyncField SyncBillNonMechsOnly;

        public static SyncField[] SyncBillProduction;
        public static SyncField[] SyncBillIncludeCriteria;

        public static SyncField[] SyncDrugPolicyEntry;
        public static SyncField[] SyncDrugPolicyEntryBuffered;

        public static ISyncField SyncTradeableCount;

        public static ISyncField SyncStorytellerDef;
        public static ISyncField SyncStorytellerDifficultyDef;
        public static ISyncField SyncStorytellerDifficulty;

        public static ISyncField SyncAutocutCompToggle;

        public static SyncField[] SyncAutoSlaughter;

        public static ISyncField SyncDryadCaste;
        public static ISyncField SyncDesiredTreeConnectionStrength;

        public static ISyncField SyncNeuralSuperchargerMode;

        public static ISyncField SyncGeneGizmoResource;
        public static ISyncField SyncGeneResource;
        public static ISyncField SyncGeneHemogenAllowed;
        public static ISyncField SyncGeneHolderAllowAll;

        public static ISyncField SyncNeedLevel;

        public static ISyncField SyncMechRechargeThresholds;
        public static ISyncField SyncMechAutoRepair;
        public static ISyncField SyncMechCarrierGizmoTargetValue;
        public static ISyncField SyncMechCarrierMaxToFill;

        public static ISyncField SyncStudiableCompEnabled;
        public static ISyncField SyncEntityContainmentMode;
        public static ISyncField SyncExtractBioferrite;

        public static ISyncField SyncActivityGizmoTarget;
        public static ISyncField SyncActivityCompTarget;
        public static ISyncField SyncActivityCompSuppression;

        public static void Init()
        {
            SyncMedCare = Sync.Field(typeof(Pawn), nameof(Pawn.playerSettings), nameof(Pawn_PlayerSettings.medCare));
            SyncSelfTend = Sync.Field(typeof(Pawn), nameof(Pawn.playerSettings), nameof(Pawn_PlayerSettings.selfTend));
            SyncHostilityResponse = Sync.Field(typeof(Pawn), nameof(Pawn.playerSettings), nameof(Pawn_PlayerSettings.hostilityResponse));
            SyncFollowDrafted = Sync.Field(typeof(Pawn), nameof(Pawn.playerSettings), nameof(Pawn_PlayerSettings.followDrafted));
            SyncFollowFieldwork = Sync.Field(typeof(Pawn), nameof(Pawn.playerSettings), nameof(Pawn_PlayerSettings.followFieldwork));
            SyncInteractionMode = Sync.Field(typeof(Pawn), nameof(Pawn.guest), nameof(Pawn_GuestTracker.interactionMode));
            SyncSlaveInteractionMode = Sync.Field(typeof(Pawn), nameof(Pawn.guest), nameof(Pawn_GuestTracker.slaveInteractionMode));
            SyncIdeoForConversion = Sync.Field(typeof(Pawn), nameof(Pawn.guest), nameof(Pawn_GuestTracker.ideoForConversion));
            SyncBeCarried = Sync.Field(typeof(Pawn), nameof(Pawn.health), nameof(Pawn_HealthTracker.beCarriedByCaravanIfSick));
            SyncPsychicEntropyLimit = Sync.Field(typeof(Pawn), nameof(Pawn.psychicEntropy), nameof(Pawn_PsychicEntropyTracker.limitEntropyAmount));
            SyncPsychicEntropyTargetFocus = Sync.Field(typeof(Pawn), nameof(Pawn.psychicEntropy), nameof(Pawn_PsychicEntropyTracker.targetPsyfocus)).SetBufferChanges();
            SyncGuiltAwaitingExecution = Sync.Field(typeof(Pawn), nameof(Pawn.guilt), nameof(Pawn_GuiltTracker.awaitingExecution));

            SyncUseWorkPriorities = Sync.Field(null, $"Verse.Current/Game/playSettings", nameof(PlaySettings.useWorkPriorities)).PostApply(UseWorkPriorities_PostApply);
            SyncAutoHomeArea = Sync.Field(null, "Verse.Current/Game/playSettings", nameof(PlaySettings.autoHomeArea));
            SyncAutoRebuild = Sync.Field(null, "Verse.Current/Game/playSettings", nameof(PlaySettings.autoRebuild));

            SyncDefaultCare = Sync.Fields(
                null,
                "Verse.Current/Game/playSettings",
                nameof(PlaySettings.defaultCareForColonist),
                nameof(PlaySettings.defaultCareForEntities),
                nameof(PlaySettings.defaultCareForGhouls),
                nameof(PlaySettings.defaultCareForPrisoner),
                nameof(PlaySettings.defaultCareForSlave),
                nameof(PlaySettings.defaultCareForFriendlyFaction),
                nameof(PlaySettings.defaultCareForNeutralFaction),
                nameof(PlaySettings.defaultCareForHostileFaction),
                nameof(PlaySettings.defaultCareForWildlife),
                nameof(PlaySettings.defaultCareForNoFaction),
                nameof(PlaySettings.defaultCareForTamedAnimal)
            ).SetBufferChanges();

            SyncQuestDismissed = Sync.Field(typeof(Quest), nameof(Quest.dismissed));
            SyncFactionAcceptRoyalFavor = Sync.Field(typeof(Faction), nameof(Faction.allowRoyalFavorRewards));
            SyncFactionAcceptGoodwill = Sync.Field(typeof(Faction), nameof(Faction.allowGoodwillRewards));

            SyncThingFilterHitPoints = Sync.Field(typeof(ThingFilterContext), "Filter/AllowedHitPointsPercents").SetBufferChanges();
            SyncThingFilterQuality = Sync.Field(typeof(ThingFilterContext), "Filter/AllowedQualityLevels").SetBufferChanges();

            SyncBillPaused = Sync.Field(typeof(Bill_Production), nameof(Bill_Production.paused)).SetBufferChanges();
            SyncBillSuspended = Sync.Field(typeof(Bill), nameof(Bill.suspended));
            SyncIngredientSearchRadius = Sync.Field(typeof(Bill), nameof(Bill.ingredientSearchRadius)).SetBufferChanges();
            SyncBillSkillRange = Sync.Field(typeof(Bill), nameof(Bill.allowedSkillRange)).SetBufferChanges();

            SyncBillIncludeHpRange = Sync.Field(typeof(Bill_Production), nameof(Bill_Production.hpRange)).SetBufferChanges();
            SyncBillIncludeQualityRange = Sync.Field(typeof(Bill_Production), nameof(Bill_Production.qualityRange)).SetBufferChanges();
            SyncBillPawnRestriction = Sync.Field(typeof(Bill), nameof(Bill.pawnRestriction));

            SyncBillSlavesOnly = Sync.Field(typeof(Bill), nameof(Bill.slavesOnly));
            SyncBillMechsOnly = Sync.Field(typeof(Bill), nameof(Bill.mechsOnly));
            SyncBillNonMechsOnly = Sync.Field(typeof(Bill), nameof(Bill.nonMechsOnly));

            SyncBillProduction = Sync.Fields(
                typeof(Bill_Production),
                null,
                nameof(Bill_Production.repeatMode),
                nameof(Bill_Production.repeatCount),
                nameof(Bill_Production.targetCount),
                nameof(Bill_Production.pauseWhenSatisfied),
                nameof(Bill_Production.unpauseWhenYouHave)
            );

            SyncBillIncludeCriteria = Sync.Fields(
                typeof(Bill_Production),
                null,
                nameof(Bill_Production.includeEquipped),
                nameof(Bill_Production.includeTainted),
                nameof(Bill_Production.limitToAllowedStuff)
            );

            SyncDrugPolicyEntry = Sync.Fields(
                typeof(DrugPolicy),
                $"{nameof(DrugPolicy.entriesInt)}/[]",
                nameof(DrugPolicyEntry.allowedForAddiction),
                nameof(DrugPolicyEntry.allowedForJoy),
                nameof(DrugPolicyEntry.allowScheduled),
                nameof(DrugPolicyEntry.takeToInventory)
            );

            SyncDrugPolicyEntryBuffered = Sync.Fields(
                typeof(DrugPolicy),
                $"{nameof(DrugPolicy.entriesInt)}/[]",
                nameof(DrugPolicyEntry.daysFrequency),
                nameof(DrugPolicyEntry.onlyIfMoodBelow),
                nameof(DrugPolicyEntry.onlyIfJoyBelow)
            ).SetBufferChanges();

            // This depends on the order of AutoSlaughterManager.configs being the same on all clients
            // The array is initialized using DefDatabase<ThingDef>.AllDefs which shouldn't cause problems though
            SyncAutoSlaughter = Sync.Fields(
                typeof(AutoSlaughterManager),
                $"{nameof(AutoSlaughterManager.configs)}/[]",
                nameof(AutoSlaughterConfig.maxTotal),
                nameof(AutoSlaughterConfig.maxMales),
                nameof(AutoSlaughterConfig.maxMalesYoung),
                nameof(AutoSlaughterConfig.maxFemales),
                nameof(AutoSlaughterConfig.maxFemalesYoung),
                nameof(AutoSlaughterConfig.allowSlaughterPregnant)
            ).PostApply(Autoslaughter_PostApply);

            SyncTradeableCount = Sync.Field(typeof(MpTransferableReference), nameof(MpTransferableReference.CountToTransfer)).SetBufferChanges().PostApply(TransferableCount_PostApply);

            SyncStorytellerDef = Sync.Field(typeof(Storyteller), nameof(Storyteller.def)).SetHostOnly().PostApply(StorytellerDef_Post);
            SyncStorytellerDifficultyDef = Sync.Field(typeof(Storyteller), nameof(Storyteller.difficultyDef)).SetHostOnly().PostApply(StorytellerDifficultyDef_Post);
            SyncStorytellerDifficulty = Sync.Field(typeof(Storyteller), nameof(Storyteller.difficulty)).ExposeValue().SetHostOnly().PostApply(StorytellerDifficulty_Post);

            SyncDryadCaste = Sync.Field(typeof(CompTreeConnection), nameof(CompTreeConnection.desiredMode));
            SyncDesiredTreeConnectionStrength = Sync.Field(typeof(CompTreeConnection), nameof(CompTreeConnection.desiredConnectionStrength));

            SyncAutocutCompToggle = Sync.Field(typeof(CompAutoCut), nameof(CompAutoCut.autoCut));

            SyncNeuralSuperchargerMode = Sync.Field(typeof(CompNeuralSupercharger), nameof(CompNeuralSupercharger.autoUseMode));

            SyncGeneGizmoResource = Sync.Field(typeof(GeneGizmo_Resource), nameof(GeneGizmo_Resource.targetValuePct)).SetBufferChanges();
            SyncGeneResource = Sync.Field(typeof(Gene_Resource), nameof(Gene_Resource.targetValue)).SetBufferChanges();
            SyncGeneHemogenAllowed = Sync.Field(typeof(Gene_Hemogen), nameof(Gene_Hemogen.hemogenPacksAllowed));
            SyncGeneHolderAllowAll = Sync.Field(typeof(CompGenepackContainer), nameof(CompGenepackContainer.autoLoad));

            SyncNeedLevel = Sync.Field(typeof(Need), nameof(Need.curLevelInt)).SetDebugOnly();

            SyncMechRechargeThresholds = Sync.Field(typeof(MechanitorControlGroup), nameof(MechanitorControlGroup.mechRechargeThresholds));
            SyncMechAutoRepair = Sync.Field(typeof(CompMechRepairable), nameof(CompMechRepairable.autoRepair));
            SyncMechCarrierGizmoTargetValue = Sync.Field(typeof(MechCarrierGizmo), nameof(MechCarrierGizmo.targetValue)).SetBufferChanges();
            SyncMechCarrierMaxToFill = Sync.Field(typeof(CompMechCarrier), nameof(CompMechCarrier.maxToFill)).SetBufferChanges();

            SyncStudiableCompEnabled = Sync.Field(typeof(CompStudiable), nameof(CompStudiable.studyEnabled));
            SyncEntityContainmentMode = Sync.Field(typeof(CompHoldingPlatformTarget), nameof(CompHoldingPlatformTarget.containmentMode));
            SyncExtractBioferrite = Sync.Field(typeof(CompHoldingPlatformTarget), nameof(CompHoldingPlatformTarget.extractBioferrite));

            SyncActivityGizmoTarget = Sync.Field(typeof(ActivityGizmo), nameof(ActivityGizmo.targetValuePct)).SetBufferChanges();
            SyncActivityCompTarget = Sync.Field(typeof(CompActivity), nameof(CompActivity.suppressIfAbove)).SetBufferChanges();
            SyncActivityCompSuppression = Sync.Field(typeof(CompActivity), nameof(CompActivity.suppressionEnabled));
        }

        [MpPrefix(typeof(StorytellerUI), nameof(StorytellerUI.DrawStorytellerSelectionInterface))]
        static void ChangeStoryteller()
        {
            SyncStorytellerDef.Watch(Find.Storyteller);
            SyncStorytellerDifficultyDef.Watch(Find.Storyteller);
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

        static void StorytellerDifficultyDef_Post(object target, object value)
        {
            foreach (var comp in Multiplayer.game.asyncTimeComps)
                comp.storyteller.difficultyDef = Find.Storyteller.difficultyDef;
        }

        static void StorytellerDifficulty_Post(object target, object value)
        {
            foreach (var comp in Multiplayer.game.asyncTimeComps)
                comp.storyteller.difficulty = Find.Storyteller.difficulty;
        }

        [MpPrefix(typeof(HealthCardUtility), nameof(HealthCardUtility.DrawOverviewTab))]
        static void HealthCardUtility_Patch(Pawn pawn)
        {
            if (pawn.playerSettings != null)
            {
                SyncMedCare.Watch(pawn);
                SyncSelfTend.Watch(pawn);
            }
        }

        [MpPrefix(typeof(ITab_Pawn_Visitor), nameof(ITab_Pawn_Visitor.FillTab))]
        static void ITab_Pawn_Visitor_Patch(ITab __instance)
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
            return WatchDropdowns(() =>
            {
                SyncBillPawnRestriction.Watch(___bill);
                SyncBillSlavesOnly.Watch(___bill);
                SyncBillMechsOnly.Watch(___bill);
                SyncBillNonMechsOnly.Watch(___bill);
            }, __result);
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

        [MpPrefix(typeof(TrainingCardUtility), nameof(TrainingCardUtility.DrawTrainingCard))]
        static void PawnSettingFollowWatch(Pawn pawn)
        {
            SyncFollowDrafted.Watch(pawn);
            SyncFollowFieldwork.Watch(pawn);
        }

        [MpPrefix(typeof(Dialog_MedicalDefaults), nameof(Dialog_MedicalDefaults.DoWindowContents))]
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

        [MpPrefix(typeof(Widgets), nameof(Widgets.CheckboxLabeled))]
        static void CheckboxLabeled()
        {
            // Watched here to get reset asap and not trigger any side effects
            if (SyncMarkers.manualPriorities)
                SyncUseWorkPriorities.Watch();
        }

        [MpPrefix(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
        static void PlaySettingsControls()
        {
            SyncAutoHomeArea.Watch();
            SyncAutoRebuild.Watch();
        }

        [MpPrefix(typeof(ThingFilterUI), nameof(ThingFilterUI.DrawHitPointsFilterConfig))]
        static void ThingFilterHitPoints()
        {
            if (ThingFilterMarkers.DrawnThingFilter != null)
                SyncThingFilterHitPoints.Watch(ThingFilterMarkers.DrawnThingFilter);
        }

        [MpPrefix(typeof(ThingFilterUI), nameof(ThingFilterUI.DrawQualityFilterConfig))]
        static void ThingFilterQuality()
        {
            if (ThingFilterMarkers.DrawnThingFilter != null)
                SyncThingFilterQuality.Watch(ThingFilterMarkers.DrawnThingFilter);
        }

        [MpPrefix(typeof(Bill), nameof(Bill.DoInterface))]
        static void BillInterfaceCard(Bill __instance)
        {
            SyncBillSuspended.Watch(__instance);
            SyncBillSkillRange.Watch(__instance);
            SyncIngredientSearchRadius.Watch(__instance);

            SyncBillProduction.Watch(__instance);
        }

        [MpPrefix(typeof(Dialog_BillConfig), nameof(Dialog_BillConfig.DoWindowContents))]
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

        [MpPrefix(typeof(BillRepeatModeUtility), nameof(BillRepeatModeUtility.MakeConfigFloatMenu), lambdaOrdinal: 0)]
        [MpPrefix(typeof(BillRepeatModeUtility), nameof(BillRepeatModeUtility.MakeConfigFloatMenu), lambdaOrdinal: 1)]
        [MpPrefix(typeof(BillRepeatModeUtility), nameof(BillRepeatModeUtility.MakeConfigFloatMenu), lambdaOrdinal: 2)]
        static void BillRepeatMode(object __instance)
        {
            SyncBillProduction.Watch(__instance.GetPropertyOrField("bill"));
        }

        [MpPrefix(typeof(ITab_Bills), nameof(ITab_Bills.TabUpdate))]
        static void BillIngredientSearchRadius(ITab_Bills __instance)
        {
            // Apply the buffered value for smooth rendering
            // (the actual syncing happens in BillIngredientSearchRadius below)
            if (__instance.mouseoverBill is { } mouseover)
                SyncIngredientSearchRadius.Watch(mouseover);
        }

        [MpPrefix(typeof(Dialog_BillConfig), nameof(Dialog_BillConfig.WindowUpdate))]
        static void BillIngredientSearchRadius(Dialog_BillConfig __instance)
        {
            SyncIngredientSearchRadius.Watch(__instance.bill);
        }

        [MpPrefix(typeof(Dialog_ManageDrugPolicies), nameof(Dialog_ManageDrugPolicies.DoPolicyConfigArea))]
        static void DialogManageDrugPolicies(Dialog_ManageDrugPolicies __instance)
        {
            DrugPolicy policy = __instance.SelectedPolicy;
            for (int i = 0; i < policy.Count; i++)
            {
                SyncDrugPolicyEntry.Watch(policy, i);
                SyncDrugPolicyEntryBuffered.Watch(policy, i);
            }
        }


        [MpPrefix(typeof(TransferableUIUtility), nameof(TransferableUIUtility.DoCountAdjustInterface))]
        static void TransferableAdjustTo(Transferable trad)
        {
            var session = SyncSessionWithTransferablesMarker.DrawnSessionWithTransferables;
            if (session != null)
                SyncTradeableCount.Watch(new MpTransferableReference(session, trad));
        }

        [MpPrefix(typeof(WITab_Caravan_Health), nameof(WITab_Caravan_Health.DoRow), new[] { typeof(Rect), typeof(Pawn) })]
        static void CaravanHealthDoRow(Pawn p)
        {
            SyncBeCarried.Watch(p);
        }

        [MpPrefix(typeof(ITab_PenAutoCut), nameof(ITab_PenAutoCut.DrawAutoCutOptions))]
        static void DrawAnimalPenAutoCutOptions(CompAnimalPenMarker marker)
        {
            SyncAutocutCompToggle.Watch(marker);
        }

        [MpPrefix(typeof(ITab_WindTurbineAutoCut), nameof(ITab_WindTurbineAutoCut.DrawAutoCutOptions))]
        static void DrawWindTurbineAutoCutOptions(CompAutoCutWindTurbine autoCut)
        {
            SyncAutocutCompToggle.Watch(autoCut);
        }

        [MpPrefix(typeof(Dialog_AutoSlaughter), nameof(Dialog_AutoSlaughter.DoAnimalRow))]
        static void Dialog_AutoSlaughter_Row(Dialog_AutoSlaughter __instance, AutoSlaughterConfig config)
        {
            SyncAutoSlaughter.Watch(__instance.map.autoSlaughterManager, __instance.map.autoSlaughterManager.configs.IndexOf(config));
        }

        [MpPrefix(typeof(Bill), nameof(Bill.DoInterface))]
        [MpPrefix(typeof(Bill_Production), nameof(Bill_Production.ShouldDoNow))]
        static void WatchBillPaused(Bill __instance)
        {
            if (__instance is Bill_Production)
                SyncBillPaused.Watch(__instance);
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
                    entry.option.action = (SyncFieldUtil.FieldWatchPrefix + watchAction + entry.option.action + SyncFieldUtil.FieldWatchPostfix);
                yield return entry;
            }
        }

        [MpPrefix(typeof(Gizmo_PruningConfig), nameof(Gizmo_PruningConfig.DrawBar))]
        static void WatchTreeConnectionStrength(Gizmo_PruningConfig __instance)
        {
            SyncDesiredTreeConnectionStrength.Watch(__instance.connection);
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
        [MpPrefix(typeof(Command_SetNeuralSuperchargerAutoUse), nameof(Command_SetNeuralSuperchargerAutoUse.ProcessInput), 0)] // Set to nobody being allowed to use
        [MpPrefix(typeof(Command_SetNeuralSuperchargerAutoUse), nameof(Command_SetNeuralSuperchargerAutoUse.ProcessInput), 1)] // Set to use for pawns based on their beliefs
        [MpPrefix(typeof(Command_SetNeuralSuperchargerAutoUse), nameof(Command_SetNeuralSuperchargerAutoUse.ProcessInput), 2)] // Set to use for everyone
        static void WatchNeuralSuperchargerMode(Command_SetNeuralSuperchargerAutoUse __instance)
        {
            SyncNeuralSuperchargerMode.Watch(__instance.comp);
		}

        [MpPrefix(typeof(CharacterCardUtility), nameof(CharacterCardUtility.DrawCharacterCard))]
        static void WatchAwaitingExecution(Pawn pawn)
        {
            SyncGuiltAwaitingExecution.Watch(pawn);
        }

        [MpPrefix(typeof(Gizmo_Slider), nameof(Gizmo_Slider.GizmoOnGUI))]
        static void SyncGeneResourceChange(Gizmo_Slider __instance)
        {
            if (__instance is GeneGizmo_Resource geneGizmo)
            {
                SyncGeneGizmoResource.Watch(geneGizmo);
                SyncGeneResource.Watch(geneGizmo.gene);

                if (geneGizmo.gene is Gene_Hemogen)
                    SyncGeneHemogenAllowed.Watch(geneGizmo.gene);
            }
            else if (__instance is ActivityGizmo activityGizmo)
            {
                SyncActivityGizmoTarget.Watch(activityGizmo);

                var comp = activityGizmo.Comp;
                SyncActivityCompTarget.Watch(comp);
                SyncActivityCompSuppression.Watch(comp);
            }
        }

        [MpPrefix(typeof(ITab_ContentsGenepackHolder), nameof(ITab_ContentsGenepackHolder.DoItemsLists))]
        static void WatchGeneHolderAllowAll(ITab_ContentsGenepackHolder __instance)
        {
            SyncGeneHolderAllowAll.Watch(__instance.ContainerThing);
        }

        [MpPrefix(typeof(Need), nameof(Need.DrawOnGUI))]
        static void SyncNeedLevelValueChange(Need __instance)
        {
            SyncNeedLevel.Watch(__instance);
        }

        [MpPrefix(typeof(Dialog_RechargeSettings), nameof(Dialog_RechargeSettings.DoWindowContents))]
        static void WatchRechargeSettings(Dialog_RechargeSettings __instance)
        {
            SyncMechRechargeThresholds.Watch(__instance.controlGroup);
        }

        [MpPrefix(typeof(PawnColumnWorker_AutoRepair), nameof(PawnColumnWorker_AutoRepair.DoCell))]
        static void WatchMechAutoRepair(Pawn pawn)
        {
            var comp = pawn.GetComp<CompMechRepairable>();
            if (comp != null)
                SyncMechAutoRepair.Watch(comp);
        }

        [MpPrefix(typeof(MechCarrierGizmo), nameof(MechCarrierGizmo.GizmoOnGUI))]
        static void WatchMechCarrierMaxToFill(MechCarrierGizmo __instance)
        {
            SyncMechCarrierGizmoTargetValue.Watch(__instance);
            SyncMechCarrierMaxToFill.Watch(__instance.carrier);
        }

        [MpPrefix(typeof(ITab_StudyNotes), nameof(ITab_StudyNotes.DrawTitle))]
        static void CompStudiableEnabledCheckbox(ITab_StudyNotes __instance)
        {
            var comp = __instance.StudiableThing.TryGetComp<CompStudiable>();
            if (comp != null)
                SyncStudiableCompEnabled.Watch(comp);
        }

        [MpPrefix(typeof(ITab_Entity), nameof(ITab_Entity.FillTab))]
        static void CompHoldingPlatformTargetMode(ITab_Entity __instance)
        {
            var comp = __instance.SelPawn.TryGetComp<CompHoldingPlatformTarget>();
            if (comp != null)
            {
                SyncEntityContainmentMode.Watch(comp);
                SyncExtractBioferrite.Watch(comp);
            }
        }
    }

}
