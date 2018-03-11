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
        public static MethodInfo skyTargetMethod = typeof(SkyManager).GetMethod("CurrentSkyTarget", BindingFlags.NonPublic | BindingFlags.Instance);
        static FieldInfo curSkyGlowField = typeof(SkyManager).GetField("curSkyGlowInt", BindingFlags.NonPublic | BindingFlags.Instance);

        static void Prefix(Map __instance)
        {
            if (Multiplayer.client == null) return;

            Multiplayer.Seed = __instance.uniqueID.Combine(Find.TickManager.TicksGame).Combine(Multiplayer.WorldComp.sessionId);

            // Reset the effects of SkyManagerUpdate
            SkyTarget target = (SkyTarget)skyTargetMethod.Invoke(__instance.skyManager, new object[0]);
            curSkyGlowField.SetValue(__instance.skyManager, target.glow);
        }
    }

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch(nameof(Map.MapPostTick))]
    public static class SeedMapPostTick
    {
        static void Prefix(Map __instance)
        {
            if (Multiplayer.client == null) return;
            Multiplayer.Seed = __instance.uniqueID.Combine(Find.TickManager.TicksGame).Combine(Multiplayer.WorldComp.sessionId).Combine(130531);
        }
    }

    [HarmonyPatch(typeof(World))]
    [HarmonyPatch(nameof(World.WorldTick))]
    public static class SeedWorldTick
    {
        static void Prefix(World __instance)
        {
            if (Multiplayer.client == null) return;
            Multiplayer.Seed = Find.TickManager.TicksGame.Combine(Multiplayer.WorldComp.sessionId).Combine(4624);
        }
    }

    [HarmonyPatch(typeof(WorldObject))]
    [HarmonyPatch(nameof(WorldObject.Tick))]
    public static class SeedWorldObjectTick
    {
        static void Prefix(WorldObject __instance)
        {
            if (Multiplayer.client == null) return;
            Multiplayer.Seed = __instance.ID.Combine(Find.TickManager.TicksGame).Combine(Multiplayer.WorldComp.sessionId);
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch(nameof(Pawn_JobTracker.JobTrackerTick))]
    public static class SeedJobTrackerTick
    {
        static void Prefix()
        {
            if (Multiplayer.client == null) return;
            Multiplayer.Seed = ThingContext.CurrentPawn.thingIDNumber.Combine(Find.TickManager.TicksGame).Combine(Multiplayer.WorldComp.sessionId).Combine(2141);
        }
    }

    public static class PatchThingTick
    {
        public static void Prefix(Thing __instance, ref Container<Map> __state)
        {
            if (Multiplayer.client == null) return;

            Multiplayer.Seed = __instance.thingIDNumber.Combine(Find.TickManager.TicksGame).Combine(Multiplayer.WorldComp.sessionId);
            ThingContext.Push(__instance);
            __state = __instance.Map;

            if (__instance is Pawn)
                __instance.Map.PushFaction(__instance.Faction);
        }

        public static void Postfix(Thing __instance, Container<Map> __state)
        {
            if (__state == null) return;

            if (__instance is Pawn)
                __state.PopFaction();

            ThingContext.Pop();
        }
    }

    public static class RandPatches
    {
        private static bool _ignore;
        private static int nesting;

        public static bool Ignore
        {
            get => _ignore;
            set
            {
                if (value)
                {
                    if (_ignore)
                    {
                        nesting++;
                        Log.Message("Nested rand ignore!");
                    }
                    else
                        _ignore = true;
                }
                else
                {
                    if (nesting > 0)
                        nesting--;
                    else
                        _ignore = false;
                }
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
