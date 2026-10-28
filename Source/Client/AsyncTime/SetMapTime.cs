using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
using Multiplayer.Client.Util;
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
            yield return AccessTools.Method(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions));
        }

        [HarmonyPriority(MpPriority.MpFirst)]
        internal static void Prefix(ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null || WorldRendererUtility.WorldSelected || Find.CurrentMap == null) return;
            __state = TimeSnapshot.GetAndSetFromMap(Find.CurrentMap);
        }

        [HarmonyPriority(MpPriority.MpLast)]
        internal static void Finalizer(TimeSnapshot? __state) => __state?.Set();
    }

    // The hediff's pawn may live on another map or in a caravan, so its
    // elapsed-time strings must come from the pawn's own clock, not the
    // viewed map's (it used to sit in the SetMapTimeForUI list above).
    [HarmonyPatch(typeof(Hediff), nameof(Hediff.GetTooltip))]
    static class HediffTooltipMapTime
    {
        static void Prefix(Hediff __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = TimeSnapshot.GetAndSetFromMap(__instance.pawn?.MapHeld);
        }

        static void Finalizer(TimeSnapshot? __state) => __state?.Set();
    }

    // Alerts iterate all maps, so no single map's clock is right for them;
    // the session-wide world clock keeps countdowns advancing regardless of
    // which map is viewed and keeps the update and draw halves consistent
    // with each other (previously AlertsReadoutUpdate ran under the viewed
    // map's clock and AlertsReadoutOnGUI under the frame ambient - the same
    // alert evaluated under two clocks in one frame).
    [HarmonyPatch]
    static class AlertsReadoutWorldTime
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(AlertsReadout), nameof(AlertsReadout.AlertsReadoutUpdate));
            yield return AccessTools.Method(typeof(AlertsReadout), nameof(AlertsReadout.AlertsReadoutOnGUI));
        }

        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = TimeSnapshot.GetAndSetFromWorld();
        }

        [HarmonyPriority(MpPriority.MpLast)]
        static void Finalizer(TimeSnapshot? __state) => __state?.Set();
    }

    [HarmonyPatch]
    static class MapUpdateTimePatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Map), nameof(Map.MapUpdate));
            yield return AccessTools.Method(typeof(Map), nameof(Map.FinalizeLoading));
        }

        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(Map __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = TimeSnapshot.GetAndSetFromMap(__instance);
        }

        [HarmonyPriority(MpPriority.MpLast)]
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

    // Portraits render through PawnCacheRenderer.RenderPawn since the 1.3
    // renderer rewrite (PawnRenderer.RenderPortrait no longer exists);
    // camera.Render() is synchronous, so this pair brackets OnPostRender ->
    // PawnRenderer.RenderCache, which reads the clock for the damage flasher
    // and render-tree animation frames. Without it a portrait renders under
    // whatever clock the frame has ambient while PortraitsCache.IsAnimated
    // (patched above) decides re-renders under the pawn's own map clock.
    [HarmonyPatch(typeof(PawnCacheRenderer), nameof(PawnCacheRenderer.RenderPawn))]
    static class PawnRenderPortraitMapTime
    {
        static void Prefix(Pawn pawn, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing) return;
            __state = TimeSnapshot.GetAndSetFromMap(pawn.MapHeld);
        }

        static void Postfix(TimeSnapshot? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(PawnTweener), nameof(PawnTweener.PreDrawPosCalculation))]
    static class PreDrawPosCalculationMapTime
    {
        static void Prefix(PawnTweener __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing) return;
            // MapHeld, not Map: a carried/transported pawn has no map of its
            // own, and with no install its tween ran under the viewer's clock
            // (stamping lastDrawTick with it), then hard-snapped when a real
            // clock returned. The holder's map clock is the motion's basis.
            __state = TimeSnapshot.GetAndSetFromMap(__instance.pawn.MapHeld);
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
            yield return MpMethodUtil.GetLambda(typeof(Sustainer), parentMethodType: MethodType.Constructor, parentArgs: new[] { typeof(SoundDef), typeof(SoundInfo) });
        }

        static void Prefix(Sustainer __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.game == null) return;
            // Most sustainers have no map: the Sustainer constructor
            // downgrades any def without world sub-sounds to OnCamera, making
            // Maker invalid. Falling through with no install left those
            // running under the viewed map's clock while Maintain() stamps
            // the maintainer's ambient tick - a cross-clock pair that ends
            // (and respawns) the sustainer every frame when the viewer's
            // clock is ahead. The world clock is the only session-wide
            // monotone basis, so install it for map-less sound code.
            __state = TimeSnapshot.GetAndSetFromMap(__instance.info.Maker.Map)
                      ?? TimeSnapshot.GetAndSetFromWorld();
        }

        static void Postfix(TimeSnapshot? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(Sample), nameof(Sample.Update))]
    static class SampleUpdateMapTime
    {
        static void Prefix(Sample __instance, ref TimeSnapshot? __state)
        {
            if (Multiplayer.game == null) return;
            // World-clock fallback for map-less samples - see
            // SustainerUpdateMapTime above
            __state = TimeSnapshot.GetAndSetFromMap(__instance.Map)
                      ?? TimeSnapshot.GetAndSetFromWorld();
        }

        static void Postfix(TimeSnapshot? __state) => __state?.Set();
    }

    // The one-shot reaper decides "finished" partly from Find.TickManager.Paused,
    // which under the viewer install is the VIEWED map's pause state: a paused
    // viewer never reaps finished tempo-affected one-shots, they accumulate, and
    // the voice limiter then cuts/restarts sounds on every new play. The world
    // clock pauses only when the whole session does, so reaping tracks actual
    // sim activity. Per-sample Update calls nest their own map/world snapshots
    // inside this bracket (SampleUpdateMapTime).
    [HarmonyPatch(typeof(SampleOneShotManager), nameof(SampleOneShotManager.SampleOneShotManagerUpdate))]
    static class OneShotReaperWorldTime
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(ref TimeSnapshot? __state)
        {
            if (Multiplayer.game == null) return;
            __state = TimeSnapshot.GetAndSetFromWorld();
        }

        [HarmonyPriority(MpPriority.MpLast)]
        static void Finalizer(TimeSnapshot? __state) => __state?.Set();
    }

    [HarmonyPatch(typeof(TipSignal), MethodType.Constructor, new[] { typeof(Func<string>), typeof(int) })]
    static class TipSignalCtor
    {
        static void Prefix(ref Func<string> textGetter)
        {
            if (Multiplayer.game == null) return;

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

    // Patch to remove endless (every tick!) condition duplicates from the condition causers
    [HarmonyPatch(typeof(CompCauseGameCondition), nameof(CompCauseGameCondition.GetConditionInstance))]
    public static class Patch_CompCauseGameCondition_GetConditionInstance
    {
        public static void Postfix(Map map, CompCauseGameCondition __instance, ref GameCondition __result)
        {
            // Sanity and MP checks. We only checking for conditions that are able to stack
            if (Multiplayer.Client == null
                    || __instance.Props.preventConditionStacking
                    || __result != null
                    || map == null)
                return;

            // Look for an existing active condition on this map that matches both def and causer
            var active = map.GameConditionManager.ActiveConditions
                .FirstOrDefault(c =>
                    c.def == __instance.Props.conditionDef &&
                    c.conditionCauser == __instance.parent);

            if (active != null)
                __result = active;
        }
    }

    [HarmonyPatch(typeof(CompCauseGameCondition), nameof(CompCauseGameCondition.EnforceConditionOn))]
    static class MapConditionCauserMapTime
    {
        static void Prefix(Map map, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = TimeSnapshot.GetAndSetFromMap(map);
        }

        static void Finalizer(TimeSnapshot? __state) => __state?.Set();
    }

    public struct TimeSnapshot
    {
        public int ticks;
        public TimeSpeed speed;
        public TimeSlower slower;
        public int gameStartAbsTick;

        public void Set()
        {
            Find.TickManager.ticksGameInt = ticks;
            Find.TickManager.slower = slower;
            Find.TickManager.curTimeSpeed = speed;
            Find.TickManager.gameStartAbsTick = gameStartAbsTick;
        }

        public static TimeSnapshot Current()
        {
            return new TimeSnapshot
            {
                ticks = Find.TickManager.ticksGameInt,
                speed = Find.TickManager.curTimeSpeed,
                slower = Find.TickManager.slower,
                gameStartAbsTick = Find.TickManager.gameStartAbsTick
            };
        }

        public static TimeSnapshot? GetAndSetFromMap(Map map)
        {
            if (map == null) return null;

            // A map mid-generation is in Find.Maps before its AsyncTimeComp is
            // registered - no comp means no map clock to install yet, same
            // contract as the null-world guard in GetAndSetFromWorld
            var mapComp = map.AsyncTime();
            if (mapComp == null) return null;

            TimeSnapshot prev = Current();

            var tickManager = Find.TickManager;

            // Field, not the property: the vanilla setter silently drops
            // writes (and can emit a RejectInput message) when
            // !PlayerCanControl, e.g. during gravship landing confirmation,
            // and a context install must never be lossy - Set() already
            // writes the field on restore
            tickManager.ticksGameInt = mapComp.mapTicks;
            tickManager.slower = mapComp.slower;
            tickManager.curTimeSpeed = mapComp.DesiredTimeSpeed;
            tickManager.gameStartAbsTick = mapComp.GameStartAbsTick;

            return prev;
        }

        // The world half of GetAndSetFromMap. Writes the curTimeSpeed field,
        // not the property: the vanilla setter silently drops writes (and can
        // emit a RejectInput message) when !PlayerCanControl, e.g. during
        // gravship landing confirmation, and a context install must never be
        // lossy.
        public static TimeSnapshot? GetAndSetFromWorld()
        {
            var worldComp = Multiplayer.AsyncWorldTime;
            if (worldComp == null) return null;

            TimeSnapshot prev = Current();
            var tickManager = Find.TickManager;

            tickManager.ticksGameInt = worldComp.worldTicks;
            tickManager.slower = worldComp.slower;
            tickManager.curTimeSpeed = worldComp.DesiredTimeSpeed;
            tickManager.gameStartAbsTick = worldComp.worldGameStartAbsTick;

            return prev;
        }
    }

    // World rendering and world-object updates key caches and motion deltas
    // on the ambient clock: WorldDrawLayer_Satellites regenerates whenever
    // lastUpdate != TicksGame and integrates orbit rotation by the delta,
    // WorldObject.DrawPos caches per tick. The world clock is their correct
    // basis - caravans and orbits move on world ticks - while an ambient that
    // alternates with the viewer's clock regenerates the layer every frame
    // (the observed world-render degradation) or jumps deltas
    // backwards. Plain snapshot bracket, deliberately not PreContext: no Rand
    // or faction state may change on a render path. Runs even while a surface
    // map is viewed (background world render).
    [HarmonyPatch(typeof(World), nameof(World.WorldUpdate))]
    static class WorldUpdateWorldTime
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = TimeSnapshot.GetAndSetFromWorld();
        }

        [HarmonyPriority(MpPriority.MpLast)]
        static void Finalizer(TimeSnapshot? __state) => __state?.Set();
    }

    // Letters are world-scoped state read between ticks: arrival and timeout
    // stamps must be compared under one session-wide clock rather than
    // whatever the frame left ambient. This preserves the clock the
    // method effectively ran under before the viewer context owned the frame.
    // Letters.cs patches the same method for unrelated reasons.
    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.LetterStackUpdate))]
    static class LetterStackUpdateWorldTime
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null) return;
            __state = TimeSnapshot.GetAndSetFromWorld();
        }

        [HarmonyPriority(MpPriority.MpLast)]
        static void Finalizer(TimeSnapshot? __state) => __state?.Set();
    }

}
