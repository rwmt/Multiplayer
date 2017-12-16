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

        [HarmonyPatch(typeof(JobDriver))]
        [HarmonyPatch(nameof(JobDriver.DriverTick))]
        public static class SeedJobDriverTick
        {
            static void Prefix(JobDriver __instance)
            {
                if (Multiplayer.client == null) return;
                Rand.Seed = __instance.job.loadID.Combine(Find.TickManager.TicksGame).Combine(Find.World.info.Seed);
            }
        }

        [HarmonyPatch(typeof(JobDriver))]
        [HarmonyPatch(nameof(JobDriver.ReadyForNextToil))]
        public static class SeedInitToil
        {
            static void Prefix(JobDriver __instance)
            {
                if (Multiplayer.client == null) return;
                Rand.Seed = __instance.job.loadID.Combine(Find.TickManager.TicksGame).Combine(Find.World.info.Seed).Combine(2361);
            }
        }

        [HarmonyPatch(typeof(Pawn_JobTracker))]
        [HarmonyPatch("DetermineNextJob")]
        public static class SeedNextJob
        {
            public static FieldInfo pawnField = typeof(Pawn_JobTracker).GetField("pawn", BindingFlags.Instance | BindingFlags.NonPublic);

            static void Prefix(Pawn_JobTracker __instance)
            {
                if (Multiplayer.client == null) return;
                Pawn pawn = (Pawn)pawnField.GetValue(__instance);
                Rand.Seed = pawn.thingIDNumber.Combine(Find.TickManager.TicksGame).Combine(Find.World.info.Seed).Combine(48358);
            }
        }

        [HarmonyPatch(typeof(Pawn_JobTracker))]
        [HarmonyPatch("DetermineNextConstantThinkTreeJob")]
        public static class SeedNextConstantJob
        {
            static void Prefix(Pawn_JobTracker __instance)
            {
                if (Multiplayer.client == null) return;
                Pawn pawn = (Pawn)SeedNextJob.pawnField.GetValue(__instance);
                Rand.Seed = pawn.thingIDNumber.Combine(Find.TickManager.TicksGame).Combine(Find.World.info.Seed).Combine(57362);
            }
        }
    }
}
