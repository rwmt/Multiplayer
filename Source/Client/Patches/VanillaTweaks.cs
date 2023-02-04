using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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
            Text.fontStyles[1].font.fontNames = new string[] { "arial", "arialbd", "ariali", "arialbi" };
            done = true;
        }
    }

    // Fix window focus handling
    [HarmonyPatch(typeof(WindowStack))]
    [HarmonyPatch(nameof(WindowStack.CloseWindowsBecauseClicked))]
    public static class WindowFocusPatch
    {
        static void Prefix(WindowStack __instance, Window clickedWindow)
        {
            for (int i = Find.WindowStack.Windows.Count - 1; i >= 0; i--)
            {
                Window window = Find.WindowStack.Windows[i];
                __instance.focusedWindow = window;

                if (window == clickedWindow || window.closeOnClickedOutside) return;
                UI.UnfocusCurrentControl();
            }

            __instance.focusedWindow = null;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_PawnsArrive), nameof(IncidentWorker_PawnsArrive.FactionCanBeGroupSource))]
    static class FactionCanBeGroupSourcePatch
    {
        static void Postfix(Faction f, ref bool __result)
        {
            __result &= f.def.pawnGroupMakers?.Count > 0;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.AddNewImmediateWindow))]
    static class LongEventWindowPreventCameraMotion
    {
        public const int LongEventWindowId = 62893994;

        static void Postfix(int ID)
        {
            if (ID == -LongEventWindowId || ID == -IngameUIPatch.ModalWindowId)
            {
                var window = Find.WindowStack.windows.Find(w => w.ID == ID);

                window.absorbInputAroundWindow = true;
                window.preventCameraMotion = true;
            }
        }
    }

    [HarmonyPatch(typeof(Window), nameof(Window.WindowOnGUI))]
    static class WindowDrawDarkBackground
    {
        static void Prefix(Window __instance)
        {
            if (Current.ProgramState == ProgramState.Entry) return;

            if (__instance.ID == -LongEventWindowPreventCameraMotion.LongEventWindowId ||
                __instance.ID == -IngameUIPatch.ModalWindowId ||
                __instance is DisconnectedWindow ||
                __instance is CaravanFormingProxy
            )
                Widgets.DrawBoxSolid(new Rect(0, 0, UI.screenWidth, UI.screenHeight), new Color(0, 0, 0, 0.5f));
        }
    }

    // Fixes a bug with long event handler's immediate window draw order
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.ImmediateWindow))]
    static class AddImmediateWindowsDuringLayouting
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            bool found = false;

            foreach (var inst in insts)
            {
                if (!found && inst.opcode == OpCodes.Ldc_I4_7)
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AddImmediateWindowsDuringLayouting), nameof(Process)));
                    found = true;
                }

                yield return inst;
            }
        }

        static EventType Process(EventType type) => type == EventType.Layout ? EventType.Repaint : type;
    }

    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawLine))]
    static class DrawLineOnlyOnRepaint
    {
        static bool Prefix() => Event.current.type == EventType.Repaint;
    }

    [HarmonyPatch]
    public static class WidgetsResolveParsePatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), nameof(Widgets.ResolveParseNow)).MakeGenericMethod(typeof(int));
        }

        // Fix input field handling
        static void Prefix(bool force, ref int val, ref string buffer, ref string edited)
        {
            if (force)
                edited = Widgets.ToStringTypedIn(val);
        }
    }

}
