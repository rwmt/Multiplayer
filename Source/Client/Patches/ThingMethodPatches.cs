using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Multiplayer.Client.Factions;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(PawnComponentsUtility), nameof(PawnComponentsUtility.AddAndRemoveDynamicComponents))]
    public static class AddAndRemoveCompsPatch
    {
        static void Prefix(Pawn pawn, ref Container<Map>? __state)
        {
            if (Multiplayer.Client == null || pawn.Faction == null) return;

            pawn.Map.PushFaction(pawn.Faction);
            __state = pawn.Map;
        }

        static void Finalizer(Pawn pawn, Container<Map>? __state)
        {
            if (__state is { Inner: var map })
                map.PopFaction();
        }
    }

    [HarmonyPatch(typeof(Pawn_NeedsTracker), nameof(Pawn_NeedsTracker.AddOrRemoveNeedsAsAppropriate))]
    public static class AddRemoveNeedsPatch
    {
        static void Prefix(Pawn ___pawn, ref Container<Map>? __state)
        {
            if (Multiplayer.Client == null || ___pawn.Faction == null) return;

            ___pawn.Map.PushFaction(___pawn.Faction);
            __state = ___pawn.Map;
        }

        static void Finalizer(Container<Map>? __state)
        {
            if (__state is { Inner: var map })
                map.PopFaction();
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class JobTrackerStart
    {
        static void Prefix(Pawn_JobTracker __instance, Job newJob, ref Container<Map>? __state)
        {
            if (Multiplayer.Client == null) return;

            if (Multiplayer.InInterface)
            {
                Log.Warning($"Started a job {newJob} on pawn {__instance.pawn} from the interface!");
                return;
            }

            Pawn pawn = __instance.pawn;

            if (pawn.Faction == null || !pawn.Spawned) return;

            pawn.Map.PushFaction(pawn.Faction);
            __state = pawn.Map;
        }

        static void Finalizer(Container<Map>? __state)
        {
            if (__state is { Inner: var map })
                map.PopFaction();
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class JobTrackerEndCurrent
    {
        static void Prefix(Pawn_JobTracker __instance, JobCondition condition, ref Container<Map>? __state)
        {
            if (Multiplayer.Client == null) return;
            Pawn pawn = __instance.pawn;

            if (pawn.Faction == null || !pawn.Spawned) return;

            pawn.Map.PushFaction(pawn.Faction);
            __state = pawn.Map;
        }

        static void Finalizer(Container<Map>? __state)
        {
            if (__state is { Inner: var map })
                map.PopFaction();
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.CheckForJobOverride))]
    public static class JobTrackerOverride
    {
        static void Prefix(Pawn_JobTracker __instance, ref Container<Map>? __state)
        {
            if (Multiplayer.Client == null) return;
            Pawn pawn = __instance.pawn;

            if (pawn.Faction == null || !pawn.Spawned) return;

            pawn.Map.PushFaction(pawn.Faction);
            ThingContext.Push(pawn);
            __state = pawn.Map;
        }

        static void Finalizer(Container<Map>? __state)
        {
            if (__state is { Inner: var map })
            {
                map.PopFaction();
                ThingContext.Pop();
            }
        }
    }

    public static class ThingMethodPatches
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

        [HarmonyPriority(MpPriority.MpFirst)]
        public static void Prefix_SpawnSetup(Thing __instance, Map __0, ref Container<Map>? __state)
        {
            if (Multiplayer.Client == null) return;

            __state = __0;
            ThingContext.Push(__instance);

            if (__instance.def.CanHaveFaction)
                __0.PushFaction(__instance.Faction);
        }

        [HarmonyPriority(MpPriority.MpLast)]
        public static void Finalizer(Thing __instance, Container<Map>? __state)
        {
            if (__state is not { Inner: var map }) return;

            if (__instance.def.CanHaveFaction)
                map.PopFaction();

            ThingContext.Pop();
        }
    }

    public static class ThingContext
    {
        public static Stack<(Thing, Map)> stack = new();

        static ThingContext() => Clear();

        public static Thing Current => stack.Peek().Item1;
        public static Pawn CurrentPawn => Current as Pawn;

        public static Map CurrentMap
        {
            get
            {
                var peek = stack.Peek();
                if (peek.Item1 != null && peek.Item1.Map != peek.Item2)
                    Log.ErrorOnce($"Thing {peek.Item1} has changed its map!", peek.Item1.thingIDNumber ^ 57481021);
                return peek.Item2;
            }
        }

        public static void Push(Thing t)
        {
            stack.Push((t, t.Map));
        }

        public static void Pop()
        {
            stack.Pop();
        }

        public static void Clear()
        {
            stack.Clear();
            stack.Push((null, null));
        }
    }
}
