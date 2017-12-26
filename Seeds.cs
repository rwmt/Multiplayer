using Harmony;
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
    class Seeds
    {
        [HarmonyPatch(typeof(Map))]
        [HarmonyPatch(nameof(Map.MapPreTick))]
        public static class SeedMapPreTick
        {
            static void Prefix(Map __instance)
            {
                if (Multiplayer.client == null) return;
                Rand.Seed = __instance.uniqueID.Combine(Find.TickManager.TicksGame).Combine(Find.World.info.Seed);
            }
        }

        [HarmonyPatch(typeof(Map))]
        [HarmonyPatch(nameof(Map.MapPostTick))]
        public static class SeedMapPostTick
        {
            static void Prefix(Map __instance)
            {
                if (Multiplayer.client == null) return;
                Rand.Seed = __instance.uniqueID.Combine(Find.TickManager.TicksGame).Combine(Find.World.info.Seed).Combine(130531);
            }
        }

        [HarmonyPatch(typeof(World))]
        [HarmonyPatch(nameof(World.WorldTick))]
        public static class SeedWorldTick
        {
            static void Prefix(World __instance)
            {
                if (Multiplayer.client == null) return;
                Rand.Seed = Find.TickManager.TicksGame.Combine(Find.World.info.Seed).Combine(4624);
            }
        }

        [HarmonyPatch(typeof(ThingWithComps))]
        [HarmonyPatch(nameof(ThingWithComps.Tick))]
        public static class SeedThingWithCompsTick
        {
            static void Prefix(Thing __instance)
            {
                if (Multiplayer.client == null) return;
                Rand.Seed = __instance.thingIDNumber.Combine(Find.TickManager.TicksGame).Combine(Find.World.info.Seed);
            }
        }

        [HarmonyPatch(typeof(Pawn))]
        [HarmonyPatch(nameof(Pawn.Tick))]
        public static class SeedPawnTick
        {
            static void Prefix(Pawn __instance)
            {
                if (Multiplayer.client == null) return;
                Rand.Seed = __instance.thingIDNumber.Combine(Find.TickManager.TicksGame).Combine(Find.World.info.Seed).Combine(3167);
            }
        }

        [HarmonyPatch(typeof(WorldObject))]
        [HarmonyPatch(nameof(WorldObject.Tick))]
        public static class SeedWorldObjectTick
        {
            static void Prefix(WorldObject __instance)
            {
                if (Multiplayer.client == null) return;
                Rand.Seed = __instance.ID.Combine(Find.TickManager.TicksGame).Combine(Find.World.info.Seed);
            }
        }

        [HarmonyPatch(typeof(Pawn_JobTracker))]
        [HarmonyPatch(nameof(Pawn_JobTracker.JobTrackerTick))]
        public static class SeedJobTrackerTick
        {
            static void Prefix()
            {
                if (Multiplayer.client == null) return;
                Rand.Seed = PawnContext.current.thingIDNumber.Combine(Find.TickManager.TicksGame).Combine(Find.World.info.Seed).Combine(2141);
            }
        }
    }
}
