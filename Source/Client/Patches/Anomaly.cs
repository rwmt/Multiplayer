using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(AnomalyUtility), nameof(AnomalyUtility.TryGetNearbyUnseenCell))]
    static class AnomalyUtility_TryGetNearbyUnseenCell
    {
        // Discards the result from CurrentViewRect and calls EmptyCellRect
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            var target = AccessTools.PropertyGetter(typeof(CameraDriver), nameof(CameraDriver.CurrentViewRect));
            var replace = AccessTools.Method(typeof(AnomalyUtility_TryGetNearbyUnseenCell), nameof(EmptyCellRect));

            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.operand as MethodInfo == target)
                {
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Call, replace);
                }
            }
        }

        static CellRect EmptyCellRect()
        {
            return Multiplayer.Client != null ? CellRect.Empty : Find.CameraDriver.CurrentViewRect;
        }

        static void Prefix() => Rand.PushState(Find.TickManager.TicksAbs);
        static void Postfix() => Rand.PopState();
    }
}