using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client.Factions;

[HarmonyPatch(typeof(SettlementUtility), nameof(SettlementUtility.AttackNow))]
static class AttackNowPatch
{
    static void Prefix(Caravan caravan)
    {
        FactionContext.Push(caravan.Faction);
    }

    static void Finalizer()
    {
        FactionContext.Pop();
    }
}

[HarmonyPatch(typeof(GetOrGenerateMapUtility), nameof(GetOrGenerateMapUtility.GetOrGenerateMap), [typeof(PlanetTile), typeof(IntVec3), typeof(WorldObjectDef), typeof(IEnumerable<GenStepWithParams>), typeof(bool)])]
static class MapGenFactionPatch
{
    static void Prefix(PlanetTile tile)
    {
        Faction factionToSet = GetFactionAt(tile);

        if (Multiplayer.Client != null && factionToSet == null)
            Log.Warning($"Couldn't set the faction context for map gen at {tile.tileId}: no world object and no stored faction.");

        FactionContext.Push(factionToSet);
    }

    private static Faction GetFactionAt(PlanetTile tile)
    {
        var worldObjectsHolder = Find.WorldObjects;

        var mapParent = worldObjectsHolder.MapParentAt(tile);
        if (mapParent != null)
            return mapParent.Faction;

        var caravan = worldObjectsHolder.PlayerControlledCaravanAt(tile);
        if (caravan != null)
            return caravan.Faction;

        return TileFactionContext.GetFactionForTile(tile);
    }

    static void Finalizer()
    {
        FactionContext.Pop();
    }
}

[HarmonyPatch(typeof(CaravanEnterMapUtility), nameof(CaravanEnterMapUtility.Enter), new[] { typeof(Caravan), typeof(Map), typeof(Func<Pawn, IntVec3>), typeof(CaravanDropInventoryMode), typeof(bool) })]
static class CaravanEnterFactionPatch
{
    static void Prefix(Caravan caravan)
    {
        FactionContext.Push(caravan.Faction);
    }

    static void Finalizer()
    {
        FactionContext.Pop();
    }
}

[HarmonyPatch(typeof(WealthWatcher), nameof(WealthWatcher.ForceRecount))]
static class WealthRecountFactionPatch
{
    static void Prefix(WealthWatcher __instance)
    {
        FactionContext.Push(__instance.map.ParentFaction);
    }

    static void Finalizer()
    {
        FactionContext.Pop();
    }
}

[HarmonyPatch(typeof(FactionIdeosTracker), nameof(FactionIdeosTracker.RecalculateIdeosBasedOnPlayerPawns))]
static class RecalculateFactionIdeosContext
{
    static void Prefix(FactionIdeosTracker __instance)
    {
        FactionContext.Push(__instance.faction);
    }

    static void Finalizer()
    {
        FactionContext.Pop();
    }
}

[HarmonyPatch(typeof(Bill), nameof(Bill.ValidateSettings))]
static class BillValidateSettingsPatch
{
    static void Prefix(Bill __instance)
    {
        if (Multiplayer.Client == null) return;
        FactionContext.Push(__instance.pawnRestriction?.Faction); // todo HostFaction, SlaveFaction?
    }

    static void Finalizer()
    {
        if (Multiplayer.Client == null) return;
        FactionContext.Pop();
    }
}

[HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.ValidateSettings))]
static class BillProductionValidateSettingsPatch
{
    static void Prefix(Bill_Production __instance, ref Map __state)
    {
        if (Multiplayer.Client == null) return;

        if (__instance.Map != null && __instance.billStack?.billGiver is Thing { Faction: { } faction })
        {
            __instance.Map.PushFaction(faction);
            __state = __instance.Map;
        }
    }

    static void Finalizer(Map __state)
    {
        __state?.PopFaction();
    }
}

// Clean up after map generation is complete
[HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
static class CleanupTileFactionContext
{
    static void Finalizer(MapParent parent)
    {
        if (parent != null)
            TileFactionContext.ClearTile(parent.Tile);
    }
}
