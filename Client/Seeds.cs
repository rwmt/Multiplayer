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

            MpLog.Log("CaravanEnter " + map.mapPawns.AllPawnsSpawned.Select(p => p.Position).ToStringSafeEnumerable());
        }
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

    [HarmonyPatch(typeof(Pawn_DrawTracker), MethodType.Constructor, new[] { typeof(Pawn) })]
    static class SeedDrawTrackerCtor
    {
        static void Prefix(Pawn pawn, ref bool __state)
        {
            if (Multiplayer.Client == null) return;

            Rand.PushState(pawn.thingIDNumber);
            __state = true;
        }

        static void Postfix(bool __state)
        {
            if (!__state) return;
            Rand.PopState();
        }
    }

    public static class PatchThingMethods
    {
        public static void Prefix(Thing __instance, ref Container<Map>? __state)
        {
            if (Multiplayer.Client == null) return;

            __state = __instance.Map;
            ThingContext.Push(__instance);

            if (__instance.def.CanHaveFaction)
                __instance.Map.PushFaction(__instance.Faction);
        }

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
        private static int nesting;

        public static bool Ignore
        {
            get => nesting > 0;
            set
            {
                if (value)
                {
                    nesting++;
                }
                else if (nesting > 0)
                {
                    nesting--;
                }
            }
        }

        public static void Prefix(ref bool __state)
        {
            Rand.PushState();
            Ignore = true;
            __state = true;
        }

        public static void Postfix(bool __state)
        {
            if (__state)
            {
                Rand.PopState();
                Ignore = false;
            }
        }
    }
}
