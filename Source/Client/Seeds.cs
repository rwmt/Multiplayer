using Harmony;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
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

            int seed = __instance.uniqueID;
            Rand.PushState(seed);

            if (Scribe.mode != LoadSaveMode.LoadingVars)
            {
                //UniqueIdsPatch.CurrentBlock = __instance.MpComp().mapIdBlock;
                UniqueIdsPatch.CurrentBlock = Multiplayer.GlobalIdBlock;
            }

            __state = true;
        }

        static void Postfix(bool __state)
        {
            if (__state)
            {
                Rand.PopState();

                if (Scribe.mode != LoadSaveMode.LoadingVars)
                    UniqueIdsPatch.CurrentBlock = null;
            }
        }
    }

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch(nameof(Map.FinalizeLoading))]
    public static class SeedMapFinalizeLoading
    {
        static void Prefix(Map __instance, ref bool __state)
        {
            if (Multiplayer.Client == null) return;

            int seed = __instance.uniqueID;
            Rand.PushState(seed);

            //UniqueIdsPatch.CurrentBlock = __instance.MpComp().mapIdBlock;
            UniqueIdsPatch.CurrentBlock = Multiplayer.GlobalIdBlock;

            __state = true;
        }

        static void Postfix(bool __state)
        {
            if (__state)
            {
                Rand.PopState();
                UniqueIdsPatch.CurrentBlock = null;
            }
        }
    }

    [HarmonyPatch(typeof(CaravanEnterMapUtility), nameof(CaravanEnterMapUtility.Enter), new[] { typeof(Caravan), typeof(Map), typeof(CaravanEnterMode), typeof(CaravanDropInventoryMode), typeof(bool), typeof(Predicate<IntVec3>) })]
    static class SeedCaravanEnter
    {
        static void Prefix(Map map, ref bool __state)
        {
            if (Multiplayer.Client == null) return;

            int seed = map.uniqueID;
            Rand.PushState(seed);

            __state = true;
        }

        static void Postfix(Map map, bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.QueueLongEvent), new[] { typeof(Action), typeof(string), typeof(bool), typeof(Action<Exception>) })]
    static class SeedLongEvents
    {
        static void Prefix(ref Action action)
        {
            if (Multiplayer.Client != null && (Multiplayer.Ticking || Multiplayer.ExecutingCmds))
            {
                action = PushState + action + Rand.PopState;
            }
        }

        static void PushState() => Rand.PushState(4);
    }

    // Seed the rotation random
    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.Spawn), new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool) })]
    static class GenSpawnRotatePatch
    {
        static MethodInfo Rot4GetRandom = AccessTools.Property(typeof(Rot4), nameof(Rot4.Random)).GetGetMethod();

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (CodeInstruction inst in insts)
            {
                if (inst.operand == Rot4GetRandom)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Thing), nameof(Thing.thingIDNumber)));
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Rand), nameof(Rand.PushState), new[] { typeof(int) }));
                }

                yield return inst;

                if (inst.operand == Rot4GetRandom)
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Rand), nameof(Rand.PopState)));
            }
        }
    }

    public static class PatchThingMethods
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        public static void Prefix(Thing __instance, ref Container<Map>? __state)
        {
            if (Multiplayer.Client == null) return;

            __state = __instance.Map;
            ThingContext.Push(__instance);

            if (__instance.def.CanHaveFaction)
                __instance.Map.PushFaction(__instance.Faction);
        }

        [HarmonyPriority(MpPriority.MpLast)]
        public static void Postfix(Thing __instance, Container<Map>? __state)
        {
            if (__state == null) return;

            if (__instance.def.CanHaveFaction)
                __state.PopFaction();

            ThingContext.Pop();
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
        public static void Postfix(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

    [MpPatch(typeof(GrammarResolver), nameof(GrammarResolver.Resolve))]
    [MpPatch(typeof(PawnBioAndNameGenerator), nameof(PawnBioAndNameGenerator.GeneratePawnName))]
    [MpPatch(typeof(NameGenerator), nameof(NameGenerator.GenerateName), new[] { typeof(RulePackDef), typeof(Predicate<string>), typeof(bool), typeof(string), typeof(string) })]
    static class SeedGrammar
    {
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

    [MpPatch(typeof(PawnGraphicSet), nameof(PawnGraphicSet.ResolveAllGraphics))]
    [MpPatch(typeof(PawnGraphicSet), nameof(PawnGraphicSet.ResolveApparelGraphics))]
    static class SeedPawnGraphics
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(PawnGraphicSet __instance, ref bool __state)
        {
            Rand.PushState(__instance.pawn.thingIDNumber);
            __state = true;
        }

        [HarmonyPriority(MpPriority.MpLast)]
        static void Postfix(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }

}
