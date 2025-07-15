using Multiplayer.Client.AsyncTime;
using System;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.DebugUi
{
    /// <summary>
    /// Calculates performance based on current TPS and target TPS. Handles speed changes and stabilization periods.
    /// </summary>
    public static class PerformanceCalculator
    {
        private static TimeSpeed lastTimeSpeed = TimeSpeed.Normal;
        private static float lastSpeedChangeTime = 0f;
        private const float SpeedStabilizationTime = 2.5f;

        // AsyncTime caching for performance
        private static Map lastCachedMap = null;
        private static AsyncTimeComp cachedAsyncTimeComp = null;
        private static float lastCacheTime = 0f;
        private const float CacheValidTime = 0.1f; // 100ms

        private static AsyncTimeComp GetCachedAsyncTimeComp()
        {
            var currentMap = Find.CurrentMap;
            var currentTime = Time.realtimeSinceStartup;

            if (currentMap == lastCachedMap &&
                cachedAsyncTimeComp != null &&
                currentTime - lastCacheTime < CacheValidTime)
            {
                return cachedAsyncTimeComp;
            }

            lastCachedMap = currentMap;
            lastCacheTime = currentTime;
            cachedAsyncTimeComp = currentMap?.AsyncTime();

            return cachedAsyncTimeComp;
        }

        /// <summary>
        /// Get the effective TPS target considering all speed-limiting mechanisms
        /// </summary>
        public static float GetTargetTPS()
        {
            if (TickPatch.Frozen) return 0f;

            if (Multiplayer.GameComp?.asyncTime == true)
            {
                // Try map async time first
                if (GetCachedAsyncTimeComp() is AsyncTimeComp asyncTimeComp)
                {
                    var desiredSpeed = asyncTimeComp.DesiredTimeSpeed;
                    float rateMultiplier = asyncTimeComp.TickRateMultiplier(desiredSpeed);

                    var currentTicks = asyncTimeComp.mapTicks;
                    var forceNormalUntil = Find.TickManager?.slower?.forceNormalSpeedUntil ?? 0;

                    if (currentTicks < forceNormalUntil) return 60f;
                    return CalculateTPS(rateMultiplier, desiredSpeed);
                }

                // Fallback to world async time
                if (Multiplayer.AsyncWorldTime is AsyncWorldTimeComp comp)
                {
                    var worldDesiredSpeed = comp.DesiredTimeSpeed;
                    float worldRateMultiplier = comp.TickRateMultiplier(worldDesiredSpeed);
                    return CalculateTPS(worldRateMultiplier, worldDesiredSpeed);
                }
            }
            else
            {
                // Non-async mode, the actual rate is the minimum across all maps
                if (GetCachedAsyncTimeComp() is AsyncTimeComp currentTickable)
                {
                    var desiredSpeed = currentTickable.DesiredTimeSpeed;
                    float actualRateMultiplier = currentTickable.ActualRateMultiplier(desiredSpeed);
                    return CalculateTPS(actualRateMultiplier, desiredSpeed);
                }
            }

            // Fallback to standard TickManager calculation
            if (IsForcedNormal() || Find.TickManager is not TickManager tickManager)
            {
                return 60f;
            }

            if (tickManager.Paused || tickManager.ForcePaused) return 0f;

            return tickManager.CurTimeSpeed switch
            {
                TimeSpeed.Paused => 0f,
                TimeSpeed.Normal => 60f,
                TimeSpeed.Fast => 180f,
                TimeSpeed.Superfast => 360f,
                TimeSpeed.Ultrafast => 900f,
                _ => 60f
            };
        }

        /// <summary>
        /// Check if we're in a stabilization period after a speed change
        /// </summary>
        public static bool IsInStabilizationPeriod()
        {
            if (Find.TickManager is not TickManager tickManager) return false;

            TimeSpeed currentSpeed;
            if (Multiplayer.GameComp?.asyncTime == true)
            {
                if (GetCachedAsyncTimeComp() is AsyncTimeComp asyncTimeComp)
                {
                    currentSpeed = asyncTimeComp.DesiredTimeSpeed;
                }
                else
                {
                    currentSpeed = Multiplayer.AsyncWorldTime?.DesiredTimeSpeed ??
                                   (tickManager.Paused ? TimeSpeed.Paused : tickManager.CurTimeSpeed);
                }
            }
            else
            {
                currentSpeed = tickManager.Paused ? TimeSpeed.Paused : tickManager.CurTimeSpeed;
            }

            if (currentSpeed != lastTimeSpeed)
            {
                lastTimeSpeed = currentSpeed;
                lastSpeedChangeTime = Time.realtimeSinceStartup;

                // Invalidate AsyncTime cache to pick up changes quickly
                cachedAsyncTimeComp = null;
                lastCachedMap = null;

                return true;
            }

            return Time.realtimeSinceStartup - lastSpeedChangeTime < SpeedStabilizationTime;
        }

        /// <summary>
        /// Get normalized TPS performance as percentage (0-100%)
        /// </summary>
        public static float GetNormalizedTPS(float currentTps)
        {
            float targetTps = GetTargetTPS();

            if (targetTps == 0f) return 100f;

            float percentage = (currentTps / targetTps) * 100f;
            return Math.Min(percentage, 100f);
        }

        /// <summary>
        /// Get normalized TPS performance or null if in stabilization period
        /// </summary>
        public static float? GetStableNormalizedTPS(float currentTps)
        {
            if (IsInStabilizationPeriod())
                return null;

            return GetNormalizedTPS(currentTps);
        }

        /// <summary>
        /// Unified color logic for performance metrics
        /// </summary>
        public static Color GetPerformanceColor(float value, float goodThreshold, float moderateThreshold, bool lowerIsBetter = false)
        {
            return lowerIsBetter switch
            {
                false when value >= goodThreshold => Color.green,
                false when value >= moderateThreshold => Color.yellow,
                true when value <= goodThreshold => Color.green,
                true when value <= moderateThreshold => Color.yellow,
                _ => Color.red
            };
        }

        private static bool IsForcedNormal() => Find.TickManager?.slower?.ForcedNormalSpeed == true;

        private static float CalculateTPS(float rateMultiplier, TimeSpeed speed)
        {
            if (speed == TimeSpeed.Paused || rateMultiplier == 0f) return 0f;
            if (IsForcedNormal()) return 60f;
            return rateMultiplier * 60f;
        }

    }
}
