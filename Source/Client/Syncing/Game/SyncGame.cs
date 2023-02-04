using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Multiplayer.Client
{
    public static class SyncGame
    {
        public static void Init()
        {
            static void TryInit(string name, Action action)
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Log.Error($"Exception during {name} initialization: {e}");
                    Multiplayer.loadingErrors = true;
                }
            }

            TryInit("SyncMethods", SyncMethods.Init);
            TryInit("SyncFields", SyncFields.Init);
            TryInit("SyncDelegates", SyncDelegates.Init);
            TryInit("SyncThingFilters", SyncThingFilters.Init);
            TryInit("SyncActions", SyncActions.Init);

            //RuntimeHelpers.RunClassConstructor(typeof(SyncResearch).TypeHandle);

            SyncFieldUtil.ApplyWatchFieldPatches(typeof(SyncFields));
        }
    }

    public static class SyncMarkers
    {
        public static bool manualPriorities;
        public static bool researchToil;
        public static DrugPolicy drugPolicy;

        public static bool drawingThingFilter;
        public static TabStorageWrapper tabStorage;
        public static BillConfigWrapper billConfig;
        public static OutfitWrapper dialogOutfit;
        public static FoodRestrictionWrapper foodRestriction;
        public static PenAutocutWrapper penAutocut;
        public static PenAnimalsWrapper penAnimals;
        public static DefaultAutocutWrapper windTurbine;

        public static ThingFilterContext DrawnThingFilter =>
            !drawingThingFilter ? null :
            tabStorage ?? billConfig ?? dialogOutfit ?? foodRestriction ?? penAutocut ?? penAnimals ?? (ThingFilterContext)windTurbine;

        #region Misc Markers
        [MpPrefix(typeof(MainTabWindow_Work), "DoManualPrioritiesCheckbox")]
        static void ManualPriorities_Prefix() => manualPriorities = true;

        [MpPostfix(typeof(MainTabWindow_Work), "DoManualPrioritiesCheckbox")]
        static void ManualPriorities_Postfix() => manualPriorities = false;

        [MpPrefix(typeof(JobDriver_Research), nameof(JobDriver_Research.MakeNewToils), lambdaOrdinal: 0)]
        static void ResearchToil_Prefix() => researchToil = true;

        [MpPostfix(typeof(JobDriver_Research), nameof(JobDriver_Research.MakeNewToils), lambdaOrdinal: 0)]
        static void ResearchToil_Postfix() => researchToil = false;

        [MpPostfix(typeof(Dialog_ManageOutfits), "DoWindowContents")]
        static void ManageOutfit_Postfix() => dialogOutfit = null;

        [MpPrefix(typeof(Dialog_ManageDrugPolicies), "DoWindowContents")]
        static void ManageDrugPolicy_Prefix(Dialog_ManageDrugPolicies __instance) => drugPolicy = __instance.SelectedPolicy;
        #endregion

        #region ThingFilter Markers
        [MpPrefix(typeof(ITab_Storage), "FillTab")]
        static void TabStorageFillTab_Prefix(ITab_Storage __instance)
        {
            var selThing = __instance.SelObject;
            var selParent = __instance.SelStoreSettingsParent;
            // If SelStoreSettingsParent is null, just return early. There'll be nothing to sync.
            // The map could potentially be null - for example, if we're syncing mortar. The mortar hun itself
            // holds the store settings, and turret guns don't have a map/location assigned - so we sync their parent.
            // Because of that, we check if the parent is not null.
            if (selParent == null || selThing is Thing { Map: null } or ThingComp { parent: { Map: null } })
                return;
            tabStorage = new(selParent);
        }

        [MpPostfix(typeof(ITab_Storage), "FillTab")]
        static void TabStorageFillTab_Postfix() => tabStorage = null;

        [MpPrefix(typeof(Dialog_BillConfig), "DoWindowContents")]
        static void BillConfig_Prefix(Dialog_BillConfig __instance) =>
            billConfig = new(__instance.bill);

        [MpPostfix(typeof(Dialog_BillConfig), "DoWindowContents")]
        static void BillConfig_Postfix() => billConfig = null;

        [MpPrefix(typeof(Dialog_ManageOutfits), "DoWindowContents")]
        static void ManageOutfit_Prefix(Dialog_ManageOutfits __instance) =>
            dialogOutfit = new(__instance.SelectedOutfit);

        [MpPostfix(typeof(Dialog_ManageOutfits), "DoWindowContents")]
        static void ManageDrugPolicy_Postfix() => drugPolicy = null;

        [MpPrefix(typeof(Dialog_ManageFoodRestrictions), "DoWindowContents")]
        static void ManageFoodRestriction_Prefix(Dialog_ManageFoodRestrictions __instance) =>
            foodRestriction = new(__instance.SelectedFoodRestriction);

        [MpPostfix(typeof(Dialog_ManageFoodRestrictions), "DoWindowContents")]
        static void ManageFoodRestriction_Postfix() => foodRestriction = null;

        [MpPrefix(typeof(ITab_PenAutoCut), "FillTab")]
        static void TabPenAutocutFillTab_Prefix(ITab_PenAutoCut __instance) =>
            penAutocut = new(__instance.SelectedCompAnimalPenMarker);

        [MpPostfix(typeof(ITab_PenAutoCut), "FillTab")]
        static void TabPenAutocutFillTab_Postfix() => penAutocut = null;

        [MpPrefix(typeof(ITab_PenAnimals), "FillTab")]
        static void TabPenAnimalsFillTab_Prefix(ITab_PenAnimals __instance) =>
            penAnimals = new(__instance.SelectedCompAnimalPenMarker);

        [MpPostfix(typeof(ITab_PenAnimals), "FillTab")]
        static void TabPenAnimalsFillTab_Postfix() => penAnimals = null;

        [MpPrefix(typeof(ITab_WindTurbineAutoCut), nameof(ITab_WindTurbineAutoCut.FillTab))]
        static void TabWindTurbineAutocutFillTab_Prefix(ITab_WindTurbineAutoCut __instance) =>
            windTurbine = new(__instance.AutoCut);

        [MpPostfix(typeof(ITab_WindTurbineAutoCut), nameof(ITab_WindTurbineAutoCut.FillTab))]
        static void TabWindTurbineAutocutFillTab_Postfix(ITab_WindTurbineAutoCut __instance) => windTurbine = null;

        [MpPrefix(typeof(ThingFilterUI), "DoThingFilterConfigWindow")]
        static void ThingFilterUI_Prefix() => drawingThingFilter = true;

        [MpPostfix(typeof(ThingFilterUI), "DoThingFilterConfigWindow")]
        static void ThingFilterUI_Postfix() => drawingThingFilter = false;
        #endregion
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

            SyncFieldUtil.FieldWatchPrefix();

            foreach (int pawn in localResearch.Keys.ToList())
            {
                SyncResearchSpeed.Watch(null, pawn);
                researchSpeed[pawn] = localResearch[pawn];
                localResearch[pawn] = 0;
            }

            SyncFieldUtil.FieldWatchPostfix();
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
