using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client
{
    // Set the map time for GUI methods depending on it
    [HarmonyPatch]
    static class SetMapTimeForUI
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_BeforeMainTabs));
            yield return AccessTools.Method(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_AfterMainTabs));
            yield return AccessTools.Method(typeof(MapInterface), nameof(MapInterface.HandleMapClicks));
            yield return AccessTools.Method(typeof(MapInterface), nameof(MapInterface.HandleLowPriorityInput));
            yield return AccessTools.Method(typeof(MapInterface), nameof(MapInterface.MapInterfaceUpdate));
            yield return AccessTools.Method(typeof(SoundRoot), nameof(SoundRoot.Update));
            yield return AccessTools.Method(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.ChoicesAtFor));
        }

        [HarmonyPriority(Priority.First + 1)]
        static void Prefix(ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null || WorldRendererUtility.WorldRenderedNow || Find.CurrentMap == null) return;
            __state = TimeSnapshot.GetAndSetFromMap(Find.CurrentMap);
        }

        [HarmonyPriority(MpPriority.MpLast)]
        static void Postfix(TimeSnapshot? __state) => __state?.Set();
    }

    [HarmonyPatch]
    static class MapUpdateTimePatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Map), nameof(Map.MapUpdate));
            yield return AccessTools.Method(typeof(Map), nameof(Map.FinalizeLoading));
        }

        [HarmonyPriority(Priority.First + 1)]
        static void Prefix(Map __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = TimeSnapshot.GetAndSetFromMap(__instance);
        }

        [HarmonyPriority(Priority.Last - 1)]
        static void Postfix(TimeSnapshot? __state) => __state?.Set();
    }

    [HarmonyPatch]
    static class PawnPortraitMapTime
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PortraitsCache), nameof(PortraitsCache.IsAnimated));
        }

        static void Prefix(Pawn pawn, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing) return;
            __state = TimeSnapshot.GetAndSetFromMap(pawn.MapHeld);
        }

        static void Postfix(TimeSnapshot? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.RenderPortrait))]
    static class PawnRenderPortraitMapTime
    {
        static void Prefix(PawnRenderer __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing) return;
            __state = TimeSnapshot.GetAndSetFromMap(__instance.pawn.MapHeld);
        }

        static void Postfix(TimeSnapshot? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(PawnTweener), nameof(PawnTweener.PreDrawPosCalculation))]
    static class PreDrawPosCalculationMapTime
    {
        static void Prefix(PawnTweener __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing) return;
            __state = TimeSnapshot.GetAndSetFromMap(__instance.pawn.Map);
        }

        static void Postfix(TimeSnapshot? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(DangerWatcher), nameof(DangerWatcher.DangerRating), MethodType.Getter)]
    static class DangerRatingMapTime
    {
        static void Prefix(DangerWatcher __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = TimeSnapshot.GetAndSetFromMap(__instance.map);
        }

        static void Postfix(TimeSnapshot? __state) => __state?.Set();
    }

    [HarmonyPatch]
    static class SustainerUpdateMapTime
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Sustainer), nameof(Sustainer.SustainerUpdate));
            yield return AccessTools.Method(typeof(Sustainer), "<.ctor>b__15_0");
        }

        static void Prefix(Sustainer __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = TimeSnapshot.GetAndSetFromMap(__instance.info.Maker.Map);
        }

        static void Postfix(TimeSnapshot? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(Sample), nameof(Sample.Update))]
    static class SampleUpdateMapTime
    {
        static void Prefix(Sample __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = TimeSnapshot.GetAndSetFromMap(__instance.Map);
        }

        static void Postfix(TimeSnapshot? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(TipSignal), MethodType.Constructor, new[] { typeof(Func<string>), typeof(int) })]
    static class TipSignalCtor
    {
        static void Prefix(ref Func<string> textGetter)
        {
            if (Multiplayer.Client == null) return;

            var current = TimeSnapshot.Current();
            var getter = textGetter;

            textGetter = () =>
            {
                var prev = TimeSnapshot.Current();
                current.Set();
                string s = getter();
                prev.Set();

                return s;
            };
        }
    }

    public struct TimeSnapshot
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

        public static TimeSnapshot Current()
        {
            return new TimeSnapshot()
            {
                ticks = Find.TickManager.ticksGameInt,
                speed = Find.TickManager.curTimeSpeed,
                slower = Find.TickManager.slower
            };
        }

        public static TimeSnapshot? GetAndSetFromMap(Map map)
        {
            if (map == null) return null;

            TimeSnapshot prev = Current();

            var man = Find.TickManager;
            var comp = map.AsyncTime();

            man.ticksGameInt = comp.mapTicks;
            man.slower = comp.slower;
            man.CurTimeSpeed = comp.TimeSpeed;

            return prev;
        }
    }

}
