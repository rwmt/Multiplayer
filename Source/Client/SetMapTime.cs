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
    // Set the map time for GUI methods depending on it
    [MpPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_BeforeMainTabs))]
    [MpPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_AfterMainTabs))]
    [MpPatch(typeof(MapInterface), nameof(MapInterface.HandleMapClicks))]
    [MpPatch(typeof(MapInterface), nameof(MapInterface.HandleLowPriorityInput))]
    [MpPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceUpdate))]
    [MpPatch(typeof(SoundRoot), nameof(SoundRoot.Update))]
    [MpPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.ChoicesAtFor))]
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

    [MpPatch(typeof(PortraitsCache), nameof(PortraitsCache.IsAnimated))]
    static class PawnPortraitMapTime
    {
        static void Prefix(Pawn pawn, ref PrevTime? __state)
        {
            if (Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing) return;
            __state = PrevTime.GetAndSetToMap(pawn.MapHeld);
        }

        static void Postfix(PrevTime? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderPortrait))]
    static class PawnRenderPortraitMapTime
    {
        static void Prefix(PawnRenderer __instance, ref PrevTime? __state)
        {
            if (Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing) return;
            __state = PrevTime.GetAndSetToMap(__instance.pawn.MapHeld);
        }

        static void Postfix(PrevTime? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(PawnTweener), nameof(PawnTweener.PreDrawPosCalculation))]
    static class PreDrawPosCalculationMapTime
    {
        static void Prefix(PawnTweener __instance, ref PrevTime? __state)
        {
            if (Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing) return;
            __state = PrevTime.GetAndSetToMap(__instance.pawn.Map);
        }

        static void Postfix(PrevTime? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(DangerWatcher), nameof(DangerWatcher.DangerRating), MethodType.Getter)]
    static class DangerRatingMapTime
    {
        static void Prefix(DangerWatcher __instance, ref PrevTime? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = PrevTime.GetAndSetToMap(__instance.map);
        }

        static void Postfix(PrevTime? __state) => __state?.Set();
    }

    [MpPatch(typeof(Sustainer), nameof(Sustainer.SustainerUpdate))]
    [MpPatch(typeof(Sustainer), "<Sustainer>m__0")]
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

    [HarmonyPatch(typeof(TipSignal), MethodType.Constructor, new[] { typeof(Func<string>), typeof(int) })]
    static class TipSignalCtor
    {
        static void Prefix(ref Func<string> textGetter)
        {
            if (Multiplayer.Client == null) return;

            var current = PrevTime.Current();
            var getter = textGetter;

            textGetter = () =>
            {
                var prev = PrevTime.Current();
                current.Set();
                string s = getter();
                prev.Set();

                return s;
            };
        }
    }

    public struct PrevTime
    {
        public int ticks;
        public TimeSpeed speed;
        public TimeSlower slower;

        public void Set()
        {
            Find.TickManager.ticksGameInt = ticks;
            Find.TickManager.slower = slower;
            Find.TickManager.curTimeSpeed = speed;
        }

        public static PrevTime Current()
        {
            return new PrevTime()
            {
                ticks = Find.TickManager.ticksGameInt,
                speed = Find.TickManager.curTimeSpeed,
                slower = Find.TickManager.slower
            };
        }

        public static PrevTime? GetAndSetToMap(Map map)
        {
            if (map == null) return null;

            PrevTime prev = Current();

            var man = Find.TickManager;
            var comp = map.AsyncTime();

            man.ticksGameInt = comp.mapTicks;
            man.slower = comp.slower;
            man.CurTimeSpeed = comp.TimeSpeed;

            return prev;
        }
    }

}
