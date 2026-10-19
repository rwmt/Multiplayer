using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LudeonTK;
using Multiplayer.Client.AsyncTime;
using Multiplayer.Client.Patches;
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
        // Changes at the start of every command, tick of a tickable and sim long event. Interface code (and so a frame) can't run
        // within one step, so caches keyed by it behave the same on every client, unlike ones keyed by frame.
        public static int simStep;
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

                // A map being generated is in Find.Maps before MapSetup gives it its comps
                var maps = Find.Maps;
                for (int i = maps.Count - 1; i >= 0; i--)
                    if (maps[i].AsyncTime() is { } comp)
                        yield return comp;
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

            RunCmdsBeforeTicks();
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
            if (Multiplayer.Client == null || Find.CurrentMap == null) return;
            Shader.SetGlobalFloat(ShaderPropertyIDs.GameSeconds, Find.CurrentMap.AsyncTime().mapTicks.TicksToSeconds());
        }

        // Commands run either here at the start of the frame or in the DoUpdate loop. The desync trace hash includes
        // the stack depth, so both paths have to be equally deep: Prefix -> RunCmdsBeforeTicks -> RunCmds like
        // Prefix -> DoUpdate -> RunCmds (#994).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunCmdsBeforeTicks() => RunCmds();

        private static bool RunCmds()
        {
            int curTimer = Timer;

            // All commands (world and map) are in the world's queue, in the order the server sent them (#733)
            var cmds = Multiplayer.AsyncWorldTime.Cmds;

            while (cmds.Count > 0 && cmds.Peek().ticks <= curTimer)
            {
                ScheduledCommand cmd = cmds.Dequeue();

                if (cmd.ticks < curTimer)
                {
                    // Can only happen with a broken log (e.g. a world state newer than its commands). The loaded state
                    // already contains the command's effects and executing it late can't match other clients, so
                    // drop it instead of blocking the queue forever.
                    Log.Error($"Multiplayer: dropping command scheduled for a past tick (timer {curTimer}): {cmd}");
                    continue;
                }

                var target = TickableById(cmd.mapId);
                if (target == null)
                {
                    Log.Error($"!!! Tickable of {cmd.mapId} not found! {cmd}");
                }
                else
                {
                    GravEngineCache.Reset();
                    simStep++;
                    target.ExecuteCmd(cmd);
                }

                if (LongEventHandler.eventQueue.Count > 0) return true; // Yield to e.g. join-point creation
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

            if (Multiplayer.GameComp.asyncTime)
            {
                foreach (ITickable tickable in AllTickables)
                {
                    if (tickable.TimePerTick(tickable.DesiredTimeSpeed) == 0) continue;
                    tickable.TimeToTickThrough += 1f;

                    worked = true;
                    TickTickable(tickable);
                }
            }
            else
            {
                TickLockstep(ref worked);
            }

            GravEngineCache.Reset();
            simStep++;
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

        // Without async time the world and the maps share one clock, but each tickable used to tick through its own
        // accumulator, and they drifted apart (#339): a new map started with a fresh accumulator and got an extra tick,
        // a rate change during a map tick (a pausing letter, TimeSlower, a pausing session) reached only the tickables
        // after it, and removing a map during a map tick shifted the lazily read map list so another map ticked twice.
        // Here every game tick ticks the world and then each map once, the rate (the world's, which runs at the fastest
        // map's speed) is checked only between game ticks like in vanilla's tick loop, and only the world's accumulator
        // is used. Maps generated during a game tick start at worldTicks (MapSetup): one generated by the world tick
        // still ticks in this game tick, one generated by a map tick from the next one.
        private static void TickLockstep(ref bool worked)
        {
            var world = Multiplayer.AsyncWorldTime;
            if (world.TimePerTick(world.DesiredTimeSpeed) == 0) return;
            world.TimeToTickThrough += 1f;

            worked = true;
            List<AsyncTimeComp> maps = null;

            while (world.TimeToTickThrough >= 0)
            {
                float timePerTick = world.TimePerTick(world.DesiredTimeSpeed);
                if (timePerTick == 0) break;

                world.TimeToTickThrough -= timePerTick;
                TickOnce(world);

                // Same order as AllTickables
                maps ??= new List<AsyncTimeComp>();
                maps.Clear();
                var gameMaps = Find.Maps;
                for (int i = gameMaps.Count - 1; i >= 0; i--)
                    if (gameMaps[i].AsyncTime() is { } comp)
                        maps.Add(comp);

                foreach (var comp in maps)
                    if (!comp.map.Disposed && Find.Maps.Contains(comp.map)) // Removed during this game tick
                        TickOnce(comp);
            }
        }

        private static void TickOnce(ITickable tickable)
        {
            simStep++;

            try
            {
                GravEngineCache.Reset();
                tickable.Tick();
            }
            catch (Exception e)
            {
                Log.Error($"Exception during ticking {tickable}: {e}");
                SimulationFailures.Record($"ticking {tickable}", e);
            }
        }

        private static void TickTickable(ITickable tickable)
        {
            while (tickable.TimeToTickThrough >= 0)
            {
                float timePerTick = tickable.TimePerTick(tickable.DesiredTimeSpeed);
                if (timePerTick == 0) break;

                tickable.TimeToTickThrough -= timePerTick;
                simStep++;

                try
                {
                    GravEngineCache.Reset();
                    tickable.Tick();
                }
                catch (Exception e)
                {
                    Log.Error($"Exception during ticking {tickable}: {e}");
                    SimulationFailures.Record($"ticking {tickable}", e);
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
                if (map.AsyncTime() is { } comp)
                    rate = Math.Min(rate, comp.TickRateMultiplier(speed));

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
