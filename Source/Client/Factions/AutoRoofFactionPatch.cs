using HarmonyLib;
using Multiplayer.Client.Factions;
using RimWorld;
using Verse;

namespace Multiplayer.Client;

[HarmonyPatch(typeof(AutoBuildRoofAreaSetter))]
[HarmonyPatch(nameof(AutoBuildRoofAreaSetter.TryGenerateAreaNow))]
public static class AutoRoofFactionPatch
{
    static bool Prefix(AutoBuildRoofAreaSetter __instance, Room room, ref Map __state)
    {
        if (Multiplayer.Client == null) return true;
        if (room.Dereferenced || room.TouchesMapEdge || room.RegionCount > 26 || room.CellCount > 320 || room.IsDoorway) return false;

        Map map = room.Map;
        Faction faction = null;

        foreach (IntVec3 cell in room.BorderCells)
        {
            Thing holder = cell.GetRoofHolderOrImpassable(map);
            if (holder == null || holder.Faction == null) continue;
            if (faction != null && holder.Faction != faction) return false;
            faction = holder.Faction;
        }

        if (faction == null) return false;

        map.PushFaction(faction);
        __state = map;

        return true;
    }

    static void Finalizer(ref Map __state)
    {
        __state?.PopFaction();
    }
}
