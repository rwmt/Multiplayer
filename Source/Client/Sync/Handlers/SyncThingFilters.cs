using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client
{
    public static class SyncThingFilters
    {
        static SyncMethod[] SyncThingFilterAllowThing;
        static SyncMethod[] SyncThingFilterAllowSpecial;
        static SyncMethod[] SyncThingFilterAllowStuffCategory;

        public static void Init()
        {
            SyncThingFilterAllowThing = Sync.MethodMultiTarget(Sync.thingFilterTarget, "SetAllow", new SyncType[] { typeof(ThingDef), typeof(bool) });
            SyncThingFilterAllowSpecial = Sync.MethodMultiTarget(Sync.thingFilterTarget, "SetAllow", new SyncType[] { typeof(SpecialThingFilterDef), typeof(bool) });
            SyncThingFilterAllowStuffCategory = Sync.MethodMultiTarget(Sync.thingFilterTarget, "SetAllow", new SyncType[] { typeof(StuffCategoryDef), typeof(bool) });
        }

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

        private static IEnumerable<SpecialThingFilterDef> OutfitSpecialFilters
            => SpecialThingFilterDefOf.AllowNonDeadmansApparel.ToEnumerable();

        private static IEnumerable<SpecialThingFilterDef> FoodSpecialFilters
            => SpecialThingFilterDefOf.AllowFresh.ToEnumerable();

        [SyncMethod]
        static void ThingFilter_DisallowAll_HelperStorage(IStoreSettingsParent storage)
            => storage.GetStoreSettings().filter.SetDisallowAll(null, null);

        [SyncMethod]
        static void ThingFilter_DisallowAll_HelperBill(Bill bill)
            => bill.ingredientFilter.SetDisallowAll(null, bill.recipe.forceHiddenSpecialFilters);

        [SyncMethod]
        static void ThingFilter_DisallowAll_HelperOutfit(Outfit outfit)
            => outfit.filter.SetDisallowAll(null, OutfitSpecialFilters);

        [SyncMethod]
        static void ThingFilter_DisallowAll_HelperFood(FoodRestriction food)
            => food.filter.SetDisallowAll(null, FoodSpecialFilters);

        [SyncMethod]
        static void ThingFilter_AllowAll_HelperStorage(IStoreSettingsParent storage)
            => storage.GetStoreSettings().filter.SetAllowAll(storage.GetParentStoreSettings()?.filter);

        [SyncMethod]
        static void ThingFilter_AllowAll_HelperBill(Bill bill)
            => bill.ingredientFilter.SetAllowAll(bill.recipe.fixedIngredientFilter);

        [SyncMethod]
        static void ThingFilter_AllowAll_HelperOutfit(Outfit outfit)
            => outfit.filter.SetAllowAll(Dialog_ManageOutfits.apparelGlobalFilter);

        [SyncMethod]
        static void ThingFilter_AllowAll_HelperFood(FoodRestriction food)
            => food.filter.SetAllowAll(Dialog_ManageFoodRestrictions.foodGlobalFilter);

        [SyncMethod]
        static void ThingFilter_AllowCategory_HelperStorage(IStoreSettingsParent storage, ThingCategoryDef categoryDef, bool allow)
            => ThingFilter_AllowCategory_Helper(storage.GetStoreSettings().filter, categoryDef, allow, storage.GetParentStoreSettings()?.filter, null, null);

        [SyncMethod]
        static void ThingFilter_AllowCategory_HelperBill(Bill bill, ThingCategoryDef categoryDef, bool allow)
            => ThingFilter_AllowCategory_Helper(bill.ingredientFilter, categoryDef, allow, bill.recipe.fixedIngredientFilter, null, bill.recipe.forceHiddenSpecialFilters);

        [SyncMethod]
        static void ThingFilter_AllowCategory_HelperOutfit(Outfit outfit, ThingCategoryDef categoryDef, bool allow)
            => ThingFilter_AllowCategory_Helper(outfit.filter, categoryDef, allow, Dialog_ManageOutfits.apparelGlobalFilter, null, OutfitSpecialFilters);

        [SyncMethod]
        static void ThingFilter_AllowCategory_HelperFood(FoodRestriction food, ThingCategoryDef categoryDef, bool allow)
            => ThingFilter_AllowCategory_Helper(food.filter, categoryDef, allow, Dialog_ManageFoodRestrictions.foodGlobalFilter, null, FoodSpecialFilters);

        static void ThingFilter_AllowCategory_Helper(ThingFilter filter, ThingCategoryDef categoryDef, bool allow, ThingFilter parentFilter, IEnumerable<ThingDef> forceHiddenDefs, IEnumerable<SpecialThingFilterDef> forceHiddenFilters)
        {
            var node = new TreeNode_ThingCategory(categoryDef);
            filter.SetAllow(
                categoryDef,
                allow,
                forceHiddenDefs,
                Listing_TreeThingFilter.CalculateHiddenSpecialFilters(node, parentFilter).ConcatIfNotNull(forceHiddenFilters)
            );
        }
    }

}
