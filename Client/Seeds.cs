using Harmony;
using Multiplayer.Common;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client
{
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

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch(nameof(Map.FinalizeLoading))]
    public static class SeedMapFinalizeLoading
    {
        static void Prefix(Map __instance)
        {
            if (Multiplayer.client == null) return;
            Multiplayer.Seed = __instance.uniqueID.Combine(Multiplayer.WorldComp.sessionId);
        }
    }

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch("ExposeComponents")]
    public static class SeedMapLoading
    {
        static void Prefix(Map __instance)
        {
            if (Multiplayer.client == null) return;
            if (Scribe.mode != LoadSaveMode.LoadingVars && Scribe.mode != LoadSaveMode.PostLoadInit) return;

            Multiplayer.Seed = __instance.uniqueID.Combine(Multiplayer.WorldComp.sessionId);
        }
    }

    public static class PatchThingMethods
    {
        public static void Prefix(Thing __instance, ref Container<Map> __state)
        {
            if (Multiplayer.client == null) return;

            __state = __instance.Map;
            Multiplayer.Seed = __instance.thingIDNumber.Combine(Find.TickManager.TicksGame).Combine(Multiplayer.WorldComp.sessionId);
            ThingContext.Push(__instance);

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
