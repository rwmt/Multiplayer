using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
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
}
