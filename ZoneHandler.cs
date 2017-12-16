using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace Multiplayer
{
    [HarmonyPatch(typeof(ZoneManager))]
    [HarmonyPatch(nameof(ZoneManager.RegisterZone))]
    public static class ZoneRegisterPatch
    {
        public static bool dontHandle;

        static bool Prefix(Zone newZone)
        {
            if (Multiplayer.client == null || dontHandle) return true;

            newZone.Map.GetComponent<MultiplayerMapComp>().zonesAdded.Add(newZone);

            return false;
        }
    }

    [HarmonyPatch(typeof(ZoneManager))]
    [HarmonyPatch(nameof(ZoneManager.DeregisterZone))]
    public static class ZoneDeregisterPatch
    {
        static bool Prefix(Zone oldZone)
        {
            if (Multiplayer.client == null || ZoneRegisterPatch.dontHandle) return true;

            oldZone.Map.GetComponent<MultiplayerMapComp>().zonesRemoved.Add(oldZone.label);

            return false;
        }
    }

    [HarmonyPatch(typeof(ZoneManager))]
    [HarmonyPatch(nameof(ZoneManager.ZoneAt))]
    public static class ZoneAtPatch
    {
        static void Postfix(ZoneManager __instance, IntVec3 c, ref Zone __result)
        {
            if (Multiplayer.client == null || !DesignateZoneAddPatch.running) return;

            if (__instance.map.GetComponent<MultiplayerMapComp>().zoneChangesThisTick.TryGetValue(__instance.map.cellIndices.CellToIndex(c), out Zone zone))
                __result = zone;
        }
    }

    [HarmonyPatch(typeof(Zone))]
    [HarmonyPatch(nameof(Zone.Delete))]
    public static class ZoneDeletePatch
    {
        static bool Prefix(Zone __instance)
        {
            if (Multiplayer.client == null) return true;

            if (__instance.cells.Count == 0)
            {
                __instance.Deregister();
            }
            else
            {
                foreach (IntVec3 cell in __instance.cells.ListFullCopy())
                    __instance.RemoveCell(cell);
            }

            Find.Selector.Deselect(__instance);

            return false;
        }
    }

    [HarmonyPatch(typeof(Zone))]
    [HarmonyPatch(nameof(Zone.AddCell))]
    public static class ZoneAddCellPatch
    {
        static bool Prefix(Zone __instance, IntVec3 c)
        {
            if (Multiplayer.client == null || ZoneRegisterPatch.dontHandle) return true;

            Map map = __instance.Map;
            map.GetComponent<MultiplayerMapComp>().zoneChangesThisTick[map.cellIndices.CellToIndex(c)] = __instance;

            return false;
        }
    }

    [HarmonyPatch(typeof(Zone))]
    [HarmonyPatch(nameof(Zone.RemoveCell))]
    public static class ZoneRemoveCellPatch
    {
        static bool Prefix(Zone __instance, IntVec3 c)
        {
            if (Multiplayer.client == null || ZoneRegisterPatch.dontHandle) return true;

            Map map = __instance.Map;
            map.GetComponent<MultiplayerMapComp>().zoneChangesThisTick[map.cellIndices.CellToIndex(c)] = null;

            return false;
        }
    }

    [HarmonyPatch(typeof(Designator_ZoneAdd))]
    [HarmonyPatch(nameof(Designator_ZoneAdd.DesignateMultiCell))]
    public static class DesignateZoneAddPatch
    {
        public static bool running;

        static void Prefix()
        {
            running = true;
        }

        static void Postfix()
        {
            running = false;
        }
    }

    [HarmonyPatch(typeof(SlotGroupManager))]
    [HarmonyPatch(nameof(SlotGroupManager.AddGroup))]
    public static class SlotGroupAddPatch
    {
        static bool Prefix(SlotGroup newGroup)
        {
            if (Multiplayer.client == null || Current.ProgramState != ProgramState.Playing) return true;
            if (newGroup.parent is Zone && !ZoneRegisterPatch.dontHandle) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(SlotGroup))]
    [HarmonyPatch(nameof(SlotGroup.Notify_AddedCell))]
    public static class SlotGroupAddCellPatch
    {
        static bool Prefix(SlotGroup __instance)
        {
            if (Multiplayer.client == null) return true;
            if (__instance.parent is Zone && !ZoneRegisterPatch.dontHandle) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(SlotGroup))]
    [HarmonyPatch(nameof(SlotGroup.Notify_LostCell))]
    public static class SlotGroupLostCellPatch
    {
        static bool Prefix(SlotGroup __instance)
        {
            if (Multiplayer.client == null) return true;
            if (__instance.parent is Zone && !ZoneRegisterPatch.dontHandle) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(SlotGroup))]
    [HarmonyPatch(nameof(SlotGroup.Notify_ParentDestroying))]
    public static class SlotGroupParentDestroyPatch
    {
        static bool Prefix(SlotGroup __instance)
        {
            if (Multiplayer.client == null) return true;
            if (__instance.parent is Zone && !ZoneRegisterPatch.dontHandle) return false;
            return true;
        }
    }
}
