using Harmony;
using Multiplayer.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Xml;
using Verse;

namespace Multiplayer.Client
{
    static class XmlNodeListPatch
    {
        public static bool optimizeXml;
        public static XmlNode node;
        public static XmlNodeList list;

        public static void XmlNode_ChildNodes_Postfix(XmlNode __instance, ref XmlNodeList __result)
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

    static class GetTypeInAnyAssemblyPatch
    {
        public static Dictionary<string, Type> results = new Dictionary<string, Type>();

        static bool Prefix(string typeName, ref Type __state)
        {
            return !results.TryGetValue(typeName, out __state);
        }

        static void Postfix(string typeName, ref Type __result, Type __state)
        {
            if (__state == null)
                results[typeName] = __result;
            else
                __result = __state;
        }
    }

    static class ParseAndProcessXml_Patch
    {
        static MethodInfo XmlCount = AccessTools.Method(typeof(XmlNodeList), "get_Count");
        static MethodInfo XmlItem = AccessTools.Method(typeof(XmlNodeList), "get_ItemOf");

        static IEnumerable<CodeInstruction> Transpiler(ILGenerator gen, IEnumerable<CodeInstruction> insts)
        {
            var local = gen.DeclareLocal(typeof(List<XmlNode>));

            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ParseAndProcessXml_Patch), nameof(XmlNodes)));
            yield return new CodeInstruction(OpCodes.Stloc_S, local);

            foreach (var inst in insts)
            {
                if (inst.operand == XmlCount)
                {
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Ldloc_S, local);
                    yield return new CodeInstruction(OpCodes.Call, typeof(List<XmlNode>).GetMethod("get_Count"));
                }
                else if (inst.operand == XmlItem)
                {
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Ldloc_S, local);
                    yield return new CodeInstruction(OpCodes.Ldloc_1);
                    yield return new CodeInstruction(OpCodes.Call, typeof(List<XmlNode>).GetMethod("get_Item"));
                }
                else
                {
                    yield return inst;
                }
            }
        }

        static List<XmlNode> XmlNodes(XmlDocument xmlDoc) => new List<XmlNode>(xmlDoc.DocumentElement.ChildNodes.Cast<XmlNode>());
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

    static class XmlInheritance_Patch
    {
        static void Prefix() => XmlNodeListPatch.optimizeXml = true;
        static void Postfix() => XmlNodeListPatch.optimizeXml = false;
    }

}
