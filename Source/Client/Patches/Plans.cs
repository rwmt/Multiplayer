using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch]
    public static class PlanGetGizmosPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            Type planType = AccessTools.TypeByName("Verse.Plan+<>c__DisplayClass45_0");
            yield return AccessTools.Method(planType, "<GetGizmos>b__1");
        }

        static bool Prefix(object __instance)
        {
            if (Multiplayer.Client == null) return true;

            Plan plan = (Plan)AccessTools.Field(__instance.GetType(), "<>4__this").GetValue(__instance);
            ColorDef color = (ColorDef)AccessTools.Field(__instance.GetType(), "newCol").GetValue(__instance);

            SyncSetPlanColor(plan, color);
            return false;
        }

        [SyncMethod]
        public static void SyncSetPlanColor(Plan plan, ColorDef color)
        {
            plan.Color = color;
            AccessTools.Method(typeof(Plan), "CheckContiguous").Invoke(plan, null);
        }
    }

    [HarmonyPatch(typeof(Designator_Plan_Copy), nameof(Designator_Plan_Copy.DesignateSingleCell))]
    public static class PlanCopySingleCellPatch
    {
        static bool Prefix(Designator_Plan_Copy __instance, IntVec3 c)
        {
            if (Multiplayer.Client == null) return true;

            SyncDesignateSingleCell(__instance, __instance.cells, __instance.colorDef);
            return false;
        }

        [SyncMethod]
        public static void SyncDesignateSingleCell(Designator_Plan_Add designator, List<IntVec3> cells, ColorDef colorDef)
        {
            designator.colorDef = colorDef;
            designator.PlanCells(cells);
        }
    }

    [HarmonyPatch(typeof(Designator_Plan_CopySelectionPaste), nameof(Designator_Plan_CopySelectionPaste.DesignateSingleCell))]
    public static class PlanCopySelectionPasteSingleCellPatch
    {
        static bool Prefix(Designator_Plan_CopySelectionPaste __instance, IntVec3 c)
        {
            if (Multiplayer.Client == null) return true;

            foreach (ColorDef color in __instance.colors)
                SyncDesignateSingleCell(__instance, __instance.GetCurrentCells(c, color).ToList(), color);

            return false;
        }

        [SyncMethod]
        public static void SyncDesignateSingleCell(Designator_Plan_CopySelectionPaste designator, List<IntVec3> cells, ColorDef colorDef)
        {
            designator.SelectedPlan = null;
            designator.colorDef = colorDef;
            designator.PlanCells(cells);
        }
    }
}
