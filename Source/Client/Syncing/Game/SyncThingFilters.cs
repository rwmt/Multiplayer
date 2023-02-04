using Multiplayer.Common;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client
{
    public static class SyncThingFilters
    {
        static SyncMethod[] AllowThing;
        static SyncMethod[] AllowSpecial;
        static SyncMethod[] AllowStuffCategory;
        static SyncMethod[] AllowCategory;
        static SyncMethod[] AllowAll;
        static SyncMethod[] DisallowAll;

        public static MultiTarget ThingFilterTarget = new MultiTarget()
        {
            { typeof(TabStorageWrapper) },
            { typeof(BillConfigWrapper) },
            { typeof(OutfitWrapper) },
            { typeof(FoodRestrictionWrapper) },
            { typeof(PenAnimalsWrapper) },
            { typeof(PenAutocutWrapper) },
            { typeof(DefaultAutocutWrapper) },
        };

        public static void Init()
        {
            AllowThing = Sync.MethodMultiTarget(ThingFilterTarget, nameof(ThingFilterContext.AllowThing_Helper));
            AllowSpecial = Sync.MethodMultiTarget(ThingFilterTarget, nameof(ThingFilterContext.AllowSpecial_Helper));
            AllowStuffCategory = Sync.MethodMultiTarget(ThingFilterTarget, nameof(ThingFilterContext.AllowStuffCat_Helper));
            AllowCategory = Sync.MethodMultiTarget(ThingFilterTarget, nameof(ThingFilterContext.AllowCategory_Helper));
            AllowAll = Sync.MethodMultiTarget(ThingFilterTarget, nameof(ThingFilterContext.AllowAll_Helper));
            DisallowAll = Sync.MethodMultiTarget(ThingFilterTarget, nameof(ThingFilterContext.DisallowAll_Helper));
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", new[] { typeof(StuffCategoryDef), typeof(bool) })]
        static bool ThingFilter_SetAllow(StuffCategoryDef cat, bool allow)
        {
            if (SyncMarkers.DrawnThingFilter == null) return true;
            return !AllowStuffCategory.DoSync(SyncMarkers.DrawnThingFilter, cat, allow);
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", new[] { typeof(SpecialThingFilterDef), typeof(bool) })]
        static bool ThingFilter_SetAllow(SpecialThingFilterDef sfDef, bool allow)
        {
            if (SyncMarkers.DrawnThingFilter == null) return true;
            return !AllowSpecial.DoSync(SyncMarkers.DrawnThingFilter, sfDef, allow);
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", new[] { typeof(ThingDef), typeof(bool) })]
        static bool ThingFilter_SetAllow(ThingDef thingDef, bool allow)
        {
            if (SyncMarkers.DrawnThingFilter == null) return true;
            return !AllowThing.DoSync(SyncMarkers.DrawnThingFilter, thingDef, allow);
        }

        [MpPrefix(typeof(ThingFilter), "SetAllow", new[] { typeof(ThingCategoryDef), typeof(bool), typeof(IEnumerable<ThingDef>), typeof(IEnumerable<SpecialThingFilterDef>) })]
        static bool ThingFilter_SetAllow(ThingCategoryDef categoryDef, bool allow)
        {
            if (SyncMarkers.DrawnThingFilter == null) return true;
            return !AllowCategory.DoSync(SyncMarkers.DrawnThingFilter, categoryDef, allow);
        }

        [MpPrefix(typeof(ThingFilter), "SetAllowAll")]
        static bool ThingFilter_SetAllowAll()
        {
            if (SyncMarkers.DrawnThingFilter == null) return true;
            return !AllowAll.DoSync(SyncMarkers.DrawnThingFilter);
        }

        [MpPrefix(typeof(ThingFilter), "SetDisallowAll")]
        static bool ThingFilter_SetDisallowAll()
        {
            if (SyncMarkers.DrawnThingFilter == null) return true;
            return !DisallowAll.DoSync(SyncMarkers.DrawnThingFilter);
        }
    }

    public abstract record ThingFilterContext : ISyncSimple
    {
        public abstract ThingFilter Filter { get; }
        public abstract ThingFilter ParentFilter { get; }
        public virtual IEnumerable<SpecialThingFilterDef> HiddenFilters { get => null; }

        internal void AllowStuffCat_Helper(StuffCategoryDef cat, bool allow)
        {
            Filter.SetAllow(cat, allow);
        }

        internal void AllowSpecial_Helper(SpecialThingFilterDef sfDef, bool allow)
        {
            Filter.SetAllow(sfDef, allow);
        }

        internal void AllowThing_Helper(ThingDef thingDef, bool allow)
        {
            Filter.SetAllow(thingDef, allow);
        }

        internal void DisallowAll_Helper()
        {
            Filter.SetDisallowAll(null, HiddenFilters);
        }

        internal void AllowAll_Helper()
        {
            Filter.SetAllowAll(ParentFilter);
        }

        internal void AllowCategory_Helper(ThingCategoryDef categoryDef, bool allow)
        {
            var node = new TreeNode_ThingCategory(categoryDef);

            Filter.SetAllow(
                categoryDef,
                allow,
                null,
                Listing_TreeThingFilter
                .CalculateHiddenSpecialFilters(node, ParentFilter)
                .ConcatIfNotNull(HiddenFilters)
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
