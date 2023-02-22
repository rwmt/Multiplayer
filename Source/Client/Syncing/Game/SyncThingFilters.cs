using System;
using Multiplayer.Common;
using RimWorld;
using System.Collections.Generic;
using Multiplayer.API;
using Multiplayer.Client.Experimental;
using Verse;

namespace Multiplayer.Client
{
    static class ThingFilterMarkers
    {
        public static bool drawingThingFilter;

        private static ThingFilterContext thingFilterContext;

        public static ThingFilterContext DrawnThingFilter
        {
            get => drawingThingFilter ? thingFilterContext : null;
            set
            {
                if (value != null && thingFilterContext != null)
                    throw new Exception("Thing filter context already set!");

                thingFilterContext = value;
            }
        }

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
            DrawnThingFilter = new TabStorageWrapper(selParent);
        }

        [MpPostfix(typeof(ITab_Storage), "FillTab")]
        static void TabStorageFillTab_Postfix() => DrawnThingFilter = null;

        [MpPrefix(typeof(Dialog_BillConfig), "DoWindowContents")]
        static void BillConfig_Prefix(Dialog_BillConfig __instance) =>
            DrawnThingFilter = new BillConfigWrapper(__instance.bill);

        [MpPostfix(typeof(Dialog_BillConfig), "DoWindowContents")]
        static void BillConfig_Postfix() => DrawnThingFilter = null;

        [MpPrefix(typeof(Dialog_ManageOutfits), "DoWindowContents")]
        static void ManageOutfit_Prefix(Dialog_ManageOutfits __instance) =>
            DrawnThingFilter = new OutfitWrapper(__instance.SelectedOutfit);

        [MpPostfix(typeof(Dialog_ManageOutfits), "DoWindowContents")]
        static void ManageOutfit_Postfix() => DrawnThingFilter = null;

        [MpPrefix(typeof(Dialog_ManageFoodRestrictions), "DoWindowContents")]
        static void ManageFoodRestriction_Prefix(Dialog_ManageFoodRestrictions __instance) =>
            DrawnThingFilter = new FoodRestrictionWrapper(__instance.SelectedFoodRestriction);

        [MpPostfix(typeof(Dialog_ManageFoodRestrictions), "DoWindowContents")]
        static void ManageFoodRestriction_Postfix() => DrawnThingFilter = null;

        [MpPrefix(typeof(ITab_PenAutoCut), "FillTab")]
        static void TabPenAutocutFillTab_Prefix(ITab_PenAutoCut __instance) =>
            DrawnThingFilter = new PenAutocutWrapper(__instance.SelectedCompAnimalPenMarker);

        [MpPostfix(typeof(ITab_PenAutoCut), "FillTab")]
        static void TabPenAutocutFillTab_Postfix() => DrawnThingFilter = null;

        [MpPrefix(typeof(ITab_PenAnimals), "FillTab")]
        static void TabPenAnimalsFillTab_Prefix(ITab_PenAnimals __instance) =>
            DrawnThingFilter = new PenAnimalsWrapper(__instance.SelectedCompAnimalPenMarker);

        [MpPostfix(typeof(ITab_PenAnimals), "FillTab")]
        static void TabPenAnimalsFillTab_Postfix() => DrawnThingFilter = null;

        [MpPrefix(typeof(ITab_WindTurbineAutoCut), nameof(ITab_WindTurbineAutoCut.FillTab))]
        static void TabWindTurbineAutocutFillTab_Prefix(ITab_WindTurbineAutoCut __instance) =>
            DrawnThingFilter = new DefaultAutocutWrapper(__instance.AutoCut);

        [MpPostfix(typeof(ITab_WindTurbineAutoCut), nameof(ITab_WindTurbineAutoCut.FillTab))]
        static void TabWindTurbineAutocutFillTab_Postfix(ITab_WindTurbineAutoCut __instance) => DrawnThingFilter = null;

        [MpPrefix(typeof(ThingFilterUI), "DoThingFilterConfigWindow")]
        static void ThingFilterUI_Prefix() => drawingThingFilter = true;

        [MpPostfix(typeof(ThingFilterUI), "DoThingFilterConfigWindow")]
        static void ThingFilterUI_Postfix() => drawingThingFilter = false;
        #endregion
    }

    public static class SyncThingFilters
    {
        [MpPrefix(typeof(ThingFilter), "SetAllow", new[] { typeof(StuffCategoryDef), typeof(bool) })]
        static bool ThingFilter_SetAllow(StuffCategoryDef cat, bool allow)
        {
            if (!Multiplayer.ShouldSync || ThingFilterMarkers.DrawnThingFilter == null) return true;
            AllowStuffCat_Helper(ThingFilterMarkers.DrawnThingFilter, cat, allow);
            return false;
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", new[] { typeof(SpecialThingFilterDef), typeof(bool) })]
        static bool ThingFilter_SetAllow(SpecialThingFilterDef sfDef, bool allow)
        {
            if (!Multiplayer.ShouldSync || ThingFilterMarkers.DrawnThingFilter == null) return true;
            AllowSpecial_Helper(ThingFilterMarkers.DrawnThingFilter, sfDef, allow);
            return false;
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", new[] { typeof(ThingDef), typeof(bool) })]
        static bool ThingFilter_SetAllow(ThingDef thingDef, bool allow)
        {
            if (!Multiplayer.ShouldSync || ThingFilterMarkers.DrawnThingFilter == null) return true;
            AllowThing_Helper(ThingFilterMarkers.DrawnThingFilter, thingDef, allow);
            return false;
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", new[] { typeof(ThingCategoryDef), typeof(bool), typeof(IEnumerable<ThingDef>), typeof(IEnumerable<SpecialThingFilterDef>) })]
        static bool ThingFilter_SetAllow(ThingCategoryDef categoryDef, bool allow)
        {
            if (!Multiplayer.ShouldSync || ThingFilterMarkers.DrawnThingFilter == null) return true;
            AllowCategory_Helper(ThingFilterMarkers.DrawnThingFilter, categoryDef, allow);
            return false;
        }

        [MpPrefix(typeof(ThingFilter), "SetAllowAll")]
        static bool ThingFilter_SetAllowAll()
        {
            if (!Multiplayer.ShouldSync || ThingFilterMarkers.DrawnThingFilter == null) return true;
            AllowAll_Helper(ThingFilterMarkers.DrawnThingFilter);
            return false;
        }

        [MpPrefix(typeof(ThingFilter), "SetDisallowAll")]
        static bool ThingFilter_SetDisallowAll()
        {
            if (!Multiplayer.ShouldSync || ThingFilterMarkers.DrawnThingFilter == null) return true;
            DisallowAll_Helper(ThingFilterMarkers.DrawnThingFilter);
            return false;
        }

        [SyncMethod]
        static void AllowStuffCat_Helper(ThingFilterContext context, StuffCategoryDef cat, bool allow)
        {
            context.Filter.SetAllow(cat, allow);
        }

        [SyncMethod]
        private static void AllowSpecial_Helper(ThingFilterContext context, SpecialThingFilterDef sfDef, bool allow)
        {
            context.Filter.SetAllow(sfDef, allow);
        }

        [SyncMethod]
        private static void AllowThing_Helper(ThingFilterContext context, ThingDef thingDef, bool allow)
        {
            context.Filter.SetAllow(thingDef, allow);
        }

        [SyncMethod]
        private static void DisallowAll_Helper(ThingFilterContext context)
        {
            context.Filter.SetDisallowAll(null, context.HiddenFilters);
        }

        [SyncMethod]
        private static void AllowAll_Helper(ThingFilterContext context)
        {
            context.Filter.SetAllowAll(context.ParentFilter);
        }

        [SyncMethod]
        private static void AllowCategory_Helper(ThingFilterContext context, ThingCategoryDef categoryDef, bool allow)
        {
            var node = new TreeNode_ThingCategory(categoryDef);

            context.Filter.SetAllow(
                categoryDef,
                allow,
                null,
                Listing_TreeThingFilter
                    .CalculateHiddenSpecialFilters(node, context.ParentFilter)
                    .ConcatIfNotNull(context.HiddenFilters)
            );
        }
    }

    public record TabStorageWrapper(IStoreSettingsParent Storage) : ThingFilterContext
    {
        public override ThingFilter Filter => Storage.GetStoreSettings().filter;
        public override ThingFilter ParentFilter => Storage.GetParentStoreSettings()?.filter;
    }

    public record BillConfigWrapper(Bill Bill) : ThingFilterContext
    {
        public override ThingFilter Filter => Bill.ingredientFilter;
        public override ThingFilter ParentFilter => Bill.recipe.fixedIngredientFilter;
        public override IEnumerable<SpecialThingFilterDef> HiddenFilters => Bill.recipe.forceHiddenSpecialFilters;
    }

    public record OutfitWrapper(Outfit Outfit) : ThingFilterContext
    {
        public override ThingFilter Filter => Outfit.filter;
        public override ThingFilter ParentFilter => Dialog_ManageOutfits.apparelGlobalFilter;
        public override IEnumerable<SpecialThingFilterDef> HiddenFilters => SpecialThingFilterDefOf.AllowNonDeadmansApparel.ToEnumerable();
    }

    public record FoodRestrictionWrapper(FoodRestriction Food) : ThingFilterContext
    {
        public override ThingFilter Filter => Food.filter;
        public override ThingFilter ParentFilter => Dialog_ManageFoodRestrictions.foodGlobalFilter;
        public override IEnumerable<SpecialThingFilterDef> HiddenFilters => SpecialThingFilterDefOf.AllowFresh.ToEnumerable();
    }

    public record PenAnimalsWrapper(CompAnimalPenMarker Pen) : ThingFilterContext
    {
        public override ThingFilter Filter => Pen.AnimalFilter;
        public override ThingFilter ParentFilter => AnimalPenUtility.GetFixedAnimalFilter();
    }

    public record PenAutocutWrapper(CompAnimalPenMarker Pen) : ThingFilterContext
    {
        public override ThingFilter Filter => Pen.AutoCutFilter;
        public override ThingFilter ParentFilter => Pen.parent.Map.animalPenManager.GetFixedAutoCutFilter();
        public override IEnumerable<SpecialThingFilterDef> HiddenFilters => SpecialThingFilterDefOf.AllowFresh.ToEnumerable();
    }

    public record DefaultAutocutWrapper(CompAutoCut AutoCut) : ThingFilterContext
    {
        public override ThingFilter Filter => AutoCut.AutoCutFilter;
        public override ThingFilter ParentFilter => AutoCut.GetFixedAutoCutFilter();
    }

}
