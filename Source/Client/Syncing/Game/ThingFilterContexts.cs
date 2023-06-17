using System.Collections.Generic;
using Multiplayer.Client.Experimental;
using Multiplayer.Common;
using RimWorld;
using Verse;

namespace Multiplayer.Client;

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
