using RimWorld;

namespace Multiplayer.Client;

public static class SyncMarkers
{
    public static bool manualPriorities;
    public static bool researchToil;

    public static DrugPolicy drugPolicy;
    public static Outfit dialogOutfit;
    public static FoodRestriction foodRestriction;

    [MpPrefix(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoManualPrioritiesCheckbox))]
    static void ManualPriorities_Prefix() => manualPriorities = true;

    [MpPostfix(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoManualPrioritiesCheckbox))]
    static void ManualPriorities_Postfix() => manualPriorities = false;

    [MpPrefix(typeof(JobDriver_Research), nameof(JobDriver_Research.MakeNewToils), lambdaOrdinal: 0)]
    static void ResearchToil_Prefix() => researchToil = true;

    [MpPostfix(typeof(JobDriver_Research), nameof(JobDriver_Research.MakeNewToils), lambdaOrdinal: 0)]
    static void ResearchToil_Postfix() => researchToil = false;

    [MpPrefix(typeof(Dialog_ManageOutfits), nameof(Dialog_ManageOutfits.DoWindowContents))]
    static void ManageOutfit_Prefix(Dialog_ManageOutfits __instance) => dialogOutfit = __instance.SelectedOutfit;

    [MpPostfix(typeof(Dialog_ManageOutfits), nameof(Dialog_ManageOutfits.DoWindowContents))]
    static void ManageOutfit_Postfix() => dialogOutfit = null;

    [MpPrefix(typeof(Dialog_ManageFoodRestrictions), nameof(Dialog_ManageFoodRestrictions.DoWindowContents))]
    static void ManageFoodRestriction_Prefix(Dialog_ManageFoodRestrictions __instance) =>
        foodRestriction = __instance.SelectedFoodRestriction;

    [MpPostfix(typeof(Dialog_ManageFoodRestrictions), nameof(Dialog_ManageFoodRestrictions.DoWindowContents))]
    static void ManageFoodRestriction_Postfix() => foodRestriction = null;

    [MpPrefix(typeof(Dialog_ManageDrugPolicies), nameof(Dialog_ManageDrugPolicies.DoWindowContents))]
    static void ManageDrugPolicy_Prefix(Dialog_ManageDrugPolicies __instance) => drugPolicy = __instance.SelectedPolicy;

    [MpPostfix(typeof(Dialog_ManageDrugPolicies), nameof(Dialog_ManageDrugPolicies.DoWindowContents))]
    static void ManageDrugPolicy_Postfix(Dialog_ManageDrugPolicies __instance) => drugPolicy = null;
}
