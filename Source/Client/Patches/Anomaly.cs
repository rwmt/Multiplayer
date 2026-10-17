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

        // Seed per pawn so that multiple calls in one tick don't all pick the same cell
        static void Prefix(Pawn pawn, ref bool __state)
        {
            if (Multiplayer.Client == null) return;
            Rand.PushState(Gen.HashCombineInt(pawn?.thingIDNumber ?? 0, Find.TickManager.TicksAbs));
            __state = true;
        }

        static void Finalizer(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }
}