using HarmonyLib;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(WindManager), nameof(WindManager.BaseWindSpeedAt))]
    static class BaseWindSpeetGetHashCodeChange
    {
        static readonly MethodInfo OriginalGetHash = AccessTools.Method(typeof(object), nameof(GetHashCode));

        static readonly MethodInfo ChangedGetHash = AccessTools.Method(typeof(BaseWindSpeetGetHashCodeChange), nameof(GetMapHash));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var ci in insts)
            {
                if (ci.Calls(OriginalGetHash))
                    yield return new CodeInstruction(OpCodes.Call, ChangedGetHash);
                else
                    yield return ci;
            }
        }

        static int GetMapHash(Map map)
        {
            if (Multiplayer.Client == null)
                return map.GetHashCode();

            return map.Tile;
        }
    }

    [HarmonyPatch(typeof(GlowGrid), MethodType.Constructor, typeof(Map))]
    static class GlowGridCtorPatch
    {
        private static AccessTools.FieldRef<GlowGrid, HashSet<CompGlower>> litGlowers =
            AccessTools.FieldRefAccess<GlowGrid, HashSet<CompGlower>>(nameof(GlowGrid.litGlowers));

        static void Postfix(GlowGrid __instance)
        {
            litGlowers(__instance) = new HashSet<CompGlower>(new CompGlowerEquality());
        }

        class CompGlowerEquality : IEqualityComparer<CompGlower>
        {
            public bool Equals(CompGlower x, CompGlower y) => x == y;
            public int GetHashCode(CompGlower obj) => obj.parent.thingIDNumber;
        }
    }

    [HarmonyPatch]
    static class PatchTargetInfoHashCodes
    {
        static MethodInfo Combine = AccessTools.Method(typeof(Gen), nameof(Gen.HashCombine)).MakeGenericMethod(typeof(Map));

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GlobalTargetInfo), nameof(GlobalTargetInfo.GetHashCode));
            yield return AccessTools.Method(typeof(TargetInfo), nameof(TargetInfo.GetHashCode));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand as MethodInfo == Combine)
                    inst.operand = AccessTools.Method(typeof(PatchTargetInfoHashCodes), nameof(CombineHashes));

                yield return inst;
            }
        }

        static int CombineHashes(int seed, Map map) => Gen.HashCombineInt(seed, map?.uniqueID ?? -1);
    }

    // todo does this cause issues?
    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetHashCode))]
    static class TradeableHashCode
    {
        static bool Prefix() => false;

        static void Postfix(Tradeable __instance, ref int __result)
        {
            __result = RuntimeHelpers.GetHashCode(__instance);
        }
    }

    // Patch game callers rather than System.HashCode: ValueTuple and Dictionary
    // also use Combine, and changing it when a session starts invalidates their keys.
    [HarmonyPatch]
    static class DeterministicGameHashCalls
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(Map), nameof(Map.NextGenSeed));
            yield return AccessTools.PropertyGetter(typeof(Gravship), nameof(Gravship.ConstantRandSeed));
            yield return AccessTools.Method(typeof(LayoutWorker), "FillAllRooms");
            yield return AccessTools.Method(typeof(Building_Trap), nameof(Building_Trap.KnowsOfTrap));
            yield return AccessTools.Method(typeof(PathFinderCostTuning), nameof(PathFinderCostTuning.GetHashCode));
            yield return AccessTools.Method(typeof(FastTileFinder.TileQueryParams), nameof(FastTileFinder.TileQueryParams.GetHashCode));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            HashCodeTranspiler.ReplaceCalls(instructions);
    }
}
