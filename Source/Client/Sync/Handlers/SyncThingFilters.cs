using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections;
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

    public abstract record ThingFilterContext : SyncWrapper
    {
        public record ContextData(ThingFilter Filter, ThingFilter ParentFilter, IEnumerable<SpecialThingFilterDef> HiddenFilters)
        {
            public ContextData(ThingFilter Filter, ThingFilter ParentFilter, SpecialThingFilterDef HiddenFilter) :
                this(Filter, ParentFilter, HiddenFilter.ToEnumerable())
            {
            }

            public ContextData(ThingFilter Filter, ThingFilter ParentFilter) :
                this(Filter, ParentFilter, (IEnumerable<SpecialThingFilterDef>)null)
            {
            }
        }

        public abstract ContextData Data { get; }

        internal void AllowStuffCat_Helper(StuffCategoryDef cat, bool allow)
        {
            var data = Data;
            data.Filter.SetAllow(cat, allow);
        }

        internal void AllowSpecial_Helper(SpecialThingFilterDef sfDef, bool allow)
        {
            var data = Data;
            data.Filter.SetAllow(sfDef, allow);
        }

        internal void AllowThing_Helper(ThingDef thingDef, bool allow)
        {
            var data = Data;
            data.Filter.SetAllow(thingDef, allow);
        }

        internal void DisallowAll_Helper()
        {
            var data = Data;
            data.Filter.SetDisallowAll(null, data.HiddenFilters);
        }

        internal void AllowAll_Helper()
        {
            var data = Data;
            data.Filter.SetAllowAll(data.ParentFilter);
        }

        internal void AllowCategory_Helper(ThingCategoryDef categoryDef, bool allow)
        {
            var data = Data;
            var node = new TreeNode_ThingCategory(categoryDef);

            data.Filter.SetAllow(
                categoryDef,
                allow,
                null,
                Listing_TreeThingFilter
                .CalculateHiddenSpecialFilters(node, data.ParentFilter)
                .ConcatIfNotNull(data.HiddenFilters)
            );
        }
    }

    public record TabStorageWrapper(IStoreSettingsParent Storage) : ThingFilterContext
    {
        public override ContextData Data =>
            new(Storage.GetStoreSettings().filter, Storage.GetParentStoreSettings()?.filter);
    }

    public record BillConfigWrapper(Bill Bill) : ThingFilterContext
    {
        public override ContextData Data =>
            new(Bill.ingredientFilter, Bill.recipe.fixedIngredientFilter, Bill.recipe.forceHiddenSpecialFilters);
    }

    public record OutfitWrapper(Outfit Outfit) : ThingFilterContext
    {
        public override ContextData Data =>
            new(Outfit.filter, Dialog_ManageOutfits.apparelGlobalFilter, SpecialThingFilterDefOf.AllowNonDeadmansApparel);
    }

    public record FoodRestrictionWrapper(FoodRestriction Food) : ThingFilterContext
    {
        public override ContextData Data =>
            new(Food.filter, Dialog_ManageFoodRestrictions.foodGlobalFilter, SpecialThingFilterDefOf.AllowFresh);
    }

    public record PenAnimalsWrapper(CompAnimalPenMarker Pen) : ThingFilterContext
    {
        public override ContextData Data =>
            new(Pen.AnimalFilter, AnimalPenUtility.GetFixedAnimalFilter());
    }

    public record PenAutocutWrapper(CompAnimalPenMarker Pen) : ThingFilterContext
    {
        public override ContextData Data =>
            new(Pen.AutoCutFilter, Pen.parent.Map.animalPenManager.GetFixedAutoCutFilter(), SpecialThingFilterDefOf.AllowFresh);
    }

}
