using Harmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    // Fixes a lag spike when opening debug tools
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch(nameof(UIRoot.UIRootOnGUI))]
    static class UIRootPatch
    {
        static bool done;

        static void Prefix()
        {
            if (done) return;
            GUI.skin.font = Text.fontStyles[1].font;
            done = true;
        }
    }

    // Fix window focus handling
    [HarmonyPatch(typeof(WindowStack))]
    [HarmonyPatch(nameof(WindowStack.CloseWindowsBecauseClicked))]
    public static class WindowFocusPatch
    {
        static void Prefix(Window clickedWindow)
        {
            for (int i = Find.WindowStack.Windows.Count - 1; i >= 0; i--)
            {
                Window window = Find.WindowStack.Windows[i];
                if (window == clickedWindow || window.closeOnClickedOutside) break;
                UI.UnfocusCurrentControl();
            }
        }
    }

    // Optimize trading
    [HarmonyPatch(typeof(ThingCategoryDef))]
    [HarmonyPatch(nameof(ThingCategoryDef.DescendantThingDefs), PropertyMethod.Getter)]
    static class ThingCategoryDef_DescendantThingDefsPatch
    {
        static Dictionary<ThingCategoryDef, HashSet<ThingDef>> values = new Dictionary<ThingCategoryDef, HashSet<ThingDef>>();

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

            set = new HashSet<ThingDef>(__result);
            values[__instance] = set;
            __result = set;
        }
    }

    // Optimize loading
    [HarmonyPatch(typeof(GenTypes), nameof(GenTypes.GetTypeInAnyAssembly))]
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

    // Optimize loading
    [HarmonyPatch(typeof(GenTypes), nameof(GenTypes.AllLeafSubclasses))]
    static class AllLeafSubclassesPatch
    {
        public static HashSet<Type> hasSubclasses;

        static bool Prefix()
        {
            if (hasSubclasses == null)
            {
                hasSubclasses = new HashSet<Type>();
                foreach (Type t in GenTypes.AllTypes)
                    if (t.BaseType != null)
                        hasSubclasses.Add(t.BaseType);
            }

            return false;
        }

        static void Postfix(Type baseType, ref IEnumerable<Type> __result)
        {
            __result = baseType.AllSubclasses().Where(t => !hasSubclasses.Contains(t));
        }
    }

    [HarmonyPatch(typeof(ModAssemblyHandler), nameof(ModAssemblyHandler.ReloadAll))]
    static class ReloadAllAssembliesPatch
    {
        static void Postfix()
        {
            GetTypeInAnyAssemblyPatch.results.Clear();
            AllLeafSubclassesPatch.hasSubclasses = null;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.ImmediateWindow))]
    static class LongEventWindowAbsorbInput
    {
        public const int LongEventWindowId = 62893994;

        static void Prefix(int ID, ref bool absorbInputAroundWindow)
        {
            if (ID == LongEventWindowId)
                absorbInputAroundWindow = true;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.AddNewImmediateWindow))]
    static class LongEventWindowPreventCameraMotion
    {
        static void Postfix(int ID)
        {
            if (ID == -LongEventWindowAbsorbInput.LongEventWindowId)
                Find.WindowStack.windows.Find(w => w.ID == ID).preventCameraMotion = true;
        }
    }

    [HarmonyPatch(typeof(Window), nameof(Window.WindowOnGUI))]
    static class WindowDrawDarkBackground
    {
        static void Prefix(Window __instance)
        {
            if (__instance.ID == -LongEventWindowAbsorbInput.LongEventWindowId ||
                __instance is DisconnectedWindow ||
                __instance is MpFormingCaravanWindow
            )
                Widgets.DrawBoxSolid(new Rect(0, 0, UI.screenWidth, UI.screenHeight), new Color(0, 0, 0, 0.5f));
        }
    }

}
