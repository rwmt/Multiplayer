using HarmonyLib;
using Multiplayer.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using Verse;

namespace Multiplayer.Client.EarlyPatches
{
    //[HarmonyPatch(typeof(XmlNode), nameof(XmlNode.ChildNodes), MethodType.Getter)]
    static class XmlNodeListPatch
    {
        public static bool optimizeXml;
        public static XmlNode node;
        public static XmlNodeList list;

        public static void Postfix(XmlNode __instance, ref XmlNodeList __result)
        {
            if (!optimizeXml) return;

            if (node != __instance)
            {
                node = __instance;
                list = new StaticXmlNodeList() { nodes = new List<XmlNode>(__result.Cast<XmlNode>()) };
            }

            __result = list;
        }

        public class StaticXmlNodeList : XmlNodeList
        {
            public List<XmlNode> nodes;

            public override int Count => nodes.Count;

            public override IEnumerator GetEnumerator()
            {
                return nodes.GetEnumerator();
            }

            public override XmlNode Item(int index)
            {
                return nodes[index];
            }
        }
    }

    [HarmonyPatch(typeof(XmlInheritance), nameof(XmlInheritance.TryRegisterAllFrom))]
    static class XmlInheritance_Patch
    {
        static void Prefix() => XmlNodeListPatch.optimizeXml = true;
        static void Postfix() => XmlNodeListPatch.optimizeXml = false;
    }

    static class AccessTools_FirstMethod_Patch
    {
        static Dictionary<Type, MethodInfo[]> typeMethods = new Dictionary<Type, MethodInfo[]>();

        public static bool Prefix(Type type, Func<MethodInfo, bool> predicate, ref MethodInfo __result)
        {
            if (type == null || predicate == null) return false;

            if (!typeMethods.TryGetValue(type, out MethodInfo[] methods))
                typeMethods[type] = methods = type.GetMethods(AccessTools.all);

            __result = methods.FirstOrDefault(predicate);

            return false;
        }
    }

}

namespace Multiplayer.Client
{
    static class ThingCategoryDef_DescendantThingDefsPatch
    {
        static Dictionary<ThingCategoryDef, HashSet<ThingDef>> values = new Dictionary<ThingCategoryDef, HashSet<ThingDef>>(DefaultComparer<ThingCategoryDef>.Instance);

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
        static Dictionary<ThingCategoryDef, HashSet<ThingCategoryDef>> values = new Dictionary<ThingCategoryDef, HashSet<ThingCategoryDef>>(DefaultComparer<ThingCategoryDef>.Instance);

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
