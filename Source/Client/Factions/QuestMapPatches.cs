using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace Multiplayer.Client.Factions;

// Multifaction: quests pick their map among the player homes. Generated for a player faction (e.g. by its
// storyteller), they could still pick another player's map: a map it got in another way than Map.IsPlayerHome's
// settlement check (a gravship landing site), or a map already given in the slate. Exclude the maps of other player
// factions. Generation in the spectator's context (no owner) keeps vanilla's choice.
public static class QuestMapFilter
{
    public static bool IsOtherPlayersMap(Map map)
    {
        if (map == null || Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return false;

        var faction = Faction.OfPlayer;
        return faction != null && faction != Multiplayer.WorldComp.spectatorFaction &&
               map.ParentFaction is { IsPlayer: true } owner && owner != faction;
    }
}

[HarmonyPatch(typeof(QuestNode_GetMap), nameof(QuestNode_GetMap.IsAcceptableMap))]
static class QuestNodeGetMap_OwnMaps_Patch
{
    static void Postfix(Map map, ref bool __result)
    {
        if (__result && QuestMapFilter.IsOtherPlayersMap(map))
            __result = false;
    }
}

// The map picker of C# quest roots (refugees, beggars, wanderers, DLC arrivals)
[HarmonyPatch]
static class QuestGenGetMap_Validator_OwnMaps_Patch
{
    static MethodBase TargetMethod() =>
        MpMethodUtil.GetLocalFunc(typeof(QuestGen_Get), nameof(QuestGen_Get.GetMap), localFunc: "Validator");

    static void Postfix(Map m, ref bool __result)
    {
        if (__result && QuestMapFilter.IsOtherPlayersMap(m))
            __result = false;
    }
}

[HarmonyPatch(typeof(QuestGen_Get), nameof(QuestGen_Get.GetMap))]
static class QuestGenGetMap_Fallback_OwnMaps_Patch
{
    // The validator excludes the other players' maps, such a result comes from the fallback to the first player home
    static void Postfix(ref Map __result)
    {
        if (QuestMapFilter.IsOtherPlayersMap(__result))
            __result = Find.Maps.FirstOrDefault(m => m.IsPlayerHome && !QuestMapFilter.IsOtherPlayersMap(m));
    }
}

[HarmonyPatch]
static class ReliquaryPilgrimsMap_OwnMaps_Patch
{
    static MethodBase TargetMethod() =>
        MpMethodUtil.GetLocalFunc(typeof(QuestNode_Root_ReliquaryPilgrims), nameof(QuestNode_Root_ReliquaryPilgrims.GetMap), localFunc: "Validator");

    static void Postfix(Map m, ref bool __result)
    {
        if (__result && QuestMapFilter.IsOtherPlayersMap(m))
            __result = false;
    }
}

// RunInt picks the map with Find.Maps.Where(m => m.IsPlayerHome && GetCandidates(m.Tile).Any())
[HarmonyPatch]
static class WorkSiteMap_OwnMaps_Patch
{
    static MethodBase TargetMethod()
    {
        var lambda = MpMethodUtil.GetLambda(typeof(QuestNode_Root_WorkSite), nameof(QuestNode_Root_WorkSite.RunInt), lambdaOrdinal: 0);
        if (lambda.ReturnType != typeof(bool) || lambda.GetParameters() is not [{ ParameterType: var param }] || param != typeof(Map))
            throw new System.Exception($"Unexpected map picker lambda in QuestNode_Root_WorkSite.RunInt: {lambda}");
        return lambda;
    }

    static void Postfix(Map m, ref bool __result)
    {
        if (__result && QuestMapFilter.IsOtherPlayersMap(m))
            __result = false;
    }
}

// RunInt has to find a map when TestRunInt passed
[HarmonyPatch(typeof(QuestNode_Root_WorkSite), nameof(QuestNode_Root_WorkSite.TestRunInt))]
static class WorkSiteTestRun_OwnMaps_Patch
{
    static void Postfix(ref bool __result)
    {
        if (!__result || Multiplayer.Client == null || !Multiplayer.GameComp.multifaction)
            return;

        // Vanilla's map check, the rest of the test passed
        __result = Find.Maps.Any(map =>
            map.IsPlayerHome && !QuestMapFilter.IsOtherPlayersMap(map) &&
            QuestNode_Root_WorkSite.AppearanceFrequency(map) > 0f &&
            QuestNode_Root_WorkSite.GetCandidates(map.Tile).Any());
    }
}
