using Harmony;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
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

        public static SyncField SyncUseWorkPriorities = Sync.Field(null, "Verse.Current/Game/playSettings", "useWorkPriorities");
        public static SyncField SyncAutoHomeArea = Sync.Field(null, "Verse.Current/Game/playSettings", "autoHomeArea");
        public static SyncField[] SyncDefaultCare = Sync.Fields(
            null,
            "Verse.Find/World/settings",
            "defaultCareForColonyHumanlike",
            "defaultCareForColonyPrisoner",
            "defaultCareForColonyAnimal",
            "defaultCareForNeutralAnimal",
            "defaultCareForNeutralFaction",
            "defaultCareForHostileFaction"
        );

        public static SyncField[] SyncThingFilterHitPoints = Sync.FieldMultiTarget(Sync.thingFilterTarget, "AllowedHitPointsPercents");
        public static SyncField[] SyncThingFilterQuality = Sync.FieldMultiTarget(Sync.thingFilterTarget, "AllowedQualityLevels");

        public static SyncField[] SyncBill = Sync.Fields(
            typeof(Bill),
            null,
            "suspended",
            "ingredientSearchRadius",
            "allowedSkillRange"
        );

        public static SyncField[] SyncBillProduction = Sync.Fields(
            typeof(Bill_Production),
            null,
            "repeatMode",
            "repeatCount",
            "targetCount",
            "storeMode",
            "pauseWhenSatisfied",
            "unpauseWhenYouHave"
        );

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
            Pawn pawn = __instance.GetPropertyOrField("SelPawn") as Pawn;
            SyncMedCare.Watch(pawn);
            SyncGetsFood.Watch(pawn);
            SyncInteractionMode.Watch(pawn);
        }

        [MpPrefix(typeof(HostilityResponseModeUtility), "DrawResponseButton")]
        static void DrawResponseButton(Pawn pawn)
        {
            SyncHostilityResponse.Watch(pawn);
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

        [MpPrefix(typeof(TrainableUtility), "<OpenMasterSelectMenu>c__AnonStorey0", "<>m__0")]
        static void OpenMasterSelectMenu_Inner1(object __instance)
        {
            SyncMaster.Watch(__instance.GetPropertyOrField("p"));
        }

        [MpPrefix(typeof(TrainableUtility), "<OpenMasterSelectMenu>c__AnonStorey1", "<>m__0")]
        static void OpenMasterSelectMenu_Inner2(object __instance)
        {
            SyncMaster.Watch(__instance.GetPropertyOrField("<>f__ref$0/p"));
        }

        [MpPrefix(typeof(Dialog_MedicalDefaults), "DoWindowContents")]
        static void MedicalDefaults()
        {
            SyncDefaultCare.Watch();
        }

        [MpPrefix(typeof(Widgets), "CheckboxLabeled")]
        static void CheckboxLabeled()
        {
            if (MethodMarkers.manualPriorities)
                SyncUseWorkPriorities.Watch();
        }

        [MpPrefix(typeof(PlaySettings), "DoPlaySettingsGlobalControls")]
        static void PlaySettingsControls()
        {
            SyncAutoHomeArea.Watch();
        }

        [MpPrefix(typeof(ThingFilterUI), "DrawHitPointsFilterConfig")]
        static void ThingFilterHitPoints()
        {
            SyncThingFilterHitPoints.Watch(MethodMarkers.ThingFilterOwner);
        }

        [MpPrefix(typeof(ThingFilterUI), "DrawQualityFilterConfig")]
        static void ThingFilterQuality()
        {
            SyncThingFilterQuality.Watch(MethodMarkers.ThingFilterOwner);
        }

        [MpPrefix(typeof(Dialog_BillConfig), "DoWindowContents")]
        static void DialogBillConfig(Dialog_BillConfig __instance)
        {
            Bill_Production bill = __instance.GetPropertyOrField("bill") as Bill_Production;
            SyncBill.Watch(bill);
            SyncBillProduction.Watch(bill);
        }

        [MpPrefix(typeof(Bill), "DoInterface")]
        static void BillInterfaceCard(Bill __instance)
        {
            SyncBill.Watch(__instance);
            SyncBillProduction.Watch(__instance);
        }

        [MpPrefix(typeof(BillRepeatModeUtility), "<MakeConfigFloatMenu>c__AnonStorey0", "<>m__0")]
        [MpPrefix(typeof(BillRepeatModeUtility), "<MakeConfigFloatMenu>c__AnonStorey0", "<>m__1")]
        [MpPrefix(typeof(BillRepeatModeUtility), "<MakeConfigFloatMenu>c__AnonStorey0", "<>m__2")]
        static void FloatMenuBillRepeatMode(object __instance)
        {
            SyncBillProduction.Watch(__instance.GetPropertyOrField("bill"));
        }

        [MpPrefix(typeof(Dialog_BillConfig), "<DoWindowContents>c__AnonStorey0", "<>m__0")]
        static void FloatMenuBillStoreMode(object __instance)
        {
            SyncBillProduction.Watch(__instance.GetPropertyOrField("$this/bill"));
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
            Sync.RegisterSyncMethod(typeof(Zone), "Delete");
            Sync.RegisterSyncMethod(typeof(BillStack), "Delete");
            Sync.RegisterSyncMethod(typeof(BillStack), "Reorder");
            Sync.RegisterSyncMethod(typeof(Pawn_JobTracker), "StartJob", typeof(Expose<Job>), typeof(JobCondition), typeof(ThinkNode), typeof(bool), typeof(bool), typeof(ThinkTreeDef), typeof(JobTag?), typeof(bool));
            Sync.RegisterSyncMethod(typeof(Pawn_JobTracker), "TryTakeOrderedJob", typeof(Expose<Job>), typeof(JobTag));
            Sync.RegisterSyncMethod(typeof(Pawn_JobTracker), "TryTakeOrderedJobPrioritizedWork", typeof(Expose<Job>), typeof(WorkGiver), typeof(IntVec3));
            Sync.RegisterSyncMethod(typeof(StorageSettings), "set_Priority");
            Sync.RegisterSyncMethod(typeof(Pawn), "set_Name", typeof(Expose<Name>));
        }

        public static SyncField SyncTimetable = Sync.Field(typeof(Pawn), "timetable", "times");

        [MpPrefix(typeof(PawnColumnWorker_CopyPasteTimetable), "PasteTo")]
        static bool CopyPasteTimetable(Pawn p)
        {
            return !SyncTimetable.DoSync(p, MpReflection.GetValueStatic(typeof(PawnColumnWorker_CopyPasteTimetable), "clipboard"));
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
            return !SyncThingFilterAllowStuffCategory.DoSync(MethodMarkers.ThingFilterOwner, cat, allow);
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", typeof(SpecialThingFilterDef), typeof(bool))]
        static bool ThingFilter_SetAllow(SpecialThingFilterDef sfDef, bool allow)
        {
            return !SyncThingFilterAllowSpecial.DoSync(MethodMarkers.ThingFilterOwner, sfDef, allow);
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", typeof(ThingDef), typeof(bool))]
        static bool ThingFilter_SetAllow(ThingDef thingDef, bool allow)
        {
            return !SyncThingFilterAllowThing.DoSync(MethodMarkers.ThingFilterOwner, thingDef, allow);
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", typeof(ThingCategoryDef), typeof(bool), typeof(IEnumerable<ThingDef>), typeof(IEnumerable<SpecialThingFilterDef>))]
        static bool ThingFilter_SetAllow(ThingCategoryDef categoryDef, bool allow)
        {
            if (!Multiplayer.ShouldSync || MethodMarkers.ThingFilterOwner == null) return true;

            if (MethodMarkers.tabStorage != null)
                ThingFilter_AllowCategory_HelperStorage(MethodMarkers.tabStorage, categoryDef, allow);
            else if (MethodMarkers.billConfig != null)
                ThingFilter_AllowCategory_HelperBill(MethodMarkers.billConfig, categoryDef, allow);
            else if (MethodMarkers.dialogOutfit != null)
                ThingFilter_AllowCategory_HelperOutfit(MethodMarkers.dialogOutfit, categoryDef, allow);

            return false;
        }

        [MpPrefix(typeof(ThingFilter), "SetDisallowAll")]
        static bool ThingFilter_SetDisallowAll()
        {
            if (!Multiplayer.ShouldSync || MethodMarkers.ThingFilterOwner == null) return true;

            if (MethodMarkers.tabStorage != null)
                ThingFilter_DisallowAll_HelperStorage(MethodMarkers.tabStorage);
            else if (MethodMarkers.billConfig != null)
                ThingFilter_DisallowAll_HelperBill(MethodMarkers.billConfig);
            else if (MethodMarkers.dialogOutfit != null)
                ThingFilter_DisallowAll_HelperOutfit(MethodMarkers.dialogOutfit);

            return false;
        }

        [MpPrefix(typeof(ThingFilter), "SetAllowAll")]
        static bool ThingFilter_SetAllowAll()
        {
            if (!Multiplayer.ShouldSync || MethodMarkers.ThingFilterOwner == null) return true;

            if (MethodMarkers.tabStorage != null)
                ThingFilter_AllowAll_HelperStorage(MethodMarkers.tabStorage);
            else if (MethodMarkers.billConfig != null)
                ThingFilter_AllowAll_HelperBill(MethodMarkers.billConfig);
            else if (MethodMarkers.dialogOutfit != null)
                ThingFilter_AllowAll_HelperOutfit(MethodMarkers.dialogOutfit);

            return false;
        }

        private static IEnumerable<SpecialThingFilterDef> OutfitSpecialFilters = SpecialThingFilterDefOf.AllowNonDeadmansApparel.ToEnumerable();

        [SyncMethod]
        static void ThingFilter_DisallowAll_HelperBill(Bill bill) => bill.ingredientFilter.SetDisallowAll(null, bill.recipe.forceHiddenSpecialFilters);

        [SyncMethod]
        static void ThingFilter_AllowAll_HelperBill(Bill bill) => bill.ingredientFilter.SetAllowAll(bill.recipe.fixedIngredientFilter);

        [SyncMethod]
        static void ThingFilter_DisallowAll_HelperOutfit(Outfit outfit) => outfit.filter.SetDisallowAll(null, OutfitSpecialFilters);

        [SyncMethod]
        static void ThingFilter_AllowAll_HelperOutfit(Outfit outfit) => outfit.filter.SetAllowAll((ThingFilter)MpReflection.GetValueStatic(typeof(Dialog_ManageOutfits), "apparelGlobalFilter"));

        [SyncMethod]
        static void ThingFilter_DisallowAll_HelperStorage(IStoreSettingsParent storage) => storage.GetStoreSettings().filter.SetDisallowAll(null, null);

        [SyncMethod]
        static void ThingFilter_AllowAll_HelperStorage(IStoreSettingsParent storage) => storage.GetStoreSettings().filter.SetAllowAll(storage.GetParentStoreSettings()?.filter);

        [SyncMethod]
        static void ThingFilter_AllowCategory_HelperBill(Bill bill, ThingCategoryDef categoryDef, bool allow) => ThingFilter_AllowCategory_Helper(bill.ingredientFilter, categoryDef, allow, bill.recipe.fixedIngredientFilter, null, bill.recipe.forceHiddenSpecialFilters);

        [SyncMethod]
        static void ThingFilter_AllowCategory_HelperOutfit(Outfit outfit, ThingCategoryDef categoryDef, bool allow) => ThingFilter_AllowCategory_Helper(outfit.filter, categoryDef, allow, (ThingFilter)MpReflection.GetValueStatic(typeof(Dialog_ManageOutfits), "apparelGlobalFilter"), null, OutfitSpecialFilters);

        [SyncMethod]
        static void ThingFilter_AllowCategory_HelperStorage(IStoreSettingsParent storage, ThingCategoryDef categoryDef, bool allow) => ThingFilter_AllowCategory_Helper(storage.GetStoreSettings().filter, categoryDef, allow, storage.GetParentStoreSettings()?.filter, null, null);

        private static MethodInfo CalculateHiddenSpecialFilters = AccessTools.Method(typeof(Listing_TreeThingFilter), "CalculateHiddenSpecialFilters");

        static void ThingFilter_AllowCategory_Helper(ThingFilter filter, ThingCategoryDef categoryDef, bool allow, ThingFilter parentfilter, IEnumerable<ThingDef> forceHiddenDefs, IEnumerable<SpecialThingFilterDef> forceHiddenFilters)
        {
            Listing_TreeThingFilter listing = new Listing_TreeThingFilter(filter, parentfilter, forceHiddenDefs, forceHiddenFilters, null);
            CalculateHiddenSpecialFilters.Invoke(listing, new object[0]);
            filter.SetAllow(categoryDef, allow, forceHiddenDefs, listing.GetPropertyOrField("hiddenSpecialFilters") as IEnumerable<SpecialThingFilterDef>);
        }
    }

    public static class SyncDelegates
    {
        [SyncDelegate]
        [MpPrefix(typeof(FloatMenuMakerMap), "<GotoLocationOption>c__AnonStorey18", "<>m__0")]   // Goto
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddDraftedOrders>c__AnonStorey3", "<>m__0")]      // Arrest
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey7", "<>m__0")]    // Rescue
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey7", "<>m__1")]    // Capture
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey9", "<>m__0")]    // Carry to cryptosleep casket
        [MpPrefix(typeof(HealthCardUtility), "<GenerateSurgeryOption>c__AnonStorey4", "<>m__0")] // Add medical bill
        static bool GeneralSync(object __instance, MethodBase __originalMethod)
        {
            return !Sync.Delegate(__instance, __originalMethod, MapProviderMode.ANY_FIELD);
        }

        [SyncDelegate("$this")]
        [MpPrefix(typeof(Pawn_PlayerSettings), "<GetGizmos>c__Iterator0", "<>m__2")]
        static bool GizmoReleaseAnimals(object __instance, MethodBase __originalMethod)
        {
            return !Sync.Delegate(__instance, __originalMethod, __instance.GetPropertyOrField("$this/pawn"));
        }

        [SyncDelegate("$this")]
        [MpPrefix(typeof(PriorityWork), "<GetGizmos>c__Iterator0", "<>m__0")]
        static bool GizmoClearPrioritizedWork(object __instance, MethodBase __originalMethod)
        {
            return !Sync.Delegate(__instance, __originalMethod, __instance.GetPropertyOrField("$this/pawn"));
        }

        [SyncDelegate]
        [MpPrefix(typeof(ITab_Bills), "<FillTab>c__AnonStorey0", "<>m__0")]
        static bool AddBill(object __instance, MethodBase __originalMethod)
        {
            Sync.selThingContext = __instance.GetPropertyOrField("$this/SelThing") as Building_WorkTable;
            bool result = !Sync.Delegate(__instance, __originalMethod, __instance.GetPropertyOrField("$this/SelThing"));
            Sync.selThingContext = null;

            return result;
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

    public static class MethodMarkers
    {
        public static bool manualPriorities;
        public static IStoreSettingsParent tabStorage;
        public static Bill billConfig;
        public static Outfit dialogOutfit;

        public static object ThingFilterOwner => tabStorage ?? billConfig ?? (object)dialogOutfit;

        [MpPrefix(typeof(MainTabWindow_Work), "DoManualPrioritiesCheckbox")]
        static void ManualPriorities_Prefix() => manualPriorities = true;

        [MpPostfix(typeof(MainTabWindow_Work), "DoManualPrioritiesCheckbox")]
        static void ManualPriorities_Postfix() => manualPriorities = false;

        [MpPrefix(typeof(ITab_Storage), "FillTab")]
        static void TabStorageFillTab_Prefix(ITab_Storage __instance) => tabStorage = (IStoreSettingsParent)__instance.GetPropertyOrField("SelStoreSettingsParent");

        [MpPostfix(typeof(ITab_Storage), "FillTab")]
        static void TabStorageFillTab_Postfix() => tabStorage = null;

        [MpPrefix(typeof(Dialog_BillConfig), "DoWindowContents")]
        static void BillConfig_Prefix(Dialog_BillConfig __instance) => billConfig = (Bill)__instance.GetPropertyOrField("bill");

        [MpPostfix(typeof(Dialog_BillConfig), "DoWindowContents")]
        static void BillConfig_Postfix() => billConfig = null;

        [MpPrefix(typeof(Dialog_ManageOutfits), "DoWindowContents")]
        static void ManageOutfit_Prefix(Dialog_ManageOutfits __instance) => dialogOutfit = (Outfit)__instance.GetPropertyOrField("SelectedOutfit");

        [MpPostfix(typeof(Dialog_ManageOutfits), "DoWindowContents")]
        static void ManageOutfit_Postfix() => dialogOutfit = null;
    }

}
