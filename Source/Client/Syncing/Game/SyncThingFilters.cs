using RimWorld;
using System.Collections.Generic;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Client;

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
