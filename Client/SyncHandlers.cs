using Harmony;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    public static class SyncHandlers
    {
        public static void Init()
        {
            HarmonyInstance harmony = Multiplayer.harmony;
            harmony.DoMpPatches(typeof(SyncMarkers));
            harmony.DoMpPatches(typeof(SyncPatches));
            harmony.DoMpPatches(typeof(SyncDelegates));
            harmony.DoMpPatches(typeof(SyncThingFilters));
            harmony.DoMpPatches(typeof(SyncResearch));

            RuntimeHelpers.RunClassConstructor(typeof(SyncPatches).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(SyncFieldsPatches).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(SyncDelegates).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(SyncThingFilters).TypeHandle);

            Sync.RegisterFieldPatches(typeof(SyncFieldsPatches));
            Sync.RegisterSyncDelegates(typeof(SyncDelegates));
            Sync.RegisterSyncMethods(typeof(SyncPatches));
            Sync.RegisterSyncMethods(typeof(SyncThingFilters));
        }
    }

    public static class SyncFieldsPatches
    {
        public static SyncField SyncAreaRestriction = Sync.Field(typeof(Pawn), "playerSettings", "AreaRestriction");
        public static SyncField SyncMedCare = Sync.Field(typeof(Pawn), "playerSettings", "medCare");
        public static SyncField SyncSelfTend = Sync.Field(typeof(Pawn), "playerSettings", "selfTend");
        public static SyncField SyncHostilityResponse = Sync.Field(typeof(Pawn), "playerSettings", "hostilityResponse");
        public static SyncField SyncFollowFieldwork = Sync.Field(typeof(Pawn), "playerSettings", "followFieldwork");
        public static SyncField SyncFollowDrafted = Sync.Field(typeof(Pawn), "playerSettings", "followDrafted");
        public static SyncField SyncMaster = Sync.Field(typeof(Pawn), "playerSettings", "master");
        public static SyncField SyncGetsFood = Sync.Field(typeof(Pawn), "guest", "GetsFood");
        public static SyncField SyncInteractionMode = Sync.Field(typeof(Pawn), "guest", "interactionMode");

        public static SyncField SyncGodMode = Sync.Field(null, "Verse.DebugSettings/godMode");
        public static SyncField SyncResearchProject = Sync.Field(null, "Verse.Find/ResearchManager/currentProj");
        public static SyncField SyncUseWorkPriorities = Sync.Field(null, "Verse.Current/Game/playSettings", "useWorkPriorities");
        public static SyncField SyncAutoHomeArea = Sync.Field(null, "Verse.Current/Game/playSettings", "autoHomeArea");
        public static SyncField SyncAutoRebuild = Sync.Field(null, "Verse.Current/Game/playSettings", "autoRebuild");
        public static SyncField[] SyncDefaultCare = Sync.Fields(
            null,
            "Verse.Current/Game/playSettings",
            "defaultCareForColonyHumanlike",
            "defaultCareForColonyPrisoner",
            "defaultCareForColonyAnimal",
            "defaultCareForNeutralAnimal",
            "defaultCareForNeutralFaction",
            "defaultCareForHostileFaction"
        );

        public static SyncField[] SyncThingFilterHitPoints =
            Sync.FieldMultiTarget(Sync.thingFilterTarget, "AllowedHitPointsPercents").SetBufferChanges();

        public static SyncField[] SyncThingFilterQuality =
            Sync.FieldMultiTarget(Sync.thingFilterTarget, "AllowedQualityLevels").SetBufferChanges();

        public static SyncField SyncBillSuspended = Sync.Field(typeof(Bill), "suspended");
        public static SyncField SyncIngredientSearchRadius = Sync.Field(typeof(Bill), "ingredientSearchRadius").SetBufferChanges();
        public static SyncField SyncBillSkillRange = Sync.Field(typeof(Bill), "allowedSkillRange").SetBufferChanges();

        public static SyncField SyncBillIncludeZone = Sync.Field(typeof(Bill_Production), "includeFromZone");
        public static SyncField SyncBillIncludeHpRange = Sync.Field(typeof(Bill_Production), "hpRange").SetBufferChanges();
        public static SyncField SyncBillIncludeQualityRange = Sync.Field(typeof(Bill_Production), "qualityRange").SetBufferChanges();
        public static SyncField SyncBillPawnRestriction = Sync.Field(typeof(Bill), "pawnRestriction");

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
            "entriesInt[]",
            "allowedForAddiction",
            "allowedForJoy",
            "allowScheduled",
            "takeToInventory"
        );

        public static SyncField[] SyncDrugPolicyEntryBuffered = Sync.Fields(
            typeof(DrugPolicy),
            "entriesInt[]",
            "daysFrequency",
            "onlyIfMoodBelow",
            "onlyIfJoyBelow"
        ).SetBufferChanges();

        [MpPrefix(typeof(AreaAllowedGUI), "DoAreaSelector")]
        static void DoAreaSelector_Prefix(Pawn p)
        {
            SyncAreaRestriction.Watch(p);
        }

        [MpPrefix(typeof(PawnColumnWorker_AllowedArea), "HeaderClicked")]
        static void AllowedArea_HeaderClicked_Prefix(PawnTable table)
        {
            foreach (Pawn pawn in table.PawnsListForReading)
                SyncAreaRestriction.Watch(pawn);
        }

        [MpPrefix("RimWorld.InspectPaneFiller+<DrawAreaAllowed>c__AnonStorey0", "<>m__0")]
        static void DrawAreaAllowed_Inner(object __instance)
        {
            SyncAreaRestriction.Watch(__instance.GetPropertyOrField("pawn"));
        }

        [MpPrefix(typeof(HealthCardUtility), "DrawOverviewTab")]
        static void HealthCardUtility1(Pawn pawn)
        {
            SyncMedCare.Watch(pawn);
            SyncSelfTend.Watch(pawn);
        }

        [MpPrefix(typeof(ITab_Pawn_Visitor), "FillTab")]
        static void ITab_Pawn_Visitor(ITab __instance)
        {
            Pawn pawn = __instance.SelPawn;
            SyncMedCare.Watch(pawn);
            SyncGetsFood.Watch(pawn);
            SyncInteractionMode.Watch(pawn);
        }

        [MpPrefix(typeof(HostilityResponseModeUtility), "<DrawResponseButton_GenerateMenu>c__Iterator0+<DrawResponseButton_GenerateMenu>c__AnonStorey1", "<>m__0")]
        static void SelectHostilityResponse(object __instance)
        {
            SyncHostilityResponse.Watch(__instance.GetPropertyOrField("<>f__ref$2/p"));
        }

        [MpPrefix(typeof(MedicalCareUtility), "<MedicalCareSelectButton_GenerateMenu>c__Iterator0+<MedicalCareSelectButton_GenerateMenu>c__AnonStorey2", "<>m__0")]
        static void SelectMedicalCare(object __instance)
        {
            SyncMedCare.Watch(__instance.GetPropertyOrField("<>f__ref$3/p"));
        }

        [MpPrefix(typeof(PawnColumnWorker_FollowFieldwork), "SetValue")]
        static void FollowFieldwork(Pawn pawn)
        {
            SyncFollowFieldwork.Watch(pawn);
        }

        [MpPrefix(typeof(PawnColumnWorker_FollowDrafted), "SetValue")]
        static void FollowDrafted(Pawn pawn)
        {
            SyncFollowDrafted.Watch(pawn);
        }

        [MpPrefix(typeof(TrainableUtility), "<MasterSelectButton_GenerateMenu>c__Iterator0+<MasterSelectButton_GenerateMenu>c__AnonStorey1", "<>m__0")]
        static void OpenMasterSelectMenu_Inner1(object __instance)
        {
            SyncMaster.Watch(__instance.GetPropertyOrField("p"));
        }

        [MpPrefix(typeof(TrainableUtility), "<MasterSelectButton_GenerateMenu>c__Iterator0+<MasterSelectButton_GenerateMenu>c__AnonStorey2", "<>m__0")]
        static void OpenMasterSelectMenu_Inner2(object __instance)
        {
            SyncMaster.Watch(__instance.GetPropertyOrField("<>f__ref$1/p"));
        }

        [MpPrefix(typeof(Dialog_MedicalDefaults), "DoWindowContents")]
        static void MedicalDefaults()
        {
            SyncDefaultCare.Watch();
        }

        [MpPrefix(typeof(Widgets), "CheckboxLabeled")]
        static void CheckboxLabeled()
        {
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

        [MpPrefix(typeof(Dialog_BillConfig), "<GeneratePawnRestrictionOptions>c__Iterator0+<GeneratePawnRestrictionOptions>c__AnonStorey4", "<>m__0")] // Set to null
        [MpPrefix(typeof(Dialog_BillConfig), "<GeneratePawnRestrictionOptions>c__Iterator0+<GeneratePawnRestrictionOptions>c__AnonStorey5", "<>m__0")]
        [MpPrefix(typeof(Dialog_BillConfig), "<GeneratePawnRestrictionOptions>c__Iterator0+<GeneratePawnRestrictionOptions>c__AnonStorey5", "<>m__1")]
        [MpPrefix(typeof(Dialog_BillConfig), "<GeneratePawnRestrictionOptions>c__Iterator0+<GeneratePawnRestrictionOptions>c__AnonStorey5", "<>m__2")]
        [MpPrefix(typeof(Dialog_BillConfig), "<GeneratePawnRestrictionOptions>c__Iterator0+<GeneratePawnRestrictionOptions>c__AnonStorey5", "<>m__3")]
        static void BillPawnRestriction(object __instance)
        {
            SyncBillPawnRestriction.Watch(__instance.GetPropertyOrField("<>f__ref$0/$this/bill"));
        }

        [MpPrefix(typeof(Dialog_BillConfig), "<GenerateStockpileInclusion>c__Iterator1", "<>m__0")]
        static void BillIncludeZoneSetNull(object __instance)
        {
            SyncBillIncludeZone.Watch(__instance.GetPropertyOrField("$this/bill"));
        }

        [MpPrefix(typeof(Dialog_BillConfig), "<GenerateStockpileInclusion>c__Iterator1+<GenerateStockpileInclusion>c__AnonStorey6", "<>m__0")]
        static void BillIncludeZone(object __instance)
        {
            SyncBillIncludeZone.Watch(__instance.GetPropertyOrField("<>f__ref$1/$this/bill"));
        }

        [MpPrefix(typeof(BillRepeatModeUtility), "<MakeConfigFloatMenu>c__AnonStorey0", "<>m__0")]
        [MpPrefix(typeof(BillRepeatModeUtility), "<MakeConfigFloatMenu>c__AnonStorey0", "<>m__1")]
        [MpPrefix(typeof(BillRepeatModeUtility), "<MakeConfigFloatMenu>c__AnonStorey0", "<>m__2")]
        static void BillRepeatMode(object __instance)
        {
            SyncBillProduction.Watch(__instance.GetPropertyOrField("bill"));
        }

        [MpPrefix(typeof(ITab_Bills), "TabUpdate")]
        static void BillIngredientSearchRadius(ITab_Bills __instance)
        {
            // Use the buffered value for smooth rendering (doesn't actually have to sync anything here)
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

        [MpPrefix(typeof(DebugWindowsOpener), "ToggleGodMode")]
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
    }

    public static class SyncPatches
    {
        static SyncPatches()
        {
            Sync.RegisterSyncMethod(typeof(Pawn_TimetableTracker), "SetAssignment");
            Sync.RegisterSyncMethod(typeof(Pawn_WorkSettings), "SetPriority");
            Sync.RegisterSyncMethod(typeof(Pawn_DraftController), "set_Drafted");
            Sync.RegisterSyncMethod(typeof(Pawn_DraftController), "set_FireAtWill");
            Sync.RegisterSyncMethod(typeof(Pawn_DraftController), "set_FireAtWill");
            Sync.RegisterSyncMethod(typeof(Pawn_DrugPolicyTracker), "set_CurrentPolicy");
            Sync.RegisterSyncMethod(typeof(Pawn_OutfitTracker), "set_CurrentOutfit");
            Sync.RegisterSyncMethod(typeof(Zone), "Delete");
            Sync.RegisterSyncMethod(typeof(BillStack), "Delete");
            Sync.RegisterSyncMethod(typeof(BillStack), "Reorder");
            Sync.RegisterSyncMethod(typeof(Bill_Production), "SetStoreMode");
            Sync.RegisterSyncMethod(typeof(Pawn_JobTracker), "StartJob", typeof(Expose<Job>), typeof(JobCondition), typeof(ThinkNode), typeof(bool), typeof(bool), typeof(ThinkTreeDef), typeof(JobTag?), typeof(bool));
            Sync.RegisterSyncMethod(typeof(Pawn_JobTracker), "TryTakeOrderedJob", typeof(Expose<Job>), typeof(JobTag));
            Sync.RegisterSyncMethod(typeof(Pawn_JobTracker), "TryTakeOrderedJobPrioritizedWork", typeof(Expose<Job>), typeof(WorkGiver), typeof(IntVec3));
            Sync.RegisterSyncMethod(typeof(StorageSettings), "set_Priority");
            Sync.RegisterSyncMethod(typeof(Pawn), "set_Name", typeof(Expose<Name>));
            Sync.RegisterSyncMethod(typeof(Building_TurretGun), "OrderAttack");
            Sync.RegisterSyncMethod(typeof(Area), "Invert");
            Sync.RegisterSyncMethod(typeof(Area), "Delete");
            Sync.RegisterSyncMethod(typeof(AreaManager), "TryMakeNewAllowed");
            Sync.RegisterSyncMethod(typeof(DrugPolicyDatabase), "MakeNewDrugPolicy");
            Sync.RegisterSyncMethod(typeof(DrugPolicyDatabase), "TryDelete");
            Sync.RegisterSyncMethod(typeof(OutfitDatabase), "MakeNewOutfit");
            Sync.RegisterSyncMethod(typeof(OutfitDatabase), "TryDelete");
        }

        public static SyncField SyncTimetable = Sync.Field(typeof(Pawn), "timetable", "times");

        [MpPrefix(typeof(PawnColumnWorker_CopyPasteTimetable), "PasteTo")]
        static bool CopyPasteTimetable(Pawn p)
        {
            return !SyncTimetable.DoSync(p, PawnColumnWorker_CopyPasteTimetable.clipboard);
        }

        // ===== CALLBACKS =====

        [MpPostfix(typeof(DrugPolicyDatabase), "MakeNewDrugPolicy")]
        static void MakeNewDrugPolicy_Postfix(DrugPolicy __result)
        {
            // todo check faction
            if (__result != null && Find.WindowStack?.WindowOfType<Dialog_ManageDrugPolicies>() is Dialog_ManageDrugPolicies dialog)
                dialog.SelectedPolicy = __result;
        }

        [MpPostfix(typeof(OutfitDatabase), "MakeNewOutfit")]
        static void MakeNewOutfit_Postfix(Outfit __result)
        {
            if (__result != null && Find.WindowStack?.WindowOfType<Dialog_ManageOutfits>() is Dialog_ManageOutfits dialog)
                dialog.SelectedOutfit = __result;
        }

        [MpPostfix(typeof(DrugPolicyDatabase), "TryDelete")]
        static void TryDeleteDrugPolicy_Postfix(DrugPolicy policy, AcceptanceReport __result)
        {
            if (__result.Accepted && Find.WindowStack?.WindowOfType<Dialog_ManageDrugPolicies>() is Dialog_ManageDrugPolicies dialog && dialog.SelectedPolicy == policy)
                dialog.SelectedPolicy = null;
        }

        [MpPostfix(typeof(OutfitDatabase), "TryDelete")]
        static void TRyDeleteOutfit_Postfix(Outfit outfit, AcceptanceReport __result)
        {
            if (__result.Accepted && Find.WindowStack?.WindowOfType<Dialog_ManageOutfits>() is Dialog_ManageOutfits dialog && dialog.SelectedOutfit == outfit)
                dialog.SelectedOutfit = null;
        }
    }

    public static class SyncThingFilters
    {
        public static SyncMethod[] SyncThingFilterAllowThing = Sync.MethodMultiTarget(Sync.thingFilterTarget, "SetAllow", typeof(ThingDef), typeof(bool));
        public static SyncMethod[] SyncThingFilterAllowSpecial = Sync.MethodMultiTarget(Sync.thingFilterTarget, "SetAllow", typeof(SpecialThingFilterDef), typeof(bool));
        public static SyncMethod[] SyncThingFilterAllowStuffCategory = Sync.MethodMultiTarget(Sync.thingFilterTarget, "SetAllow", typeof(StuffCategoryDef), typeof(bool));

        [MpPrefix(typeof(ThingFilter), "SetAllow", typeof(StuffCategoryDef), typeof(bool))]
        static bool ThingFilter_SetAllow(StuffCategoryDef cat, bool allow)
        {
            return !SyncThingFilterAllowStuffCategory.DoSync(SyncMarkers.ThingFilterOwner, cat, allow);
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", typeof(SpecialThingFilterDef), typeof(bool))]
        static bool ThingFilter_SetAllow(SpecialThingFilterDef sfDef, bool allow)
        {
            return !SyncThingFilterAllowSpecial.DoSync(SyncMarkers.ThingFilterOwner, sfDef, allow);
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", typeof(ThingDef), typeof(bool))]
        static bool ThingFilter_SetAllow(ThingDef thingDef, bool allow)
        {
            return !SyncThingFilterAllowThing.DoSync(SyncMarkers.ThingFilterOwner, thingDef, allow);
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", typeof(ThingCategoryDef), typeof(bool), typeof(IEnumerable<ThingDef>), typeof(IEnumerable<SpecialThingFilterDef>))]
        static bool ThingFilter_SetAllow(ThingCategoryDef categoryDef, bool allow)
        {
            if (!Multiplayer.ShouldSync || SyncMarkers.ThingFilterOwner == null) return true;

            if (SyncMarkers.tabStorage != null)
                ThingFilter_AllowCategory_HelperStorage(SyncMarkers.tabStorage, categoryDef, allow);
            else if (SyncMarkers.billConfig != null)
                ThingFilter_AllowCategory_HelperBill(SyncMarkers.billConfig, categoryDef, allow);
            else if (SyncMarkers.dialogOutfit != null)
                ThingFilter_AllowCategory_HelperOutfit(SyncMarkers.dialogOutfit, categoryDef, allow);

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

            return false;
        }

        private static IEnumerable<SpecialThingFilterDef> OutfitSpecialFilters => SpecialThingFilterDefOf.AllowNonDeadmansApparel.ToEnumerable();

        [SyncMethod]
        static void ThingFilter_DisallowAll_HelperBill(Bill bill) => bill.ingredientFilter.SetDisallowAll(null, bill.recipe.forceHiddenSpecialFilters);

        [SyncMethod]
        static void ThingFilter_AllowAll_HelperBill(Bill bill) => bill.ingredientFilter.SetAllowAll(bill.recipe.fixedIngredientFilter);

        [SyncMethod]
        static void ThingFilter_DisallowAll_HelperOutfit(Outfit outfit) => outfit.filter.SetDisallowAll(null, OutfitSpecialFilters);

        [SyncMethod]
        static void ThingFilter_AllowAll_HelperOutfit(Outfit outfit) => outfit.filter.SetAllowAll(Dialog_ManageOutfits.apparelGlobalFilter);

        [SyncMethod]
        static void ThingFilter_DisallowAll_HelperStorage(IStoreSettingsParent storage) => storage.GetStoreSettings().filter.SetDisallowAll(null, null);

        [SyncMethod]
        static void ThingFilter_AllowAll_HelperStorage(IStoreSettingsParent storage) => storage.GetStoreSettings().filter.SetAllowAll(storage.GetParentStoreSettings()?.filter);

        [SyncMethod]
        static void ThingFilter_AllowCategory_HelperBill(Bill bill, ThingCategoryDef categoryDef, bool allow) => ThingFilter_AllowCategory_Helper(bill.ingredientFilter, categoryDef, allow, bill.recipe.fixedIngredientFilter, null, bill.recipe.forceHiddenSpecialFilters);

        [SyncMethod]
        static void ThingFilter_AllowCategory_HelperOutfit(Outfit outfit, ThingCategoryDef categoryDef, bool allow) => ThingFilter_AllowCategory_Helper(outfit.filter, categoryDef, allow, Dialog_ManageOutfits.apparelGlobalFilter, null, OutfitSpecialFilters);

        [SyncMethod]
        static void ThingFilter_AllowCategory_HelperStorage(IStoreSettingsParent storage, ThingCategoryDef categoryDef, bool allow) => ThingFilter_AllowCategory_Helper(storage.GetStoreSettings().filter, categoryDef, allow, storage.GetParentStoreSettings()?.filter, null, null);

        static void ThingFilter_AllowCategory_Helper(ThingFilter filter, ThingCategoryDef categoryDef, bool allow, ThingFilter parentfilter, IEnumerable<ThingDef> forceHiddenDefs, IEnumerable<SpecialThingFilterDef> forceHiddenFilters)
        {
            Listing_TreeThingFilter listing = new Listing_TreeThingFilter(filter, parentfilter, forceHiddenDefs, forceHiddenFilters, null);
            listing.CalculateHiddenSpecialFilters();
            filter.SetAllow(categoryDef, allow, forceHiddenDefs, listing.hiddenSpecialFilters);
        }
    }

    public static class SyncDelegates
    {
        [SyncDelegate]
        [MpPrefix(typeof(FloatMenuMakerMap), "<GotoLocationOption>c__AnonStorey1B", "<>m__0")]   // Goto
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey3", "<>m__0")]    // Arrest
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey7", "<>m__0")]    // Rescue
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey7", "<>m__1")]    // Capture
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey9", "<>m__0")]    // Carry to cryptosleep casket
        [MpPrefix(typeof(HealthCardUtility), "<GenerateSurgeryOption>c__AnonStorey4", "<>m__0")] // Add medical bill
        static bool GeneralSync(object __instance, MethodBase __originalMethod)
        {
            return !Sync.Delegate(__instance, __originalMethod);
        }

        [SyncDelegate("$this")]
        [MpPrefix(typeof(Pawn_PlayerSettings), "<GetGizmos>c__Iterator0", "<>m__1")]    // Release animals
        [MpPrefix(typeof(PriorityWork), "<GetGizmos>c__Iterator0", "<>m__0")]           // Clear prioritized work
        [MpPrefix(typeof(CompFlickable), "<CompGetGizmosExtra>c__Iterator0", "<>m__1")] // Designate flick
        static bool GeneralIteratorSync(object __instance, MethodBase __originalMethod)
        {
            return !Sync.Delegate(__instance, __originalMethod);
        }

        [SyncDelegate]
        [MpPrefix(typeof(ITab_Bills), "<FillTab>c__AnonStorey0", "<>m__0")] // New bill
        static bool AddBill(object __instance, MethodBase __originalMethod)
        {
            Sync.selThingContext = __instance.GetPropertyOrField("$this/SelThing") as Building_WorkTable;
            bool result = !Sync.Delegate(__instance, __originalMethod);
            Sync.selThingContext = null;

            return result;
        }

        [SyncDelegate("changeableProjectile", "<>f__ref$0/$this")]
        [MpPrefix(typeof(Building_TurretGun), "<GetGizmos>c__Iterator0+<GetGizmos>c__AnonStorey2", "<>m__0")] // Remove shell
        static bool TurretGunGizmos_RemoveShell(object __instance, MethodBase __originalMethod)
        {
            return !Sync.Delegate(__instance, __originalMethod);
        }

        [SyncDelegate("$this")]
        [MpPrefix(typeof(Building_TurretGun), "<GetGizmos>c__Iterator0", "<>m__0")] // Reset forced target
        [MpPrefix(typeof(Building_TurretGun), "<GetGizmos>c__Iterator0", "<>m__1")] // Toggle hold fire
        static bool TurretGunGizmos(object __instance, MethodBase __originalMethod)
        {
            return !Sync.Delegate(__instance, __originalMethod);
        }

        /*[SyncDelegate("lord")]
        [MpPatch(typeof(Pawn_MindState), "<GetGizmos>c__Iterator0+<GetGizmos>c__AnonStorey2", "<>m__0")]
        static bool GizmoCancelFormingCaravan(object __instance)
        {
            if (!Multiplayer.ShouldSync) return true;
            Sync.Delegate(__instance, MpReflection.GetPropertyOrField(__instance, ""));
            return false;
        }*/
    }

    public static class SyncMarkers
    {
        public static bool manualPriorities;
        public static bool researchToil;

        public static IStoreSettingsParent tabStorage;
        public static Bill billConfig;
        public static Outfit dialogOutfit;
        public static object ThingFilterOwner => tabStorage ?? billConfig ?? (object)dialogOutfit;

        [MpPrefix(typeof(MainTabWindow_Work), "DoManualPrioritiesCheckbox")]
        static void ManualPriorities_Prefix() => manualPriorities = true;

        [MpPostfix(typeof(MainTabWindow_Work), "DoManualPrioritiesCheckbox")]
        static void ManualPriorities_Postfix() => manualPriorities = false;

        [MpPrefix(typeof(JobDriver_Research), "<MakeNewToils>c__Iterator0+<MakeNewToils>c__AnonStorey1", "<>m__0")]
        static void ResearchToil_Prefix() => researchToil = true;

        [MpPostfix(typeof(JobDriver_Research), "<MakeNewToils>c__Iterator0+<MakeNewToils>c__AnonStorey1", "<>m__0")]
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
    }

    public static class SyncResearch
    {
        private static Dictionary<int, float> localResearch = new Dictionary<int, float>();

        [MpPrefix(typeof(ResearchManager), nameof(ResearchManager.ResearchPerformed))]
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
        public static SyncField SyncResearchSpeed =
            Sync.Field(null, "Multiplayer.Client.SyncResearch/researchSpeed[]").SetBufferChanges().InGameLoop();

        public static void ConstantTick()
        {
            if (localResearch.Count == 0) return;

            Extensions.PushFaction(null, Multiplayer.RealPlayerFaction);
            Sync.FieldWatchPrefix();

            foreach (int pawn in localResearch.Keys.ToList())
            {
                SyncResearchSpeed.Watch(null, pawn);
                researchSpeed[pawn] = localResearch[pawn];
                localResearch[pawn] = 0;
            }

            Sync.FieldWatchPostfix();
            Extensions.PopFaction();
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
            ScribeUtil.Look(ref data, "data", LookMode.Value);
        }
    }

}
