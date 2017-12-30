using Harmony;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Verse;
using Verse.AI;

namespace Multiplayer
{
    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch(nameof(Map.MapPreTick))]
    public static class SeedMapPreTick
    {
        static void Prefix(Map __instance)
        {
            if (Multiplayer.client == null) return;
            Rand.Seed = __instance.uniqueID.Combine(Find.TickManager.TicksGame).Combine(Multiplayer.WorldComp.sessionId);
        }
    }

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch(nameof(Map.MapPostTick))]
    public static class SeedMapPostTick
    {
        static void Prefix(Map __instance)
        {
            if (Multiplayer.client == null) return;
            Rand.Seed = __instance.uniqueID.Combine(Find.TickManager.TicksGame).Combine(Multiplayer.WorldComp.sessionId).Combine(130531);
        }
    }

    [HarmonyPatch(typeof(World))]
    [HarmonyPatch(nameof(World.WorldTick))]
    public static class SeedWorldTick
    {
        static void Prefix(World __instance)
        {
            if (Multiplayer.client == null) return;
            Rand.Seed = Find.TickManager.TicksGame.Combine(Multiplayer.WorldComp.sessionId).Combine(4624);
        }
    }

    [HarmonyPatch(typeof(WorldObject))]
    [HarmonyPatch(nameof(WorldObject.Tick))]
    public static class SeedWorldObjectTick
    {
        static void Prefix(WorldObject __instance)
        {
            if (Multiplayer.client == null) return;
            Rand.Seed = __instance.ID.Combine(Find.TickManager.TicksGame).Combine(Multiplayer.WorldComp.sessionId);
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.JobTrackerTick))]
    public static class SeedJobTrackerTick
    {
        static void Prefix()
        {
            if (Multiplayer.client == null) return;
            Rand.Seed = PawnContext.current.thingIDNumber.Combine(Find.TickManager.TicksGame).Combine(Multiplayer.WorldComp.sessionId).Combine(2141);
        }
    }

    public static class SeedThingTick
    {
        public static void Prefix(Thing __instance)
        {
            if (Multiplayer.client == null) return;
            Rand.Seed = __instance.thingIDNumber.Combine(Find.TickManager.TicksGame).Combine(Multiplayer.WorldComp.sessionId);
        }
    }

    public static class RandPatches
    {
        public static bool ignore;

        public static void Prefix()
        {
            Rand.PushState();
            ignore = true;
        }

        public static void Postfix()
        {
            Rand.PopState();
            ignore = false;
        }
    }
}
