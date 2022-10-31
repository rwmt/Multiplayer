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
        public static int Timer { get; private set; }

        public static double accumulator;
        public static int tickUntil;
        public static int workTicks;
        public static bool currentExecutingCmdIssuedBySelf;
        public static bool shouldPause;
        public static int pausedAt;

        public static TimeSpeed replayTimeSpeed;

        public static SimulatingData simulating;

        public static bool ShouldHandle => LongEventHandler.currentEvent == null && !Multiplayer.session.desynced;
        public static bool Simulating => simulating?.target != null;
        public static bool Paused => shouldPause && Timer >= pausedAt && !Simulating && ShouldHandle;

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
        public static Stopwatch tickTimer = Stopwatch.StartNew();
        static Stopwatch time = Stopwatch.StartNew();

        [TweakValue("Multiplayer", 0f, 100f)]
        public static float maxBehind = 6f;

        static bool Prefix()
        {
            if (Multiplayer.Client == null) return true;
            if (!ShouldHandle) return false;
            if (Paused) return false;

            double delta = time.ElapsedMillisDouble() / 1000.0 * 60.0;
            time.Restart();

            int maxDelta = Multiplayer.settings.aggressiveTicking ? 6 : 3;

            if (delta > maxDelta)
                delta = maxDelta;

            accumulator += delta;

            float okDelta = Multiplayer.settings.aggressiveTicking ? 2.5f : 1.7f;

            if (Timer >= tickUntil)
                accumulator = 0;
            else if (!Multiplayer.IsReplay && delta < okDelta && tickUntil - Timer > maxBehind)
                accumulator += Math.Min(60, tickUntil - Timer - maxBehind);

            if (Multiplayer.IsReplay && replayTimeSpeed == TimeSpeed.Paused)
                accumulator = 0;

            if (simulating is { targetIsTickUntil: true })
                simulating.target = tickUntil;

            CheckFinishSimulating();

            if (MpVersion.IsDebug)
                SimpleProfiler.Start();

            Tick(out var worked);
            if (worked) workTicks++;

            if (MpVersion.IsDebug)
                SimpleProfiler.Pause();

            CheckFinishSimulating();

            return false;
        }

        private static void CheckFinishSimulating()
        {
            if (simulating?.target != null && Timer >= simulating.target)
            {
                simulating.onFinish?.Invoke();
                ClearSimulating();
            }
        }

        public static void SetSimulation(int ticks = 0, bool toTickUntil = false, Action onFinish = null, Action onCancel = null, string cancelButtonKey = null, bool canESC = false, string simTextKey = null)
        {
            simulating = new SimulatingData()
            {
                target = ticks,
                targetIsTickUntil = toTickUntil,
                onFinish = onFinish,
                onCancel = onCancel,
                canEsc = canESC,
                cancelButtonKey = cancelButtonKey ?? "CancelButton",
                simTextKey = simTextKey ?? "MpSimulating"
            };
        }

        static ITickable CurrentTickable()
        {
            if (WorldRendererUtility.WorldRenderedNow)
                return Multiplayer.WorldComp;

            if (Find.CurrentMap != null)
                return Find.CurrentMap.AsyncTime();

            return null;
        }

        static void Postfix()
        {
            if (Multiplayer.Client == null || Find.CurrentMap == null) return;
            Shader.SetGlobalFloat(ShaderPropertyIDs.GameSeconds, Find.CurrentMap.AsyncTime().mapTicks.TicksToSeconds());
        }

        public static void Tick(out bool worked)
        {
            worked = false;
            updateTimer.Restart();

            while ((!Simulating && accumulator > 0) || (Simulating && Timer < simulating.target && updateTimer.ElapsedMilliseconds < 25))
            {
                tickTimer.Restart();
                int curTimer = Timer;

                foreach (ITickable tickable in AllTickables)
                {
                    while (tickable.Cmds.Count > 0 && tickable.Cmds.Peek().ticks == curTimer)
                    {
                        ScheduledCommand cmd = tickable.Cmds.Dequeue();
                        tickable.ExecuteCmd(cmd);

                        if (LongEventHandler.eventQueue.Count > 0) return; // Yield to e.g. join-point creation
                    }
                }

                foreach (ITickable tickable in AllTickables)
                {
                    if (tickable.TimePerTick(tickable.TimeSpeed) == 0) continue;
                    tickable.RealTimeToTickThrough += 1f;

                    worked = true;
                    TickTickable(tickable);
                }

                ConstantTicker.Tick();

                accumulator -= 1 * ReplayMultiplier();
                Timer += 1;

                tickTimer.Stop();

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
            if (!Multiplayer.IsReplay || Simulating) return 1f;

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
            if (Multiplayer.GameComp.asyncTime)
                return tickable.TickRateMultiplier(speed);

            var rate = Multiplayer.WorldComp.TickRateMultiplier(speed);
            foreach (var map in Find.Maps)
                rate = Math.Min(rate, map.AsyncTime().TickRateMultiplier(speed));

            return rate;
        }

        public static void ClearSimulating()
        {
            simulating = null;
        }

        public static void Reset()
        {
            ClearSimulating();
            Timer = 0;
            tickUntil = 0;
            accumulator = 0;
            shouldPause = false;
            workTicks = 0;
        }

        public static void SetTimer(int value)
        {
            Timer = value;
        }
    }

    public class SimulatingData
    {
        public int? target;
        public bool targetIsTickUntil; // When true, the target field is always up-to-date with TickPatch.tickUntil
        public Action onFinish;
        public bool canEsc;
        public Action onCancel;
        public string cancelButtonKey;
        public string simTextKey;
    }
}
