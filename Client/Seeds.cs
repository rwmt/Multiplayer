using Harmony;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System.Collections;
using System.Collections.Generic;
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
            if (Multiplayer.client == null) return;
            Multiplayer.Seed = 1;
        }
    }

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch(nameof(Map.ExposeData))]
    public static class SeedMapLoad
    {
        static void Prefix(Map __instance, ref bool __state)
        {
            if (Multiplayer.client == null) return;
            if (Scribe.mode != LoadSaveMode.LoadingVars && Scribe.mode != LoadSaveMode.ResolvingCrossRefs && Scribe.mode != LoadSaveMode.PostLoadInit) return;

            int seed = __instance.uniqueID.Combine(Multiplayer.WorldComp.sessionId);
            Rand.PushState(seed);
            UnityRandomSeed.Push(seed);

            __state = true;
        }

        static void Postfix(bool __state)
        {
            if (__state)
            {
                Rand.PopState();
                UnityRandomSeed.Pop();
            }
        }
    }

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch(nameof(Map.FinalizeLoading))]
    public static class SeedMapFinalizeLoading
    {
        static void Prefix(Map __instance, ref bool __state)
        {
            if (Multiplayer.client == null) return;

            int seed = __instance.uniqueID.Combine(Multiplayer.WorldComp.sessionId);
            Rand.PushState(seed);
            UnityRandomSeed.Push(seed);

            __state = true;
        }

        static void Postfix(bool __state)
        {
            if (__state)
            {
                Rand.PopState();
                UnityRandomSeed.Pop();
            }
        }
    }

    [MpPatch(typeof(VoluntarilyJoinableLordsStarter), "Tick_TryStartParty")]
    [MpPatch(typeof(DamageWatcher), nameof(DamageWatcher.Notify_DamageTaken))]
    public static class MapParentFactionPatch
    {
        static void Prefix(ref bool __state)
        {
            if (MapAsyncTimeComp.tickingMap is Map map)
            {
                map.PushFaction(map.ParentFaction);
                __state = true;
            }
        }

        static void Postfix(bool __state)
        {
            if (__state)
                MapAsyncTimeComp.tickingMap.PopFaction();
        }
    }

    public static class PatchThingMethods
    {
        public static void Prefix(Thing __instance, ref Container<Map> __state)
        {
            if (Multiplayer.client == null) return;

            __state = __instance.Map;
            ThingContext.Push(__instance);

            if (__instance is Pawn || __instance is Building)
                __instance.Map.PushFaction(__instance.Faction);
        }

        public static void Postfix(Thing __instance, Container<Map> __state)
        {
            if (__state == null) return;

            if (__instance is Pawn || __instance is Building)
                __state.PopFaction();

            ThingContext.Pop();
        }
    }

    public static class UnityRandomSeed
    {
        private static Stack<Random.State> stack = new Stack<Random.State>();

        public static void Push(int seed)
        {
            stack.Push(Random.state);
            Random.InitState(seed);
        }

        public static void Pop()
        {
            Random.state = stack.Pop();
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
                    if (nesting > 0)
                        Log.Message("Nested rand ignore!");
                    nesting++;
                }
                else if (nesting > 0)
                    nesting--;
            }
        }

        public static void Prefix()
        {
            Rand.PushState();
            Ignore = true;
        }

        public static void Postfix()
        {
            Rand.PopState();
            Ignore = false;
        }
    }
}
