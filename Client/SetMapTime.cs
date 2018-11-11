using Harmony;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client
{
    static class SetMapTimeForUI
    {
        static void Prefix(ref PrevTime? __state)
        {
            if (Multiplayer.Client == null || WorldRendererUtility.WorldRenderedNow || Find.CurrentMap == null) return;
            __state = PrevTime.GetAndSetToMap(Find.CurrentMap);
        }

        static void Postfix(PrevTime? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
    static class MapUpdateTimePatch
    {
        static void Prefix(Map __instance, ref PrevTime? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = PrevTime.GetAndSetToMap(__instance);
        }

        static void Postfix(PrevTime? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(PortraitsCache), nameof(PortraitsCache.IsAnimated))]
    static class PawnPortraitMapTime
    {
        static void Prefix(Pawn pawn, ref PrevTime? __state)
        {
            if (Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing) return;
            __state = PrevTime.GetAndSetToMap(pawn.Map);
        }

        static void Postfix(PrevTime? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderPortrait))]
    static class PawnRenderPortraitMapTime
    {
        static void Prefix(PawnRenderer __instance, ref PrevTime? __state)
        {
            if (Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing) return;
            __state = PrevTime.GetAndSetToMap(__instance.pawn.Map);
        }

        static void Postfix(PrevTime? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(DangerWatcher))]
    [HarmonyPatch(nameof(DangerWatcher.DangerRating), MethodType.Getter)]
    static class DangerRatingMapTime
    {
        static void Prefix(DangerWatcher __instance, ref PrevTime? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = PrevTime.GetAndSetToMap(__instance.map);
        }

        static void Postfix(PrevTime? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(Sustainer), nameof(Sustainer.SustainerUpdate))]
    static class SustainerUpdateMapTime
    {
        static void Prefix(Sustainer __instance, ref PrevTime? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = PrevTime.GetAndSetToMap(__instance.info.Maker.Map);
        }

        static void Postfix(PrevTime? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(Sample), nameof(Sample.Update))]
    static class SampleUpdateMapTime
    {
        static void Prefix(Sample __instance, ref PrevTime? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = PrevTime.GetAndSetToMap(__instance.Map);
        }

        static void Postfix(PrevTime? __state) => __state?.Set();
    }

    public struct PrevTime
    {
        public int ticks;
        public TimeSpeed speed;

        public void Set()
        {
            Find.TickManager.ticksGameInt = ticks;
            Find.TickManager.curTimeSpeed = speed;
        }

        public static PrevTime? GetAndSetToMap(Map map)
        {
            if (map == null) return null;

            PrevTime prev = new PrevTime()
            {
                ticks = Find.TickManager.ticksGameInt,
                speed = Find.TickManager.curTimeSpeed
            };

            Find.TickManager.ticksGameInt = map.AsyncTime().mapTicks;
            Find.TickManager.CurTimeSpeed = map.AsyncTime().TimeSpeed;

            return prev;
        }
    }

}
