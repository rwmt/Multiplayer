using HarmonyLib;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;

namespace Multiplayer.Client.Patches
{
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

    // Have created a patch that will handle 2-8 System.HashCode.Combine functions.
    // TODO: Check the following:
    // TileQueryParams.GetHashCode
    // UnmanagedGridTraverseParams.GetHashCode
    // MapGridRequest.GetHashCode
    // SimplifiedPastureNutritionSimulator.GetHashCode
    // FieldAliasCache.GetHashCode
    static class HashCodeDetours
    {
        private static readonly Type HashCodeType = typeof(string).Assembly.GetType("System.HashCode");

        internal static MethodBase GetCombineIntMethod(int numOfInts)
        {
            Type[] intTypes = Enumerable.Repeat(typeof(int), numOfInts).ToArray();

            // Look for a pre-compiled int,int,… overload (only exists on some runtimes)
            MethodInfo method = HashCodeType.GetMethod("Combine", BindingFlags.Public | BindingFlags.Static, binder: null, types: intTypes, modifiers: null);
            if (method != null)
                return method;   // good – no generics involved

            // Fall back to the generic definition and close it for <int,…,int>
            MethodInfo genericMethod = HashCodeType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                 .First(x =>
                                    x.Name == "Combine" &&
                                    x.IsGenericMethodDefinition &&
                                    x.GetGenericArguments().Length == numOfInts);

            return genericMethod.MakeGenericMethod(intTypes);
        }
    }

    //  ─── PATCH 2 ints ─────────────────────────────────────────
    [HarmonyPatch]
    static class Combine2Patch
    {
        static MethodBase TargetMethod() => HashCodeDetours.GetCombineIntMethod(2);

        static bool Prefix(int value1, int value2, ref int __result)
        {
            if (Multiplayer.Client == null)
                return true;

            __result = Gen.HashCombineInt(value1, value2);
            return false;
        }
    }

    //  ─── PATCH 3 ints ─────────────────────────────────────────
    [HarmonyPatch]
    static class Combine3Patch
    {
        static MethodBase TargetMethod() => HashCodeDetours.GetCombineIntMethod(3);

        static bool Prefix(int value1, int value2, int value3, ref int __result)
        {
            if (Multiplayer.Client == null)
                return true;

            __result = DeterministicHash.HashCombineInt(value1, value2, value3);
            return false;
        }
    }

    //  ─── PATCH 4 ints ─────────────────────────────────────────
    [HarmonyPatch]
    static class Combine4Patch
    {
        static MethodBase TargetMethod() => HashCodeDetours.GetCombineIntMethod(4);

        static bool Prefix(int value1, int value2, int value3, int value4, ref int __result)
        {
            if (Multiplayer.Client == null)
                return true;

            __result = Gen.HashCombineInt(value1, value2, value3, value4);
            return false;
        }
    }

    //  ─── PATCH 5 ints ─────────────────────────────────────────
    [HarmonyPatch]
    static class Combine5Patch
    {
        static MethodBase TargetMethod() => HashCodeDetours.GetCombineIntMethod(5);

        static bool Prefix(int value1, int value2, int value3, int value4, int value5, ref int __result)
        {
            if (Multiplayer.Client == null)
                return true;

            __result = DeterministicHash.HashCombineInt(value1, value2, value3, value4, value5);
            return false;
        }
    }

    //  ─── PATCH 6 ints ─────────────────────────────────────────
    [HarmonyPatch]
    static class Combine6Patch
    {
        static MethodBase TargetMethod() => HashCodeDetours.GetCombineIntMethod(6);

        static bool Prefix(int value1, int value2, int value3, int value4, int value5, int value6, ref int __result)
        {
            if (Multiplayer.Client == null)
                return true;

            __result = DeterministicHash.HashCombineInt(value1, value2, value3,
                                                  value4, value5, value6);
            return false;
        }
    }

    //  ─── PATCH 7 ints ─────────────────────────────────────────
    [HarmonyPatch]
    static class Combine7Patch
    {
        static MethodBase TargetMethod() => HashCodeDetours.GetCombineIntMethod(7);

        static bool Prefix(int value1, int value2, int value3, int value4, int value5, int value6, int value7, ref int __result)
        {
            if (Multiplayer.Client == null)
                return true;

            __result = DeterministicHash.HashCombineInt(value1, value2, value3, value4,
                                                  value5, value6, value7);
            return false;
        }
    }

    //  ─── PATCH 8 ints ─────────────────────────────────────────
    [HarmonyPatch]
    static class Combine8Patch
    {
        static MethodBase TargetMethod() => HashCodeDetours.GetCombineIntMethod(8);

        static bool Prefix(int value1, int value2, int value3, int value4, int value5, int value6, int value7, int value8, ref int __result)
        {
            if (Multiplayer.Client == null)
                return true;

            __result = DeterministicHash.HashCombineInt(value1, value2, value3, value4,
                                                  value5, value6, value7, value8);
            return false;
        }
    }
}
