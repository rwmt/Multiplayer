using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Util;
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
        private static FieldInfo planField;
        private static FieldInfo colorField;

        static IEnumerable<MethodBase> TargetMethods()
        {
            // The color option's action, <>c__DisplayClass45_0.<GetGizmos>b__1 in 1.6
            var lambda = MpMethodUtil.GetLambda(typeof(Plan), nameof(Plan.GetGizmos), lambdaOrdinal: 1);

            planField = AccessTools.Field(lambda.DeclaringType, "<>4__this");
            colorField = AccessTools.Field(lambda.DeclaringType, "newCol");
            if (planField == null || colorField == null)
                throw new Exception($"Plan color lambda {lambda.DeclaringType}::{lambda.Name} doesn't capture the expected fields");

            yield return lambda;
        }

        static bool Prefix(object __instance)
        {
            if (Multiplayer.Client == null) return true;

            Plan plan = (Plan)planField.GetValue(__instance);
            ColorDef color = (ColorDef)colorField.GetValue(__instance);

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
