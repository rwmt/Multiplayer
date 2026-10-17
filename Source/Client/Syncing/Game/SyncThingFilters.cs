using RimWorld;
using System.Collections.Generic;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Client;

public static class SyncThingFilters
{
    [MpPrefix(typeof(ThingFilter), nameof(ThingFilter.SetAllow), [typeof(StuffCategoryDef), typeof(bool)])]
    static bool ThingFilter_SetAllow(ThingFilter __instance, StuffCategoryDef cat, bool allow)
    {
        if (!Multiplayer.ShouldSync) return true;
        if (ThingFilterMarkers.IsUnsyncableFilter(__instance)) return false;
        if (ThingFilterMarkers.DrawnThingFilter == null) return true;
        AllowStuffCat_Helper(ThingFilterMarkers.DrawnThingFilter, cat, allow);
        return false;
    }

    [MpPrefix(typeof(ThingFilter), nameof(ThingFilter.SetAllow), [typeof(SpecialThingFilterDef), typeof(bool)])]
    static bool ThingFilter_SetAllow(ThingFilter __instance, SpecialThingFilterDef sfDef, bool allow)
    {
        if (!Multiplayer.ShouldSync) return true;
        if (ThingFilterMarkers.IsUnsyncableFilter(__instance)) return false;
        if (ThingFilterMarkers.DrawnThingFilter == null) return true;
        AllowSpecial_Helper(ThingFilterMarkers.DrawnThingFilter, sfDef, allow);
        return false;
    }

    [MpPrefix(typeof(ThingFilter), nameof(ThingFilter.SetAllow), [typeof(ThingDef), typeof(bool)])]
    static bool ThingFilter_SetAllow(ThingFilter __instance, ThingDef thingDef, bool allow)
    {
        if (!Multiplayer.ShouldSync) return true;
        if (ThingFilterMarkers.IsUnsyncableFilter(__instance)) return false;
        if (ThingFilterMarkers.DrawnThingFilter == null) return true;
        AllowThing_Helper(ThingFilterMarkers.DrawnThingFilter, thingDef, allow);
        return false;
    }

    [MpPrefix(typeof(ThingFilter), nameof(ThingFilter.SetAllow), [typeof(ThingCategoryDef), typeof(bool), typeof(IEnumerable<ThingDef>), typeof(IEnumerable<SpecialThingFilterDef>)])]
    static bool ThingFilter_SetAllow(ThingFilter __instance, ThingCategoryDef categoryDef, bool allow)
    {
        if (!Multiplayer.ShouldSync) return true;
        if (ThingFilterMarkers.IsUnsyncableFilter(__instance)) return false;
        if (ThingFilterMarkers.DrawnThingFilter == null) return true;
        AllowCategory_Helper(ThingFilterMarkers.DrawnThingFilter, categoryDef, allow);
        return false;
    }

    [MpPrefix(typeof(ThingFilter), nameof(ThingFilter.SetAllowAll))]
    static bool ThingFilter_SetAllowAll(ThingFilter __instance)
    {
        if (!Multiplayer.ShouldSync) return true;
        if (ThingFilterMarkers.IsUnsyncableFilter(__instance)) return false;
        if (ThingFilterMarkers.DrawnThingFilter == null) return true;
        AllowAll_Helper(ThingFilterMarkers.DrawnThingFilter);
        return false;
    }

    [MpPrefix(typeof(ThingFilter), nameof(ThingFilter.SetDisallowAll))]
    static bool ThingFilter_SetDisallowAll(ThingFilter __instance)
    {
        if (!Multiplayer.ShouldSync) return true;
        if (ThingFilterMarkers.IsUnsyncableFilter(__instance)) return false;
        if (ThingFilterMarkers.DrawnThingFilter == null) return true;
        DisallowAll_Helper(ThingFilterMarkers.DrawnThingFilter);
        return false;
    }

    // The range setters invoke settingsChangedCallback, which for storage rechecks the map's haulables. In the interface
    // the setters are called by the watched sliders and the field watch's restore, so only write the backing field
    // there to keep those side effects out of the sender's simulation. The callback runs when the synced field applies.
    static bool RunRangeSetter(ThingFilter filter, out bool writeField)
    {
        writeField = false;

        if (!Multiplayer.InInterface)
            return true;

        if (ThingFilterMarkers.IsUnsyncableFilter(filter))
            return false;

        if (ThingFilterMarkers.DrawnThingFilter?.Filter == filter || filter.settingsChangedCallback?.Target is StorageSettings)
        {
            writeField = true;
            return false;
        }

        return true;
    }

    [MpPrefix(typeof(ThingFilter), "set_" + nameof(ThingFilter.AllowedHitPointsPercents))]
    static bool ThingFilter_SetHitPoints(ThingFilter __instance, FloatRange value)
    {
        if (RunRangeSetter(__instance, out var writeField)) return true;
        if (writeField) __instance.allowedHitPointsPercents = value;
        return false;
    }

    [MpPrefix(typeof(ThingFilter), "set_" + nameof(ThingFilter.AllowedQualityLevels))]
    static bool ThingFilter_SetQuality(ThingFilter __instance, QualityRange value)
    {
        if (RunRangeSetter(__instance, out var writeField)) return true;
        if (writeField) __instance.allowedQualities = value;
        return false;
    }

    [MpPrefix(typeof(ThingFilter), "set_" + nameof(ThingFilter.AllowedMentalBreakChance))]
    static bool ThingFilter_SetMentalBreak(ThingFilter __instance, FloatRange value)
    {
        if (RunRangeSetter(__instance, out var writeField)) return true;
        if (writeField) __instance.allowedMentalBreakChance = value;
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
            context.Filter.OnlySpecialFilters ? null : Listing_TreeThingFilter
                .CalculateHiddenSpecialFilters(node, context.ParentFilter)
                .ConcatIfNotNull(context.HiddenFilters)
        );
    }
}
