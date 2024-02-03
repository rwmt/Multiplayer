using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public interface ISwitchToMap { }

    [HarmonyPatch(typeof(Window), nameof(Window.InnerWindowOnGUI))]
    static class SwitchToMapPatch
    {
        private static FieldInfo doCloseXField = AccessTools.Field(typeof(Window), nameof(Window.doCloseX));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.opcode == OpCodes.Ldfld && inst.operand == doCloseXField)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0); // This window
                    yield return new CodeInstruction(OpCodes.Ldloc_0); // Window rect
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SwitchToMapPatch), nameof(DoSwitchToMap)));
                }

                yield return inst;
            }
        }

        static void DoSwitchToMap(Window window, Rect rect)
        {
            if (window is not ISwitchToMap)
                return;

            using (MpStyle.Set(GameFont.Tiny))
            {
                var switchToMapText = "MpSwitchToMap".Translate();
                var width = switchToMapText.GetWidthCached() + 25;

                if (Widgets.ButtonText(new Rect(rect.xMax - width - 5, 5, width, 24), switchToMapText))
                    window.Close();
            }
        }
    }
}
