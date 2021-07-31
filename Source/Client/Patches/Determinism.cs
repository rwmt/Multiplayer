using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(PawnTweener))]
    [HarmonyPatch(nameof(PawnTweener.TweenedPos), MethodType.Getter)]
    static class DrawPosPatch
    {
        static bool Prefix() => Multiplayer.Client == null || Multiplayer.InInterface;

        // Give the root position during ticking
        static void Postfix(PawnTweener __instance, ref Vector3 __result)
        {
            if (Multiplayer.Client == null || Multiplayer.InInterface) return;
            __result = __instance.TweenedPosRoot();
        }
    }

    [HarmonyPatch]
    static class FixApparelSort
    {
        static MethodBase TargetMethod() =>
            typeof(Pawn_ApparelTracker).
            GetNestedTypes(BindingFlags.NonPublic).
            Select(t => Inner(t)).AllNotNull().FirstOrDefault();

        private static MethodBase Inner(Type t)
        {
            if (!t.IsCompilerGenerated())
                return null;
            return AccessTools.FirstMethod(t, m => m.Name.Contains(nameof(Pawn_ApparelTracker.SortWornApparelIntoDrawOrder)));
        }

        static void Postfix(Apparel a, Apparel b, ref int __result)
        {
            if (__result == 0)
                __result = a.thingIDNumber.CompareTo(b.thingIDNumber);
        }
    }

    [HarmonyPatch(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.TryGetMeleeVerb))]
    static class TryGetMeleeVerbPatch
    {
        static bool Cancel => Multiplayer.Client != null && Multiplayer.InInterface;

        static bool Prefix()
        {
            // Namely FloatMenuUtility.GetMeleeAttackAction
            return !Cancel;
        }

        static void Postfix(Pawn_MeleeVerbs __instance, Thing target, ref Verb __result)
        {
            if (Cancel)
                __result = __instance.GetUpdatedAvailableVerbsList(false).FirstOrDefault(ve => ve.GetSelectionWeight(target) != 0).verb;
        }
    }

    [HarmonyPatch(typeof(PawnBioAndNameGenerator), nameof(PawnBioAndNameGenerator.TryGetRandomUnusedSolidName))]
    static class GenerateNewPawnInternalPatch
    {
        static MethodBase FirstOrDefault = SymbolExtensions.GetMethodInfo<IEnumerable<NameTriple>>(
            e => e.FirstOrDefault()
        );

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e)
        {
            List<CodeInstruction> insts = new List<CodeInstruction>(e);

            for (int i = insts.Count - 1; i >= 0; i--)
            {
                if (insts[i].operand == FirstOrDefault)
                    insts.Insert(
                       i + 1,
                       new CodeInstruction(OpCodes.Ldloc_1),
                       new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GenerateNewPawnInternalPatch), nameof(Unshuffle)).MakeGenericMethod(typeof(NameTriple)))
                   );
            }

            return insts;
        }

        public static void Unshuffle<T>(List<T> list)
        {
            uint iters = Rand.iterations;

            int i = 0;
            while (i < list.Count)
            {
                int index = Mathf.Abs(MurmurHash.GetInt(Rand.seed, iters--) % (i + 1));
                T value = list[index];
                list[index] = list[i];
                list[i] = value;
                i++;
            }
        }
    }

    [HarmonyPatch(typeof(WorldObjectSelectionUtility), nameof(WorldObjectSelectionUtility.VisibleToCameraNow))]
    static class CaravanVisibleToCameraPatch
    {
        static void Postfix(ref bool __result)
        {
            if (!Multiplayer.InInterface)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(WealthWatcher), nameof(WealthWatcher.ForceRecount))]
    static class WealthWatcherRecalc
    {
        static bool Prefix() => Multiplayer.Client == null || !Multiplayer.ShouldSync;
    }

    [HarmonyPatch(typeof(FloodFillerFog), nameof(FloodFillerFog.FloodUnfog))]
    static class FloodUnfogPatch
    {
        static void Postfix(ref FloodUnfogResult __result)
        {
            if (Multiplayer.Client != null)
                __result.allOnScreen = false;
        }
    }

    [HarmonyPatch(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.DrawTrackerTick))]
    static class DrawTrackerTickPatch
    {
        static MethodInfo CellRectContains = AccessTools.Method(typeof(CellRect), nameof(CellRect.Contains));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                yield return inst;

                if (inst.operand == CellRectContains)
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    yield return new CodeInstruction(OpCodes.Or);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Zone), nameof(Zone.Cells), MethodType.Getter)]
    static class ZoneCellsShufflePatch
    {
        static FieldInfo CellsShuffled = AccessTools.Field(typeof(Zone), nameof(Zone.cellsShuffled));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            bool found = false;

            foreach (var inst in insts)
            {
                yield return inst;

                if (!found && inst.operand == CellsShuffled)
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ZoneCellsShufflePatch), nameof(ShouldShuffle)));
                    yield return new CodeInstruction(OpCodes.Not);
                    yield return new CodeInstruction(OpCodes.Or);
                    found = true;
                }
            }
        }

        static bool ShouldShuffle()
        {
            return Multiplayer.Client == null || Multiplayer.Ticking;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.StartOrResumeBillJob))]
    static class StartOrResumeBillPatch
    {
        static FieldInfo LastFailTicks = AccessTools.Field(typeof(Bill), nameof(Bill.lastIngredientSearchFailTicks));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts, MethodBase original)
        {
            var list = new List<CodeInstruction>(insts);

            int index = new CodeFinder(original, list).Forward(OpCodes.Stfld, LastFailTicks).Advance(-1);
            if (list[index].opcode != OpCodes.Ldc_I4_0)
                throw new Exception("Wrong code");

            list.RemoveAt(index);

            list.Insert(
                index,
                new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StartOrResumeBillPatch), nameof(Value)))
            );

            return list;
        }

        static int Value(Bill bill, Pawn pawn)
        {
            return FloatMenuMakerMap.makingFor == pawn ? bill.lastIngredientSearchFailTicks : 0;
        }
    }

    [HarmonyPatch]
    static class SortArchivablesById
    {
        static MethodBase TargetMethod()
        {
            // Get all non public types, including compiler types
            List<Type> nestedPrivateTypes = new List<Type>(typeof(Archive).GetNestedTypes(BindingFlags.NonPublic));

            // There are two of these in 1.1, <GetGizmos> and <>c. Pluck the one we want for sort inner
            Type cType = nestedPrivateTypes.Find(t => t.Name.Equals("<>c"));

            return AccessTools.Method(cType, "<Add>b__6_0");
        }

        static void Postfix(IArchivable x, ref int __result)
        {
            if (x is ArchivedDialog dialog)
                __result = dialog.ID;
            else if (x is Letter letter)
                __result = letter.ID;
            else if (x is Message msg)
                __result = msg.ID;
        }
    }

    [HarmonyPatch(typeof(DangerWatcher), nameof(DangerWatcher.DangerRating), MethodType.Getter)]
    static class DangerRatingPatch
    {
        static bool Prefix() => !Multiplayer.InInterface;

        static void Postfix(DangerWatcher __instance, ref StoryDanger __result)
        {
            if (Multiplayer.InInterface)
                __result = __instance.dangerRatingInt;
        }
    }

    [HarmonyPatch(typeof(Caravan), nameof(Caravan.ImmobilizedByMass), MethodType.Getter)]
    static class ImmobilizedByMass_Patch
    {
        static bool Prefix() => !Multiplayer.InInterface;
    }

    [HarmonyPatch(typeof(StoryWatcher_PopAdaptation), nameof(StoryWatcher_PopAdaptation.Notify_PawnEvent))]
    static class CancelStoryWatcherEventInInterface
    {
        static bool Prefix() => !Multiplayer.InInterface;
    }

    [HarmonyPatch(typeof(AutoSlaughterManager), nameof(AutoSlaughterManager.Notify_ConfigChanged))]
    static class CancelAutoslaughterDirtying
    {
        static bool Prefix() => !Multiplayer.InInterface;
    }

    [HarmonyPatch(typeof(Pawn_AbilityTracker), nameof(Pawn_AbilityTracker.AllAbilitiesForReading), MethodType.Getter)]
    static class DontRecacheAbilitiesInInterface
    {
        static bool Prefix() => !Multiplayer.InInterface;

        static void Postfix(Pawn_AbilityTracker __instance, ref List<Ability> __result)
        {
            // The result can be null only if the method gets cancelled by the prefix
            if (__result == null)
                __result = __instance.allAbilitiesCached;
        }
    }

}
