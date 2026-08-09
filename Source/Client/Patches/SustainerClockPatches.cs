using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.Client.Util;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client.Patches
{
    // Sustainers with PerTick/PerTickRare maintenance compare an ambient tick
    // against lastMaintainTick, but under per-map clocks the stamp and the
    // check can come from different clocks: Maintain() runs under whatever
    // context the maintainer had (often a map's tick loop), while
    // SustainerUpdate runs under the sound bracket's clock (owner map, or the
    // world clock for map-less sustainers). A stamp clock behind the check
    // clock reads as "unmaintained" and ends the sustainer even though its
    // owner maintained it microseconds ago - and owners respawn ended
    // sustainers, so the sound restarts every frame.
    //
    // Fix: before the vanilla staleness check runs, if the sustainer LOOKS
    // tick-stale but was really maintained within the grace window (measured
    // in realtime, which no clock swap can touch), re-stamp it under the
    // clock the check will use. Truly abandoned sustainers still die, at most
    // GraceSeconds late - audio-only, imperceptible. PerFrame maintenance is
    // untouched (frame counts are global). Vanilla paused behavior is
    // untouched (a frozen clock never reads as stale).
    public static class SustainerRealtimeMaintenance
    {
        public static readonly Dictionary<Sustainer, float> lastMaintainRealTime = new();

        public const float GraceSeconds = 1f;

        private static float lastRescueReport;
        private static int rescueCount;

        public static void NoteRescue(Sustainer sustainer, int ambientTicks, int staleStamp)
        {
            rescueCount++;
            var now = Time.realtimeSinceStartup;
            if (now - lastRescueReport < 60f) return;
            lastRescueReport = now;
            // MpLog.Log, not Debug: Debug is [Conditional("DEBUG")] and
            // compiled out of the Release zips the field actually runs -
            // this line is field evidence, it must exist there
            MpLog.Log($"Sustainer cross-clock rescue: {sustainer.def} ambient={ambientTicks} stamp={staleStamp} ({rescueCount} rescues since last report)");
            rescueCount = 0;
        }
    }

    [HarmonyPatch(typeof(Sustainer), MethodType.Constructor, typeof(SoundDef), typeof(SoundInfo))]
    static class SustainerCtorRealtimeStamp
    {
        static void Postfix(Sustainer __instance)
        {
            if (Multiplayer.Client == null) return;
            SustainerRealtimeMaintenance.lastMaintainRealTime[__instance] = Time.realtimeSinceStartup;
        }
    }

    [HarmonyPatch(typeof(Sustainer), nameof(Sustainer.Maintain))]
    static class SustainerMaintainRealtimeStamp
    {
        static void Postfix(Sustainer __instance)
        {
            if (Multiplayer.Client == null) return;
            SustainerRealtimeMaintenance.lastMaintainRealTime[__instance] = Time.realtimeSinceStartup;
        }
    }

    [HarmonyPatch(typeof(Sustainer), nameof(Sustainer.End))]
    static class SustainerEndCleanup
    {
        static void Postfix(Sustainer __instance)
            => SustainerRealtimeMaintenance.lastMaintainRealTime.Remove(__instance);
    }

    [HarmonyPatch(typeof(Sustainer), nameof(Sustainer.SustainerUpdate))]
    static class SustainerTolerantEndCheck
    {
        // Runs after SustainerUpdateMapTime's prefix so the re-stamp uses the
        // same clock the vanilla staleness check is about to read
        [HarmonyPriority(Priority.Low)]
        static void Prefix(Sustainer __instance)
        {
            if (Multiplayer.Client == null || __instance.Ended) return;

            var maintenance = __instance.info.Maintenance;
            int staleAfter;
            if (maintenance == MaintenanceType.PerTick)
                staleAfter = 1;
            else if (maintenance == MaintenanceType.PerTickRare)
                staleAfter = 250;
            else
                return;

            var ambientTicks = Find.TickManager.TicksGame;
            if (ambientTicks <= __instance.lastMaintainTick + staleAfter)
                return; // not stale, vanilla keeps it

            if (!SustainerRealtimeMaintenance.lastMaintainRealTime.TryGetValue(__instance, out var maintainedAt) ||
                Time.realtimeSinceStartup - maintainedAt > SustainerRealtimeMaintenance.GraceSeconds)
                return; // truly abandoned, let vanilla end it

            SustainerRealtimeMaintenance.NoteRescue(__instance, ambientTicks, __instance.lastMaintainTick);
            __instance.lastMaintainTick = ambientTicks;
        }
    }
}
