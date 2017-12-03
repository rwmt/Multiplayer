using Harmony;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI;

namespace ServerMod
{
    class Seeds
    {
        [HarmonyPatch(typeof(Thing))]
        [HarmonyPatch(nameof(Thing.Tick))]
        public static class SeedThingTick
        {
            static void Prefix(Thing __instance)
            {
                if (ServerMod.client == null) return;
                Rand.Seed = __instance.thingIDNumber.Combine(Find.TickManager.TicksGame).Combine(Find.World.info.Seed);
            }
        }

        [HarmonyPatch(typeof(WorldObject))]
        [HarmonyPatch(nameof(WorldObject.Tick))]
        public static class SeedWorldObjectTick
        {
            static void Prefix(WorldObject __instance)
            {
                if (ServerMod.client == null) return;
                Rand.Seed = __instance.ID.Combine(Find.TickManager.TicksGame).Combine(Find.World.info.Seed);
            }
        }

        [HarmonyPatch(typeof(JobDriver))]
        [HarmonyPatch(nameof(JobDriver.DriverTick))]
        public static class SeedJobDriverTick
        {
            static void Prefix(JobDriver __instance)
            {
                if (ServerMod.client == null) return;
                Rand.Seed = __instance.job.loadID.Combine(Find.TickManager.TicksGame).Combine(Find.World.info.Seed);
            }
        }

        [HarmonyPatch(typeof(JobDriver))]
        [HarmonyPatch(nameof(JobDriver.ReadyForNextToil))]
        public static class SeedInitToil
        {
            static void Prefix(JobDriver __instance)
            {
                if (ServerMod.client == null) return;
                Rand.Seed = __instance.job.loadID.Combine(Find.TickManager.TicksGame).Combine(Find.World.info.Seed);
            }
        }
    }
}
