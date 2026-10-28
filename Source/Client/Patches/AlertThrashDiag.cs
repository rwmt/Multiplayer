using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Patches
{
    // Diagnostic only. Field reports describe alert sounds re-firing ("low
    // food keeps pinging") in multiplayer sessions, which requires an alert
    // to leave and re-enter the active list - Notify_Started rings the bell
    // only on re-entry. No mechanism for the flip has been confirmed from
    // the code, so instead of a speculative fix this logs a rate-limited
    // line naming the alert and the state it flipped under, turning the next
    // session's Player.log into the evidence. Log-only, UI-side, no sim
    // state touched.
    [HarmonyPatch(typeof(AlertsReadout), nameof(AlertsReadout.CheckAddOrRemoveAlert))]
    static class AlertThrashDiag
    {
        // Real-time seconds of recent activations, per alert type
        private static readonly Dictionary<string, Queue<float>> activations = new();
        private static readonly Dictionary<string, float> lastReport = new();

        private const int ThrashActivationCount = 3;
        private const float ThrashWindowSeconds = 60f;
        private const float ReportIntervalSeconds = 300f;

        static void Prefix(AlertsReadout __instance, Alert alert, ref bool __state)
            => __state = alert != null && __instance.activeAlerts.Contains(alert);

        static void Postfix(AlertsReadout __instance, Alert alert, bool __state)
        {
            if (Multiplayer.Client == null || alert == null) return;

            try
            {
                // Only newly-(re)activated alerts ring the bell
                if (__state || !__instance.activeAlerts.Contains(alert)) return;

                var key = alert.GetType().Name;
                var now = Time.realtimeSinceStartup;

                if (!activations.TryGetValue(key, out var times))
                    activations[key] = times = new Queue<float>();

                times.Enqueue(now);
                while (times.Count > 0 && times.Peek() < now - ThrashWindowSeconds)
                    times.Dequeue();

                if (times.Count < ThrashActivationCount) return;
                if (lastReport.TryGetValue(key, out var last) && now - last < ReportIntervalSeconds) return;
                lastReport[key] = now;

                MpLog.Log(
                    $"Alert thrash: {key} activated {times.Count}x in {ThrashWindowSeconds}s. " +
                    $"ambientTicks={Find.TickManager.TicksGame}, worldTicks={Multiplayer.AsyncWorldTime?.worldTicks}, " +
                    $"viewedMap={Find.CurrentMap?.uniqueID}, " +
                    $"mapSpeeds=[{MapStates()}]{LowFoodInputs(alert)}");
            }
            catch (Exception e)
            {
                Log.WarningOnce($"Alert thrash diagnostic failed: {e.Message}", 758231);
            }
        }

        private static string MapStates() =>
            Find.Maps.Select(m => m.AsyncTime())
                .Where(c => c != null)
                .Join(c => $"{c.map.uniqueID}:{c.DesiredTimeSpeed}@{c.mapTicks}", ", ");

        private static string LowFoodInputs(Alert alert)
        {
            if (alert is not Alert_LowFood) return "";

            var perMap = Find.Maps
                .Where(m => m.IsPlayerHome && m.mapPawns.AnyColonistSpawned)
                .Join(m => $"{m.uniqueID}:food={m.resourceCounter.TotalHumanEdibleNutrition:F1}/colonists={m.mapPawns.FreeColonistsSpawnedCount}", ", ");

            return $", lowFood=[{perMap}]";
        }
    }
}
