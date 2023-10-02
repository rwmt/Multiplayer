using HarmonyLib;
using Multiplayer.Common;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Multiplayer.Client.AsyncTime;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickManagerUpdate))]
    public static class TickPatch
    {
        public static int Timer { get; private set; }

        public static int ticksToRun;
        public static int tickUntil; // Ticks < tickUntil can be simulated
        public static int workTicks;
        public static bool currentExecutingCmdIssuedBySelf;
        public static bool serverFrozen;
        public static int frozenAt;

        public const float StandardTimePerFrame = 1000.0f / 60.0f;

        // Time is in milliseconds
        private static float realTime;
        public static float avgFrameTime = StandardTimePerFrame;
        public static float serverTimePerTick;

        private static float frameTimeSentAt;

        public static TimeSpeed replayTimeSpeed;

        public static SimulatingData simulating;

        public static bool ShouldHandle => LongEventHandler.currentEvent == null && !Multiplayer.session.desynced;
        public static bool Simulating => simulating?.target != null;
        public static bool Frozen => serverFrozen && Timer >= frozenAt && !Simulating && ShouldHandle;

        public static IEnumerable<ITickable> AllTickables
        {
            get
            {
                yield return Multiplayer.AsyncWorldTime;

                var maps = Find.Maps;
                for (int i = maps.Count - 1; i >= 0; i--)
                    yield return maps[i].AsyncTime();
            }
        }

        static Stopwatch updateTimer = Stopwatch.StartNew();
        public static Stopwatch tickTimer = Stopwatch.StartNew();

        [TweakValue("Multiplayer")]
        public static bool doSimulate = true;

        static bool Prefix()
        {
            if (Multiplayer.Client == null) return true;
            if (!ShouldHandle) return false;
            if (Frozen) return false;

            int ticksBehind = tickUntil - Timer;
            realTime += Time.deltaTime * 1000f;

            // Slow down when few ticksBehind to accumulate a buffer
            // Try to speed up when many ticksBehind
            // Else run at the speed from the server
            float stpt = ticksBehind <= 3 ? serverTimePerTick * 1.2f : ticksBehind >= 7 ? serverTimePerTick * 0.8f : serverTimePerTick;

            if (Multiplayer.IsReplay)
                stpt = StandardTimePerFrame * ReplayMultiplier();

            if (Timer >= tickUntil)
            {
                ticksToRun = 0;
            }
            else if (realTime > 0 && stpt > 0)
            {
                avgFrameTime = (avgFrameTime + Time.deltaTime * 1000f) / 2f;

                ticksToRun = Multiplayer.IsReplay ? Mathf.CeilToInt(realTime / stpt) : 1;
                realTime -= ticksToRun * stpt;
            }

            if (realTime > 0)
                realTime = 0;

            if (Time.time - frameTimeSentAt > 32f/1000f)
            {
                Multiplayer.Client.Send(Packets.Client_FrameTime, avgFrameTime);
                frameTimeSentAt = Time.time;
            }

            if (Multiplayer.IsReplay && replayTimeSpeed == TimeSpeed.Paused || !doSimulate)
                ticksToRun = 0;

            if (simulating is { targetIsTickUntil: true })
                simulating.target = tickUntil;

            CheckFinishSimulating();

            if (MpVersion.IsDebug)
                SimpleProfiler.Start();

            DoUpdate(out var worked);
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

        public static void SetSimulation(int ticks = 0, bool toTickUntil = false, Action onFinish = null, Action onCancel = null, string cancelButtonKey = null, bool canEsc = false, string simTextKey = null)
        {
            simulating = new SimulatingData
            {
                target = ticks,
                targetIsTickUntil = toTickUntil,
                onFinish = onFinish,
                onCancel = onCancel,
                canEsc = canEsc,
                cancelButtonKey = cancelButtonKey ?? "CancelButton",
                simTextKey = simTextKey ?? "MpSimulating"
            };
        }

        static ITickable CurrentTickable()
        {
            if (WorldRendererUtility.WorldRenderedNow)
                return Multiplayer.AsyncWorldTime;

            if (Find.CurrentMap != null)
                return Find.CurrentMap.AsyncTime();

            return null;
        }

        static void Postfix()
        {
            if (Multiplayer.Client == null || Find.CurrentMap == null) return;
            Shader.SetGlobalFloat(ShaderPropertyIDs.GameSeconds, Find.CurrentMap.AsyncTime().mapTicks.TicksToSeconds());
        }

        public static void DoUpdate(out bool worked)
        {
            worked = false;
            updateTimer.Restart();

            while (Simulating ? (Timer < simulating.target && updateTimer.ElapsedMilliseconds < 25) : (ticksToRun > 0))
            {
                if (DoTick(ref worked))
                    return;
            }
        }

        public static void DoTicks(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                bool worked = false;
                DoTick(ref worked);
            }
        }

        // Returns whether the tick loop should stop
        public static bool DoTick(ref bool worked, bool justCmds = false)
        {
            tickTimer.Restart();
            int curTimer = Timer;

            foreach (ITickable tickable in AllTickables)
            {
                while (tickable.Cmds.Count > 0 && tickable.Cmds.Peek().ticks == curTimer)
                {
                    ScheduledCommand cmd = tickable.Cmds.Dequeue();
                    tickable.ExecuteCmd(cmd);

                    if (LongEventHandler.eventQueue.Count > 0) return true; // Yield to e.g. join-point creation
                }
            }

            if (justCmds)
                return true;

            foreach (ITickable tickable in AllTickables)
            {
                if (tickable.TimePerTick(tickable.DesiredTimeSpeed) == 0) continue;
                tickable.TimeToTickThrough += 1f;

                worked = true;
                TickTickable(tickable);
            }

            ConstantTicker.Tick();

            ticksToRun -= 1;
            Timer += 1;

            tickTimer.Stop();

            if (Multiplayer.session.desynced || Timer >= tickUntil || LongEventHandler.eventQueue.Count > 0)
            {
                ticksToRun = 0;
                return true;
            }

            return false;
        }

        private static void TickTickable(ITickable tickable)
        {
            while (tickable.TimeToTickThrough >= 0)
            {
                float timePerTick = tickable.TimePerTick(tickable.DesiredTimeSpeed);
                if (timePerTick == 0) break;

                tickable.TimeToTickThrough -= timePerTick;

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
            if (tickable.TimePerTick(tickable.DesiredTimeSpeed) == 0f)
                return 1 / 100f; // So paused sections of the timeline are skipped through

            return tickable.ActualRateMultiplier(tickable.DesiredTimeSpeed) / tickable.ActualRateMultiplier(replayTimeSpeed);
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

            var rate = Multiplayer.AsyncWorldTime.TickRateMultiplier(speed);
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
            ticksToRun = 0;
            serverFrozen = false;
            workTicks = 0;
            serverTimePerTick = 0;
            avgFrameTime = StandardTimePerFrame;
            realTime = 0;
            TimeControlPatch.prePauseTimeSpeed = null;
        }

        public static void SetTimer(int value)
        {
            Timer = value;
        }

        public static ITickable TickableById(int tickableId)
        {
            return AllTickables.FirstOrDefault(t => t.TickableId == tickableId);
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
