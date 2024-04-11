using System.Collections.Generic;
using Multiplayer.API;
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

public record OutfitWrapper(ApparelPolicy Policy) : ThingFilterContext
{
    public override ThingFilter Filter => Policy.filter;
    public override ThingFilter ParentFilter => Dialog_ManageApparelPolicies.apparelGlobalFilter;
    public override IEnumerable<SpecialThingFilterDef> HiddenFilters => SpecialThingFilterDefOf.AllowNonDeadmansApparel.ToEnumerable();
}

public record FoodRestrictionWrapper(FoodPolicy Policy) : ThingFilterContext
{
    public override ThingFilter Filter => Policy.filter;
    public override ThingFilter ParentFilter => Dialog_ManageFoodPolicies.foodGlobalFilter;
    public override IEnumerable<SpecialThingFilterDef> HiddenFilters => SpecialThingFilterDefOf.AllowFresh.ToEnumerable();
}

public record ReadingPolicyDefFilterWrapper(ReadingPolicy Policy) : ThingFilterContext
{
    public override ThingFilter Filter => Policy.defFilter;
    public override ThingFilter ParentFilter => Dialog_ManageReadingPolicies.PolicyGlobalFilter;
}

public record ReadingPolicyEffectFilterWrapper(ReadingPolicy Policy) : ThingFilterContext
{
    public override ThingFilter Filter => Policy.effectFilter;
    public override ThingFilter ParentFilter => null;
}

public record PenAnimalsWrapper(CompAnimalPenMarker PenMarker) : ThingFilterContext
{
    public override ThingFilter Filter => PenMarker.AnimalFilter;
    public override ThingFilter ParentFilter => AnimalPenUtility.GetFixedAnimalFilter();
}

public record PenAutocutWrapper(CompAnimalPenMarker PenMarker) : ThingFilterContext
{
    public override ThingFilter Filter => PenMarker.AutoCutFilter;
    public override ThingFilter ParentFilter => PenMarker.parent.Map.animalPenManager.GetFixedAutoCutFilter();
    public override IEnumerable<SpecialThingFilterDef> HiddenFilters => SpecialThingFilterDefOf.AllowFresh.ToEnumerable();
}

public record DefaultAutocutWrapper(CompAutoCut AutoCut) : ThingFilterContext
{
    public override ThingFilter Filter => AutoCut.AutoCutFilter;
    public override ThingFilter ParentFilter => AutoCut.GetFixedAutoCutFilter();
}
