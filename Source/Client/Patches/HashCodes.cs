using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(GlowGrid), MethodType.Constructor, new[] { typeof(Map) })]
    static class GlowGridCtorPatch
    {
        static void Postfix(GlowGrid __instance)
        {
            __instance.litGlowers = new HashSet<CompGlower>(new CompGlowerEquality());
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
                if (inst.operand == Combine)
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
}
