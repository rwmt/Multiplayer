extern alias zip;

using Harmony;
using Multiplayer.Common;
using RimWorld;
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
    [HotSwappable]
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
        public static double lastUpdateTook;

        static bool Prefix()
        {
            if (Multiplayer.Client == null) return true;
            if (LongEventHandler.currentEvent != null) return false;
            if (Multiplayer.session.desynced) return false;

            double delta = Time.deltaTime * 60.0;
            int maxDelta = MultiplayerMod.settings.aggressiveTicking ? 6 : 3;

            if (delta > maxDelta)
                delta = maxDelta;

            accumulator += delta;

            float okDelta = MultiplayerMod.settings.aggressiveTicking ? 2.5f : 1.7f;

            if (Timer >= tickUntil)
                accumulator = 0;
            else if (!Multiplayer.IsReplay && delta < okDelta && tickUntil - Timer > 6)
                accumulator += Math.Min(60, tickUntil - Timer - 6);

            if (Multiplayer.IsReplay && replayTimeSpeed == TimeSpeed.Paused)
                accumulator = 0;

            if (skipToTickUntil)
                skipTo = tickUntil;

            CheckFinishSkipping();

            SimpleProfiler.Start();
            updateTimer.Restart();
            Tick();
            lastUpdateTook = updateTimer.ElapsedMillisDouble();
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
            if (Multiplayer.WorldComp.asyncTime)
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

    public static class ConstantTicker
    {
        public static bool ticking;

        public static void Tick()
        {
            ticking = true;

            try
            {
                //TickResearch();

                // Not really deterministic but here for possible future server-side game state verification
                //Extensions.PushFaction(null, Multiplayer.RealPlayerFaction);
                //TickSync();
                //SyncResearch.ConstantTick();
                //Extensions.PopFaction();

                var sync = Multiplayer.game.sync;
                if (sync.ShouldCollect && TickPatch.Timer % 30 == 0 && sync.current != null)
                {
                    if (!TickPatch.Skipping && (Multiplayer.LocalServer != null || Multiplayer.arbiterInstance))
                        Multiplayer.Client.SendFragmented(Packets.Client_SyncInfo, sync.current.Serialize());

                    sync.Add(sync.current);
                    sync.current = null;
                }
            }
            finally
            {
                ticking = false;
            }
        }

        private static void TickSync()
        {
            foreach (SyncField f in Sync.bufferedFields)
            {
                if (!f.inGameLoop) continue;

                Sync.bufferedChanges[f].RemoveAll((k, data) =>
                {
                    if (OnMainThread.CheckShouldRemove(f, k, data))
                        return true;

                    if (!data.sent && TickPatch.Timer - data.timestamp > 30)
                    {
                        f.DoSync(k.first, data.toSend, k.second);
                        data.sent = true;
                        data.timestamp = TickPatch.Timer;
                    }

                    return false;
                });
            }
        }

        private static Pawn dummyPawn = new Pawn()
        {
            relations = new Pawn_RelationsTracker(dummyPawn),
        };

        public static void TickResearch()
        {
            MultiplayerWorldComp comp = Multiplayer.WorldComp;
            foreach (FactionWorldData factionData in comp.factionData.Values)
            {
                if (factionData.researchManager.currentProj == null)
                    continue;

                Extensions.PushFaction(null, factionData.factionId);

                foreach (var kv in factionData.researchSpeed.data)
                {
                    Pawn pawn = PawnsFinder.AllMaps_Spawned.FirstOrDefault(p => p.thingIDNumber == kv.Key);
                    if (pawn == null)
                    {
                        dummyPawn.factionInt = Faction.OfPlayer;
                        pawn = dummyPawn;
                    }

                    Find.ResearchManager.ResearchPerformed(kv.Value, pawn);

                    dummyPawn.factionInt = null;
                }

                Extensions.PopFaction();
            }
        }
    }

    [HarmonyPatch(typeof(Prefs))]
    [HarmonyPatch(nameof(Prefs.PauseOnLoad), MethodType.Getter)]
    public static class CancelSingleTick
    {
        // Cancel ticking after loading as its handled seperately
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client != null)
                __result = false;
        }
    }

}
