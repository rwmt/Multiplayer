using RimWorld;
using System;
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
            if (MethodMarkers.tabStorage != null)
                SyncThingFilterHitPoints.Watch(MethodMarkers.tabStorage);
            else if (MethodMarkers.billConfig != null)
                SyncThingFilterHitPoints.Watch(MethodMarkers.billConfig);
        }

        [MpPrefix(typeof(ThingFilterUI), "DrawQualityFilterConfig")]
        static void ThingFilterQuality()
        {
            if (MethodMarkers.tabStorage != null)
                SyncThingFilterQuality.Watch(MethodMarkers.tabStorage);
            else if (MethodMarkers.billConfig != null)
                SyncThingFilterQuality.Watch(MethodMarkers.billConfig);
        }
    }

    public static class SyncPatches
    {
        public static SyncMethod SyncSetAssignment = Sync.Method(typeof(Pawn), "timetable", "SetAssignment");
        public static SyncMethod SyncSetWorkPriority = Sync.Method(typeof(Pawn), "workSettings", "SetPriority");
        public static SyncMethod SyncSetDrafted = Sync.Method(typeof(Pawn), "drafter", "set_Drafted");
        public static SyncMethod SyncSetFireAtWill = Sync.Method(typeof(Pawn), "drafter", "set_FireAtWill");
        public static SyncMethod[] SyncSetStoragePriority = Sync.MethodMultiTarget(Sync.storageTarget, "set_Priority");

        public static SyncMethod SyncStartJob = Sync.Method(typeof(Pawn), "jobs", "StartJob", typeof(Expose<Job>), typeof(JobCondition), typeof(ThinkNode), typeof(bool), typeof(bool), typeof(ThinkTreeDef), typeof(JobTag?), typeof(bool));
        public static SyncMethod SyncTryTakeOrderedJob = Sync.Method(typeof(Pawn), "jobs", "TryTakeOrderedJob", typeof(Expose<Job>), typeof(JobTag));
        public static SyncMethod SyncTryTakeOrderedJobPrioritizedWork = Sync.Method(typeof(Pawn), "jobs", "TryTakeOrderedJobPrioritizedWork", typeof(Expose<Job>), typeof(WorkGiver), typeof(IntVec3));

        public static SyncMethod SyncAddBill = Sync.Method(typeof(BillStack), "AddBill", typeof(Expose<Bill>));
        public static SyncMethod SyncDeleteBill = Sync.Method(typeof(BillStack), "Delete");
        public static SyncMethod SyncReorderBill = Sync.Method(typeof(BillStack), "Reorder");

        public static SyncField SyncTimetable = Sync.Field(typeof(Pawn), "timetable", "times");

        [MpPrefix(typeof(Pawn_TimetableTracker), "SetAssignment")]
        static bool SetTimetableAssignment(Pawn_TimetableTracker __instance, int hour, TimeAssignmentDef ta)
        {
            return !SyncSetAssignment.DoSync(__instance.GetPropertyOrField("pawn"), hour, ta);
        }

        [MpPrefix(typeof(PawnColumnWorker_CopyPasteTimetable), "PasteTo")]
        static bool CopyPasteTimetable(Pawn p)
        {
            return !SyncTimetable.DoSync(p, MpReflection.GetPropertyOrField("PawnColumnWorker_CopyPasteTimetable.clipboard"));
        }

        [MpPrefix(typeof(Pawn_WorkSettings), "SetPriority")]
        static bool SetWorkPriority(Pawn_WorkSettings __instance, WorkTypeDef w, int priority)
        {
            return !SyncSetWorkPriority.DoSync(__instance.GetPropertyOrField("pawn"), w, priority);
        }

        [MpPrefix(typeof(Pawn_DraftController), "set_Drafted")]
        static bool SetDrafted(Pawn_DraftController __instance, bool value)
        {
            return !SyncSetDrafted.DoSync(__instance.pawn, value);
        }

        [MpPrefix(typeof(Pawn_DraftController), "set_FireAtWill")]
        static bool SetFireAtWill(Pawn_DraftController __instance, bool value)
        {
            return !SyncSetFireAtWill.DoSync(__instance.pawn, value);
        }

        [MpPrefix(typeof(Pawn_JobTracker), "StartJob")]
        static bool StartJob(Pawn_JobTracker __instance, Job newJob, JobCondition lastJobEndCondition, ThinkNode jobGiver, bool resumeCurJobAfterwards, bool cancelBusyStances, ThinkTreeDef thinkTree, JobTag? tag, bool fromQueue)
        {
            return !SyncSetFireAtWill.DoSync(__instance.GetPropertyOrField("pawn"), newJob, lastJobEndCondition, jobGiver, resumeCurJobAfterwards, cancelBusyStances, thinkTree, tag, fromQueue);
        }

        [MpPrefix(typeof(StorageSettings), "set_Priority")]
        static bool StorageSetPriority(StorageSettings __instance, StoragePriority value)
        {
            return !SyncSetStoragePriority.DoSync(__instance.owner, value);
        }

        [MpPrefix(typeof(Pawn_JobTracker), "TryTakeOrderedJob")]
        static bool TryTakeOrderedJob(Pawn_JobTracker __instance, Job job, JobTag tag)
        {
            return !SyncTryTakeOrderedJob.DoSync(__instance.GetPropertyOrField("pawn"), job, tag);
        }

        [MpPrefix(typeof(Pawn_JobTracker), "TryTakeOrderedJobPrioritizedWork")]
        static bool TryTakeOrderedJobPrioritizedWork(Pawn_JobTracker __instance, Job job, WorkGiver giver, IntVec3 cell)
        {
            return !SyncTryTakeOrderedJobPrioritizedWork.DoSync(__instance.GetPropertyOrField("pawn"), job, giver, cell);
        }

        [MpPrefix(typeof(BillStack), "AddBill")]
        static bool AddBill(BillStack __instance, Bill bill)
        {
            return !SyncAddBill.DoSync(__instance, bill);
        }

        [MpPrefix(typeof(BillStack), "Delete")]
        static bool DeleteBill(BillStack __instance, Bill bill)
        {
            return !SyncDeleteBill.DoSync(__instance, bill);
        }

        [MpPrefix(typeof(BillStack), "Reorder")]
        static bool ReorderBill(BillStack __instance, Bill bill, int offset)
        {
            return !SyncReorderBill.DoSync(__instance, bill, offset);
        }
    }

    public static class SyncDelegates
    {
        [SyncDelegate]
        [MpPrefix(typeof(FloatMenuMakerMap), "<GotoLocationOption>c__AnonStorey18", "<>m__0")] // Goto
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddDraftedOrders>c__AnonStorey3", "<>m__0")] // Arrest
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey7", "<>m__0")] // Rescue
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey7", "<>m__1")] // Capture
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey9", "<>m__0")] // Carry to cryptosleep casket
        static bool FloatMenuGeneral(object __instance)
        {
            return !Sync.Delegate(__instance, MapProviderMode.ANY_FIELD);
        }

        [SyncDelegate("$this")]
        [MpPrefix(typeof(Pawn_PlayerSettings), "<GetGizmos>c__Iterator0", "<>m__2")]
        static bool GizmoReleaseAnimals(object __instance)
        {
            return !Sync.Delegate(__instance, __instance.GetPropertyOrField("$this/pawn"));
        }

        [SyncDelegate("$this")]
        [MpPrefix(typeof(PriorityWork), "<GetGizmos>c__Iterator0", "<>m__0")]
        static bool GizmoClearPrioritizedWork(object __instance)
        {
            return !Sync.Delegate(__instance, __instance.GetPropertyOrField("$this/pawn"));
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
        static void ManageOutfit_Postfix() => billConfig = null;
    }

}
