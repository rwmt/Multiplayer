using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Multiplayer.Client.Util;
using Verse;
using Verse.Grammar;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Game))]
    [HarmonyPatch(nameof(Game.LoadGame))]
    public static class SeedGameLoad
    {
        static void Prefix()
        {
            Rand.PushState();

            if (Multiplayer.Client == null) return;
            Rand.Seed = 1;
        }

        static void Postfix()
        {
            Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch(nameof(Map.ExposeData))]
    public static class SeedMapLoad
    {
        static void Prefix(Map __instance, ref bool __state)
        {
            if (Multiplayer.Client == null) return;
            if (Scribe.mode != LoadSaveMode.LoadingVars && Scribe.mode != LoadSaveMode.ResolvingCrossRefs && Scribe.mode != LoadSaveMode.PostLoadInit) return;

            int seed = Gen.HashCombineInt(__instance.uniqueID, Find.World.ConstantRandSeed);
            Rand.PushState(seed);

            __state = true;
        }

        static void Postfix(Map __instance, bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch(nameof(Map.FinalizeLoading))]
    public static class SeedMapFinalizeLoading
    {
        static void Prefix(Map __instance, ref bool __state)
        {
            if (Multiplayer.Client == null) return;

            int seed = Gen.HashCombineInt(__instance.uniqueID, Find.World.ConstantRandSeed);
            Rand.PushState(seed);

            __state = true;
        }

        static void Postfix(Map __instance, bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(CaravanEnterMapUtility), nameof(CaravanEnterMapUtility.Enter), typeof(Caravan), typeof(Map), typeof(Func<Pawn, IntVec3>), typeof(CaravanDropInventoryMode), typeof(bool))]
    static class SeedCaravanEnter
    {
        static void Prefix(Map map, ref bool __state)
        {
            if (Multiplayer.Client == null) return;

            int seed = Gen.HashCombineInt(map.uniqueID, Find.World.ConstantRandSeed);
            Rand.PushState(seed);

            __state = true;
        }

        static void Postfix(Map map, bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>), typeof(bool), typeof(bool), typeof(Action))]
    static class SeedLongEvents
    {
        static void Prefix(ref Action action, ref Action callback)
        {
            if (Multiplayer.Client != null && (Multiplayer.Ticking || Multiplayer.ExecutingCmds))
            {
                var seed = Rand.Int;
                action = (() => Rand.PushState(seed)) + action + Rand.PopState;

                if (callback != null)
                {
                    var callbackSeed = Rand.Int;
                    callback = (() => Rand.PushState(callbackSeed)) + callback + Rand.PopState;
                }
            }
        }
    }

    // Seed the rotation random
    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.Spawn), typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool))]
    static class GenSpawnRotatePatch
    {
        static MethodInfo Rot4GetRandom = AccessTools.Property(typeof(Rot4), nameof(Rot4.Random)).GetGetMethod();

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (CodeInstruction inst in insts)
            {
                if (inst.operand as MethodInfo == Rot4GetRandom)
                {
                    // Load newThing.thingIdNumber to the stack
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Thing), nameof(Thing.thingIDNumber)));
                    // Load Find.World.ConstantRandSeed to the stack
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Find), nameof(Find.World)));
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(World), nameof(World.ConstantRandSeed)));
                    // Pop our 2 values, call Gen.HashCombineInt with them, and push the outcome to the stack
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Gen), nameof(Gen.HashCombineInt), [typeof(int), typeof(int)]));
                    // Pop the value off the stack and call Rand.PushState with it as the argument
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Rand), nameof(Rand.PushState), [typeof(int)]));
                }

                yield return inst;

                if (inst.operand as MethodInfo == Rot4GetRandom)
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Rand), nameof(Rand.PopState)));
            }
        }
    }

    public static class RandPatches
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        public static void Prefix(ref bool __state)
        {
            Rand.PushState();
            __state = true;
        }

        [HarmonyPriority(MpPriority.MpLast)]
        public static void Finalizer(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    [HarmonyPatch]
    static class SeedGrammar
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GrammarResolver), nameof(GrammarResolver.Resolve));
            yield return AccessTools.Method(typeof(PawnBioAndNameGenerator), nameof(PawnBioAndNameGenerator.GeneratePawnName));
            yield return AccessTools.Method(typeof(NameGenerator), nameof(NameGenerator.GenerateName), [typeof(RulePackDef), typeof(Predicate<string>), typeof(bool), typeof(string), typeof(string), typeof(List<Rule>)]);
        }

        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(ref bool __state)
        {
            Rand.Element(0, 0); // advance the rng
            Rand.PushState();
            __state = true;
        }

        [HarmonyPriority(MpPriority.MpLast)]
        static void Postfix(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    [HarmonyPatch]
    static class SeedPawnGraphics
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return MpMethodUtil.GetLambda(typeof(PawnRenderer), nameof(PawnRenderer.SetAllGraphicsDirty), 0);
        }

        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(PawnRenderer __instance, ref bool __state)
        {
            Rand.PushState(Gen.HashCombineInt(__instance.pawn.thingIDNumber, Find.World.ConstantRandSeed));
            __state = true;
        }

        [HarmonyPriority(MpPriority.MpLast)]
        static void Postfix(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(PreceptComp_UnwillingToDo_Chance), nameof(PreceptComp_UnwillingToDo_Chance.MemberWillingToDo))]
    static class SeedPreceptComp_UnwillingToDo_Chance
    {
        public static void Prefix(ref bool __state, HistoryEvent ev)
        {
            if (Multiplayer.Client == null) return;

            // PreceptComp_UnwillingToDo_Chance.MemberWillingToDo uses RNG, and can be called in interface (or other places that cause issues).
            // Seed the RNG using the pawn's ID (or the world's constant rand seed as fallback) and the current tick. This will ensure we
            // get a unique result for each pawn on a given tick, but if called multiple times on the same time it'll have a consistent result.
            // The fallback is mostly unnecessary, as this precept comp expects a doer. However, we may as well include it as a precaution.
            // We could add some more parameters to the seed, like using the map's ID or the world's seed, but it's probably an overkill.
            // This could probably be handled in a smarter way, so if anyone has an idea on how and is willing to do it - go ahead and replace this.
            var pawnId = ev.args.TryGetArg<Pawn>(HistoryEventArgsNames.Doer, out var pawn) ? pawn.thingIDNumber : Find.World.ConstantRandSeed;

            Rand.PushState(Gen.HashCombineInt(pawnId, Find.TickManager.TicksGame));
            __state = true;
        }

        public static void Finalizer(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

}
