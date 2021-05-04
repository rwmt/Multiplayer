extern alias zip;

using HarmonyLib;
using Multiplayer.Common;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickManagerUpdate))]
    public static class TickPatch
    {
        public static double accumulator;
        public static int Timer;
        public static int tickUntil;
        public static bool currentExecutingCmdIssuedBySelf;

        public static TimeSpeed replayTimeSpeed;

        static bool skipToTickUntil;
        public static int skipTo = -1;
        static Action afterSkip;
        public static bool canESCSkip;
        public static Action cancelSkip;
        public static string skipCancelButtonKey;
        public static string skippingTextKey;

        public static bool Skipping => skipTo >= 0;

        public static IEnumerable<ITickable> AllTickables
        {
            get
            {
                MultiplayerWorldComp comp = Multiplayer.WorldComp;
                yield return comp;

                var maps = Find.Maps;
                for (int i = maps.Count - 1; i >= 0; i--)
                    yield return maps[i].AsyncTime();
            }
        }

        static Stopwatch updateTimer = Stopwatch.StartNew();
        static Stopwatch time = Stopwatch.StartNew();

        public static double lastUpdateTook;

        [TweakValue("Multiplayer", 0f, 100f)]
        public static float maxBehind = 6f;

        static bool Prefix()
        {
            if (Multiplayer.Client == null) return true;
            if (LongEventHandler.currentEvent != null) return false;
            if (Multiplayer.session.desynced) return false;

            double delta = time.ElapsedMillisDouble() / 1000.0 * 60.0;
            time.Restart();

            int maxDelta = MultiplayerMod.settings.aggressiveTicking ? 6 : 3;

            if (delta > maxDelta)
                delta = maxDelta;

            accumulator += delta;

            float okDelta = MultiplayerMod.settings.aggressiveTicking ? 2.5f : 1.7f;

            if (Timer >= tickUntil)
                accumulator = 0;
            else if (!Multiplayer.IsReplay && delta < okDelta && tickUntil - Timer > maxBehind)
                accumulator += Math.Min(60, tickUntil - Timer - maxBehind);

            if (Multiplayer.IsReplay && replayTimeSpeed == TimeSpeed.Paused)
                accumulator = 0;

            if (skipToTickUntil)
                skipTo = tickUntil;

            CheckFinishSkipping();
            if(MpVersion.IsDebug)
                SimpleProfiler.Start();

            updateTimer.Restart();
            Tick();
            lastUpdateTook = updateTimer.ElapsedMillisDouble();

            if(MpVersion.IsDebug)
                SimpleProfiler.Pause();

            CheckFinishSkipping();

            return false;
        }

        private static void CheckFinishSkipping()
        {
            if (skipTo >= 0 && Timer >= skipTo)
            {
                afterSkip?.Invoke();
                ClearSkipping();
            }
        }

        public static void SkipTo(int ticks = 0, bool toTickUntil = false, Action onFinish = null, Action onCancel = null, string cancelButtonKey = null, bool canESC = false, string simTextKey = null)
        {
            skipTo = ticks;
            skipToTickUntil = toTickUntil;
            afterSkip = onFinish;
            cancelSkip = onCancel;
            canESCSkip = canESC;
            skipCancelButtonKey = cancelButtonKey ?? "CancelButton";
            skippingTextKey = simTextKey ?? "MpSimulating";
        }

        public static void ClearSkipping()
        {
            skipTo = -1;
            skipToTickUntil = false;
            accumulator = 0;
            afterSkip = null;
            cancelSkip = null;
            canESCSkip = false;
            skipCancelButtonKey = null;
            skippingTextKey = null;
        }

        static ITickable CurrentTickable()
        {
            if (WorldRendererUtility.WorldRenderedNow)
                return Multiplayer.WorldComp;
            else if (Find.CurrentMap != null)
                return Find.CurrentMap.AsyncTime();

            return null;
        }

        static void Postfix()
        {
            if (Multiplayer.Client == null || Find.CurrentMap == null) return;
            Shader.SetGlobalFloat(ShaderPropertyIDs.GameSeconds, Find.CurrentMap.AsyncTime().mapTicks.TicksToSeconds());
        }

        public static void Tick()
        {
            while ((!Skipping && accumulator > 0) || (Skipping && Timer < skipTo && updateTimer.ElapsedMilliseconds < 25))
            {
                int curTimer = Timer;

                foreach (ITickable tickable in AllTickables)
                {
                    while (tickable.Cmds.Count > 0 && tickable.Cmds.Peek().ticks == curTimer)
                    {
                        ScheduledCommand cmd = tickable.Cmds.Dequeue();
                        tickable.ExecuteCmd(cmd);
                    }
                }

                foreach (ITickable tickable in AllTickables)
                {
                    if (tickable.TimePerTick(tickable.TimeSpeed) == 0) continue;
                    tickable.RealTimeToTickThrough += 1f;

                    TickTickable(tickable);
                }

                ConstantTicker.Tick();

                accumulator -= 1 * ReplayMultiplier();
                Timer += 1;

                if (Multiplayer.session.desynced || Timer >= tickUntil || LongEventHandler.eventQueue.Count > 0)
                {
                    accumulator = 0;
                    return;
                }
            }
        }

        private static void TickTickable(ITickable tickable)
        {
            while (tickable.RealTimeToTickThrough >= 0)
            {
                float timePerTick = tickable.TimePerTick(tickable.TimeSpeed);
                if (timePerTick == 0) break;

                tickable.RealTimeToTickThrough -= timePerTick;

                try
                {
                    tickable.Tick();
                }
                catch (Exception e)
                {
                    Log.Error($"Exception during ticking {tickable}: {e}");
                }
            }
        }

        private static float ReplayMultiplier()
        {
            if (!Multiplayer.IsReplay || Skipping) return 1f;

            if (replayTimeSpeed == TimeSpeed.Paused)
                return 0f;

            ITickable tickable = CurrentTickable();
            if (tickable.TimePerTick(tickable.TimeSpeed) == 0f)
                return 1 / 100f; // So paused sections of the timeline are skipped through

            return tickable.ActualRateMultiplier(tickable.TimeSpeed) / tickable.ActualRateMultiplier(replayTimeSpeed);
        }

        public static float TimePerTick(this ITickable tickable, TimeSpeed speed)
        {
            if (tickable.ActualRateMultiplier(speed) == 0f)
                return 0f;
            return 1f / tickable.ActualRateMultiplier(speed);
        }

        public static float ActualRateMultiplier(this ITickable tickable, TimeSpeed speed)
        {
            if (MultiplayerWorldComp.asyncTime)
                return tickable.TickRateMultiplier(speed);

            return Find.Maps.Select(m => (ITickable)m.AsyncTime()).Concat(Multiplayer.WorldComp).Select(t => t.TickRateMultiplier(speed)).Min();
        }
    }

    public interface ITickable
    {
        float RealTimeToTickThrough { get; set; }

        TimeSpeed TimeSpeed { get; }

        Queue<ScheduledCommand> Cmds { get; }

        float TickRateMultiplier(TimeSpeed speed);

        void Tick();

        void ExecuteCmd(ScheduledCommand cmd);
    }
}
