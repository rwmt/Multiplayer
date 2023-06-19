using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client
{
    static class ThingCategoryDef_DescendantThingDefsPatch
    {
        static Dictionary<ThingCategoryDef, HashSet<ThingDef>> values = new(DefaultComparer<ThingCategoryDef>.Instance);

        static bool Prefix(ThingCategoryDef __instance)
        {
            return !values.ContainsKey(__instance);
        }

        static void Postfix(ThingCategoryDef __instance, ref IEnumerable<ThingDef> __result)
        {
            if (values.TryGetValue(__instance, out HashSet<ThingDef> set))
            {
                __result = set;
                return;
            }

            set = new HashSet<ThingDef>(__result, DefaultComparer<ThingDef>.Instance);
            values[__instance] = set;
            __result = set;
        }
    }

    static class ThingCategoryDef_ThisAndChildCategoryDefsPatch
    {
        static Dictionary<ThingCategoryDef, HashSet<ThingCategoryDef>> values = new(DefaultComparer<ThingCategoryDef>.Instance);

        static bool Prefix(ThingCategoryDef __instance)
        {
            return !values.ContainsKey(__instance);
        }

        static void Postfix(ThingCategoryDef __instance, ref IEnumerable<ThingCategoryDef> __result)
        {
            if (values.TryGetValue(__instance, out HashSet<ThingCategoryDef> set))
            {
                __result = set;
                return;
            }

            set = new HashSet<ThingCategoryDef>(__result, DefaultComparer<ThingCategoryDef>.Instance);
            values[__instance] = set;
            __result = set;
        }
    }
}
