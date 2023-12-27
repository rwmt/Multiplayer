using System;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Client;

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
        // The map could potentially be null - for example, if we're syncing mortar. The mortar gun itself
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
