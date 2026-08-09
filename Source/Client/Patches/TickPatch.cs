using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using Multiplayer.Client.AsyncTime;
using Multiplayer.Client.Desyncs;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld.Planet;
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
        public static CommandType? currentExecutingCmdType;
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
                {
                    // Skip maps whose AsyncTimeComp isn't registered yet
                    // (mid-session map generation) - every consumer derefs the
                    // tickable, and a command aimed at such a map already
                    // fails loud through RunCmds' TickableById null path.
                    var comp = maps[i].AsyncTime();
                    if (comp != null)
                        yield return comp;
                }
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
                Multiplayer.Client.Send(new ClientFrameTimePacket(avgFrameTime));
                frameTimeSentAt = Time.time;
            }

            if (Multiplayer.IsReplay && replayTimeSpeed == TimeSpeed.Paused || !doSimulate)
                ticksToRun = 0;

            if (simulating is { targetIsTickUntil: true })
                simulating.target = tickUntil;

            CheckFinishSimulating();

            if (MpVersion.IsDebug)
                SimpleProfiler.Start();

            RunCmds();
            if  (LongEventHandler.eventQueue.Count == 0)
            {
                DoUpdate(out var worked);
                if (worked) workTicks++;
            }

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
            if (WorldRendererUtility.WorldSelected)
                return Multiplayer.AsyncWorldTime;

            if (Find.CurrentMap != null)
                return Find.CurrentMap.AsyncTime();

            return null;
        }

        static void Postfix()
        {
            if (Multiplayer.Client == null) return;

            InstallViewerTimeContext();
            Patches.FactionResidueGuard.HealAfterTicks();

            // AsyncTime() can be null while a join or load is mid-flight
            // (Multiplayer.game lags Client in that window)
            if (Find.CurrentMap?.AsyncTime() is not { } viewerTime) return;
            Shader.SetGlobalFloat(ShaderPropertyIDs.GameSeconds, viewerTime.mapTicks.TicksToSeconds());
        }

// Nothing owns the global clock between ticks, so rendering and the
        // UI read whatever the last tickable left installed. Give the rest of
        // the frame one defined context instead: the full snapshot (tick
        // count, speed, slower, gameStartAbsTick) of what the player is
        // actually looking at, so every unwrapped per-frame reader sees a
        // single consistent clock instead of alternating between the viewer's
        // clock inside SetMapTime brackets and the world clock outside them.
        //
        // Between-tick readers that genuinely want world time have explicit
        // world wraps: LetterStackUpdate, AlertsReadout, World.WorldUpdate
        // and - load-bearing for determinism - SaveLoad.SaveGameData, which
        // would otherwise scribe each client's viewer clock into join-point
        // saves.
        //
        // Safe for the sim: this runs after the entire tick and command loop,
        // and every sim path installs its own context on entry (the world
        // tick installs its own count), so this value is never a simulation
        // input.
        internal static void InstallViewerTimeContext()
        {
            // Null checks cover the join/load window where Multiplayer.game
            // (and with it the async comps) lags Multiplayer.Client
            if (Multiplayer.game == null || Find.TickManager == null) return;

            // The previous snapshots are deliberately discarded: this installs
            // the frame's owner, it doesn't bracket a scope
            if (WorldRendererUtility.WorldSelected)
                TimeSnapshot.GetAndSetFromWorld();
            else if (Find.CurrentMap is { } map && map.AsyncTime() != null)
                TimeSnapshot.GetAndSetFromMap(map);
        }

        private static bool RunCmds()
        {
            int curTimer = Timer;

            foreach (ITickable tickable in AllTickables)
            {
                while (tickable.Cmds.Count > 0 && tickable.Cmds.Peek().ticks == curTimer)
                {
                    ScheduledCommand cmd = tickable.Cmds.Dequeue();

                    // Minimal code impact fix for #733. Having all the commands be added to a single queue gets rid of
                    // the out-of-order execution problem. With a proper fix, this can be reverted to tickable.ExecuteCmd
                    var target = TickableById(cmd.mapId);
                    if (target == null)
                    {
                        Log.Error($"!!! Tickable of {cmd.mapId} not found! {cmd}");
                    } else target.ExecuteCmd(cmd);

                    if (LongEventHandler.eventQueue.Count > 0) return true; // Yield to e.g. join-point creation
                }
            }

            return false;
        }

        public static void DoUpdate(out bool worked)
        {
            worked = false;
            updateTimer.Restart();

            while (Simulating ? (Timer < simulating.target && updateTimer.ElapsedMilliseconds < 25) : (ticksToRun > 0))
            {
                if (RunCmds())
                    return;
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
        public static bool DoTick(ref bool worked)
        {
            tickTimer.Restart();

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
                    SimulationFailures.Handle($"Exception during ticking {tickable}", e);
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
            {
                // A map can sit in Find.Maps before its AsyncTimeComp is
                // registered (mid-session map generation). A comp-less map
                // isn't a running map, so it can't bound the rate.
                var comp = map.AsyncTime();
                if (comp != null)
                    rate = Math.Min(rate, comp.TickRateMultiplier(speed));
            }

            return rate;
        }

        public static void ClearSimulating() => simulating = null;

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

        public static void SetTimer(int value) => Timer = value;

        public static ITickable TickableById(int tickableId) => AllTickables.FirstOrDefault(t => t.TickableId == tickableId);
    }

    // Root_Play.Update runs RealTime.Update, PortraitsCache and UIRootUpdate
    // BEFORE TickManagerUpdate, where the frame's viewer context is normally
    // installed - so those consumers read the PREVIOUS frame's residual
    // ambient (e.g. unpausedTime advances by a stale TickRateMultiplier,
    // making pausable-animated materials move in bursts). Install the viewer
    // context at the top of the frame too; the sim still installs its own
    // contexts on entry, so this is render/UI-only like the post-tick install.
    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Update))]
    static class FrameStartViewerContext
    {
        static void Prefix()
        {
            if (Multiplayer.Client == null) return;
            TickPatch.InstallViewerTimeContext();
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
