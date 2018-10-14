using Harmony;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
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
            Sync.RegisterSyncMethods(typeof(TradingWindow));
        }
    }

    public static class SyncFieldsPatches
    {
        public static SyncField SyncMedCare = Sync.Field(typeof(Pawn), "playerSettings", "medCare");
        public static SyncField SyncSelfTend = Sync.Field(typeof(Pawn), "playerSettings", "selfTend");
        public static SyncField SyncHostilityResponse = Sync.Field(typeof(Pawn), "playerSettings", "hostilityResponse");
        public static SyncField SyncGetsFood = Sync.Field(typeof(Pawn), "guest", "GetsFood");
        public static SyncField SyncInteractionMode = Sync.Field(typeof(Pawn), "guest", "interactionMode");

        public static SyncField SyncGodMode = Sync.Field(null, "Verse.DebugSettings/godMode");
        public static SyncField SyncResearchProject = Sync.Field(null, "Verse.Find/ResearchManager/currentProj");
        public static SyncField SyncUseWorkPriorities = Sync.Field(null, "Verse.Current/Game/playSettings", "useWorkPriorities").PostApply(UseWorkPriorities_PostApply);
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
        ).SetBufferChanges();

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

        public static SyncField SyncZoneLabel = Sync.Field(typeof(Zone), "label");

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

        public static SyncField SyncTradeableCount = Sync.Field(typeof(MpTradeableReference), "CountToTransfer").SetBufferChanges();

        [MpPrefix(typeof(HealthCardUtility), "DrawOverviewTab")]
        static void HealthCardUtility1(Pawn pawn)
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

        [MpPrefix(typeof(Dialog_MedicalDefaults), "DoWindowContents")]
        static void MedicalDefaults()
        {
            SyncDefaultCare.Watch();
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

        [MpPrefix(typeof(Dialog_RenameZone), "SetName")]
        static void RenameZone(Dialog_RenameZone __instance)
        {
            SyncZoneLabel.Watch(__instance.zone);
        }

        [MpPrefix(typeof(TransferableUIUtility), "DoCountAdjustInterface")]
        static void TransferableAdjustTo(Transferable trad)
        {
            if (MpTradeSession.current != null && trad is Tradeable tr)
                SyncTradeableCount.Watch(new MpTradeableReference(MpTradeSession.current.sessionId, tr));
        }

        static void UseWorkPriorities_PostApply(object target, object value)
        {
            // From MainTabWindow_Work.DoManualPrioritiesCheckbox
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                if (pawn.Faction == Faction.OfPlayer && pawn.workSettings != null)
                    pawn.workSettings.Notify_UseWorkPrioritiesChanged();
        }
    }

    public class MpTradeableReference
    {
        public int sessionId;
        public Tradeable tradeable;

        public MpTradeableReference(int sessionId, Tradeable tradeable)
        {
            this.sessionId = sessionId;
            this.tradeable = tradeable;
        }

        public int CountToTransfer
        {
            get => tradeable.CountToTransfer;
            set => tradeable.CountToTransfer = value;
        }

        public override int GetHashCode() => tradeable.GetHashCode();
        public override bool Equals(object obj) => obj is MpTradeableReference tr && tr.tradeable == tradeable;
    }

    public static class SyncPatches
    {
        static SyncPatches()
        {
            Sync.RegisterSyncProperty(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted));
            Sync.RegisterSyncProperty(typeof(Pawn_DraftController), nameof(Pawn_DraftController.FireAtWill));
            Sync.RegisterSyncProperty(typeof(Pawn_DrugPolicyTracker), nameof(Pawn_DrugPolicyTracker.CurrentPolicy));
            Sync.RegisterSyncProperty(typeof(Pawn_OutfitTracker), nameof(Pawn_OutfitTracker.CurrentOutfit));
            Sync.RegisterSyncProperty(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.AreaRestriction));
            Sync.RegisterSyncProperty(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.Master));
            Sync.RegisterSyncProperty(typeof(Pawn), nameof(Pawn.Name), new[] { typeof(Expose<Name>) });
            Sync.RegisterSyncProperty(typeof(StorageSettings), nameof(StorageSettings.Priority));
            Sync.RegisterSyncProperty(typeof(CompForbiddable), nameof(CompForbiddable.Forbidden));

            Sync.RegisterSyncMethod(typeof(Pawn_TimetableTracker), nameof(Pawn_TimetableTracker.SetAssignment));
            Sync.RegisterSyncMethod(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority));
            Sync.RegisterSyncMethod(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob), new[] { typeof(Expose<Job>), typeof(JobTag) }).SetHasContext();
            Sync.RegisterSyncMethod(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJobPrioritizedWork), new[] { typeof(Expose<Job>), typeof(WorkGiver), typeof(IntVec3) }).SetHasContext();
            Sync.RegisterSyncMethod(typeof(Pawn_TrainingTracker), nameof(Pawn_TrainingTracker.SetWantedRecursive));
            Sync.RegisterSyncMethod(typeof(Zone), nameof(Zone.Delete));
            Sync.RegisterSyncMethod(typeof(BillStack), nameof(BillStack.AddBill), new[] { typeof(Expose<Bill>) }); // Only used for pasting
            Sync.RegisterSyncMethod(typeof(BillStack), nameof(BillStack.Delete));
            Sync.RegisterSyncMethod(typeof(BillStack), nameof(BillStack.Reorder));
            Sync.RegisterSyncMethod(typeof(Bill_Production), nameof(Bill_Production.SetStoreMode));
            Sync.RegisterSyncMethod(typeof(Building_TurretGun), nameof(Building_TurretGun.OrderAttack));
            Sync.RegisterSyncMethod(typeof(Area), nameof(Area.Invert));
            Sync.RegisterSyncMethod(typeof(Area), nameof(Area.Delete));
            Sync.RegisterSyncMethod(typeof(Area_Allowed), nameof(Area_Allowed.SetLabel));
            Sync.RegisterSyncMethod(typeof(AreaManager), nameof(AreaManager.TryMakeNewAllowed));
            Sync.RegisterSyncMethod(typeof(DrugPolicyDatabase), nameof(DrugPolicyDatabase.MakeNewDrugPolicy));
            Sync.RegisterSyncMethod(typeof(DrugPolicyDatabase), nameof(DrugPolicyDatabase.TryDelete));
            Sync.RegisterSyncMethod(typeof(OutfitDatabase), nameof(OutfitDatabase.MakeNewOutfit));
            Sync.RegisterSyncMethod(typeof(OutfitDatabase), nameof(OutfitDatabase.TryDelete));
            Sync.RegisterSyncMethod(typeof(Building_Bed), nameof(Building_Bed.TryAssignPawn));
            Sync.RegisterSyncMethod(typeof(Building_Bed), nameof(Building_Bed.TryUnassignPawn));
            Sync.RegisterSyncProperty(typeof(Building_Bed), nameof(Building_Bed.Medical));
            Sync.RegisterSyncMethod(typeof(Building_Grave), nameof(Building_Grave.TryAssignPawn));
            Sync.RegisterSyncMethod(typeof(Building_Grave), nameof(Building_Grave.TryUnassignPawn));
            Sync.RegisterSyncMethod(typeof(PawnColumnWorker_Designator), nameof(PawnColumnWorker_Designator.SetValue)); // Virtual but currently not overriden by any subclasses
            Sync.RegisterSyncMethod(typeof(PawnColumnWorker_FollowDrafted), nameof(PawnColumnWorker_FollowDrafted.SetValue));
            Sync.RegisterSyncMethod(typeof(PawnColumnWorker_FollowFieldwork), nameof(PawnColumnWorker_FollowFieldwork.SetValue));
            Sync.RegisterSyncProperty(typeof(CompGatherSpot), nameof(CompGatherSpot.Active));
            Sync.RegisterSyncMethod(typeof(Building_BlastingCharge), nameof(Building_BlastingCharge.Command_Detonate));

            Sync.RegisterSyncMethod(typeof(Building_Grave), nameof(Building_Grave.EjectContents));
            Sync.RegisterSyncMethod(typeof(Building_Casket), nameof(Building_Casket.EjectContents));
            Sync.RegisterSyncMethod(typeof(Building_CryptosleepCasket), nameof(Building_CryptosleepCasket.EjectContents));
            Sync.RegisterSyncMethod(typeof(Building_AncientCryptosleepCasket), nameof(Building_AncientCryptosleepCasket.EjectContents));

            Sync.RegisterSyncMethod(typeof(Building_OrbitalTradeBeacon), nameof(Building_OrbitalTradeBeacon.MakeMatchingStockpile));
            Sync.RegisterSyncMethod(typeof(Building_SunLamp), nameof(Building_SunLamp.MakeMatchingGrowZone));
            Sync.RegisterSyncMethod(typeof(Building_ShipComputerCore), nameof(Building_ShipComputerCore.TryLaunch));
            Sync.RegisterSyncMethod(typeof(CompPower), nameof(CompPower.TryManualReconnect));
            Sync.RegisterSyncMethod(typeof(CompTempControl), nameof(CompTempControl.InterfaceChangeTargetTemperature));
            Sync.RegisterSyncMethod(typeof(CompTransporter), nameof(CompTransporter.CancelLoad), new Type[0]);
            Sync.RegisterSyncMethod(typeof(StorageSettingsClipboard), nameof(StorageSettingsClipboard.PasteInto));
            Sync.RegisterSyncMethod(typeof(Command_SetTargetFuelLevel), "<ProcessInput>m__2"); // Set target fuel level from Dialog_Slider
            Sync.RegisterSyncMethod(typeof(ITab_Pawn_Gear), nameof(ITab_Pawn_Gear.InterfaceDrop)).SetHasContext();
            Sync.RegisterSyncMethod(typeof(ITab_Pawn_Gear), nameof(ITab_Pawn_Gear.InterfaceIngest)).SetHasContext();

            Sync.RegisterSyncMethod(typeof(MpTradeSession), nameof(MpTradeSession.TryExecute));
            Sync.RegisterSyncMethod(typeof(MpTradeSession), nameof(MpTradeSession.Reset));
            Sync.RegisterSyncMethod(typeof(MpTradeSession), nameof(MpTradeSession.ToggleGiftMode));
        }

        static SyncField SyncTimetable = Sync.Field(typeof(Pawn), "timetable", "times");

        [MpPrefix(typeof(PawnColumnWorker_CopyPasteTimetable), nameof(PawnColumnWorker_CopyPasteTimetable.PasteTo))]
        static bool PastePawnTimetable(Pawn p)
        {
            return !SyncTimetable.DoSync(p, PawnColumnWorker_CopyPasteTimetable.clipboard);
        }

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

        static Dialog_ManageOutfits GetDialogOutfits() => Find.WindowStack?.WindowOfType<Dialog_ManageOutfits>();
        static Dialog_ManageDrugPolicies GetDialogDrugPolicies() => Find.WindowStack?.WindowOfType<Dialog_ManageDrugPolicies>();

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

        [SyncMethod]
        public static void AdvanceTime()
        {
            int to = 148 * 1000;
            if (Find.TickManager.TicksGame < to)
            {
                Find.TickManager.ticksGameInt = to;
                Find.Maps[0].AsyncTime().mapTicks = to;
            }
        }

        [SyncMethod]
        public static void SaveMap()
        {
            Map map = Find.Maps[0];
            byte[] mapData = ScribeUtil.WriteExposable(Current.Game, "map", true);
            File.WriteAllBytes($"map_0_{Multiplayer.username}.xml", mapData);
        }
    }

    public static class SyncThingFilters
    {
        static SyncMethod[] SyncThingFilterAllowThing = Sync.MethodMultiTarget(Sync.thingFilterTarget, "SetAllow", new[] { typeof(ThingDef), typeof(bool) });
        static SyncMethod[] SyncThingFilterAllowSpecial = Sync.MethodMultiTarget(Sync.thingFilterTarget, "SetAllow", new[] { typeof(SpecialThingFilterDef), typeof(bool) });
        static SyncMethod[] SyncThingFilterAllowStuffCategory = Sync.MethodMultiTarget(Sync.thingFilterTarget, "SetAllow", new[] { typeof(StuffCategoryDef), typeof(bool) });

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
        [MpPrefix(typeof(FloatMenuMakerMap), "<GotoLocationOption>c__AnonStorey1B", "<>m__0")]      // Goto
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey3", "<>m__0")]       // Arrest
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey7", "<>m__0")]       // Rescue
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey7", "<>m__1")]       // Capture
        [MpPrefix(typeof(FloatMenuMakerMap), "<AddHumanlikeOrders>c__AnonStorey9", "<>m__0")]       // Carry to cryptosleep casket
        [MpPrefix(typeof(HealthCardUtility), "<GenerateSurgeryOption>c__AnonStorey4", "<>m__0")]    // Add medical bill
        [MpPrefix(typeof(Command_SetPlantToGrow), "<ProcessInput>c__AnonStorey0", "<>m__0")]        // Set plant to grow
        [MpPrefix(typeof(Building_Bed), "<ToggleForPrisonersByInterface>c__AnonStorey3", "<>m__0")] // Toggle bed for prisoners
        [MpPrefix(typeof(ITab_Bills), "<FillTab>c__AnonStorey0", "<>m__0")]                         // Add bill
        [MpPrefix(typeof(CompLongRangeMineralScanner), "<CompGetGizmosExtra>c__Iterator0+<CompGetGizmosExtra>c__AnonStorey1", "<>m__0")] // Select mineral to scan for
        static bool GeneralSync(object __instance, MethodBase __originalMethod)
        {
            return !Sync.Delegate(__instance, __originalMethod);
        }

        [SyncDelegate("$this")]
        [MpPrefix(typeof(CompFlickable), "<CompGetGizmosExtra>c__Iterator0", "<>m__1")] // Toggle flick designation
        [MpPrefix(typeof(Pawn_PlayerSettings), "<GetGizmos>c__Iterator0", "<>m__1")]    // Toggle release animals
        [MpPrefix(typeof(Building_TurretGun), "<GetGizmos>c__Iterator0", "<>m__1")]     // Toggle turret hold fire
        [MpPrefix(typeof(Building_Trap), "<GetGizmos>c__Iterator0", "<>m__1")]          // Toggle trap auto-rearm
        [MpPrefix(typeof(Building_Door), "<GetGizmos>c__Iterator0", "<>m__1")]          // Toggle door hold open
        [MpPrefix(typeof(Zone_Growing), "<GetGizmos>c__Iterator0", "<>m__1")]           // Toggle zone allow sow
        static bool GeneralIteratorSync_Toggle(object __instance, MethodBase __originalMethod)
        {
            return !Sync.Delegate(__instance, __originalMethod);
        }

        [SyncDelegate("$this")]
        [MpPrefix(typeof(PriorityWork), "<GetGizmos>c__Iterator0", "<>m__0")]               // Clear prioritized work
        [MpPrefix(typeof(Building_TurretGun), "<GetGizmos>c__Iterator0", "<>m__0")]         // Reset forced target
        [MpPrefix(typeof(UnfinishedThing), "<GetGizmos>c__Iterator0", "<>m__0")]            // Cancel unfinished thing
        [MpPrefix(typeof(CompTempControl), "<CompGetGizmosExtra>c__Iterator0", "<>m__0")]   // Reset temperature
        static bool GeneralIteratorSync_Action(object __instance, MethodBase __originalMethod)
        {
            return !Sync.Delegate(__instance, __originalMethod);
        }

        [SyncDelegate("changeableProjectile", "<>f__ref$0/$this")]
        [MpPrefix(typeof(Building_TurretGun), "<GetGizmos>c__Iterator0+<GetGizmos>c__AnonStorey2", "<>m__0")] // Extract shell
        static bool TurretGunGizmos_RemoveShell(object __instance, MethodBase __originalMethod)
        {
            return !Sync.Delegate(__instance, __originalMethod);
        }

        [SyncDelegate("<>f__ref$0/$this", "things")]
        [MpPrefix(typeof(Designator), "<>c__Iterator0+<>c__AnonStorey1", "<>m__0")]
        static bool DesignateAll(object __instance, MethodBase __originalMethod)
        {
            return !Sync.Delegate(__instance, __originalMethod);
        }

        [SyncDelegate("<>f__ref$0/$this", "<>f__ref$3/designation", "designations")]
        [MpPrefix(typeof(Designator), "<>c__Iterator0+<>c__AnonStorey2", "<>m__0")]
        static bool RemoveAllDesignations(object __instance, MethodBase __originalMethod)
        {
            return !Sync.Delegate(__instance, __originalMethod);
        }
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
            ScribeUtil.Look(ref data, "data", LookMode.Value);
        }
    }

}
