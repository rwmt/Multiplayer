extern alias zip;

using Harmony;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        public static bool disableSkipCancel;
        public static bool skipToTickUntil;
        public static int skipTo = -1;
        public static Action afterSkip;

        public static IEnumerable<ITickable> AllTickables
        {
            get
            {
                MultiplayerWorldComp comp = Multiplayer.WorldComp;
                yield return comp;
                yield return comp.ticker;

                var maps = Find.Maps;
                for (int i = maps.Count - 1; i >= 0; i--)
                    yield return maps[i].AsyncTime();
            }
        }

        static bool Prefix()
        {
            if (Multiplayer.Client == null) return true;
            if (LongEventHandler.currentEvent != null) return false;
            if (Multiplayer.session.desynced) return false;

            double delta = Time.deltaTime * 60.0;
            if (delta > 3)
                delta = 3;

            accumulator += delta;

            if (Timer >= tickUntil)
                accumulator = 0;
            else if (!Multiplayer.IsReplay && delta < 1.5 && tickUntil - Timer > 6)
                accumulator += Math.Min(100, tickUntil - Timer - 6);

            if (Multiplayer.IsReplay && replayTimeSpeed == TimeSpeed.Paused)
                accumulator = 0;

            if (skipToTickUntil)
                skipTo = tickUntil;

            if (skipTo >= 0 && Timer >= skipTo)
            {
                afterSkip?.Invoke();
                ClearSkipping();
            }

            SimpleProfiler.Start();
            Tick();
            SimpleProfiler.Pause();

            return false;
        }

        public static void ClearSkipping()
        {
            skipTo = -1;
            skipToTickUntil = false;
            accumulator = 0;
            afterSkip = null;
            disableSkipCancel = false;
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
            var watch = Stopwatch.StartNew();

            while ((skipTo < 0 && accumulator > 0) || (skipTo >= 0 && Timer < skipTo && watch.ElapsedMilliseconds < 25))
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

                accumulator -= 1 * ReplayMultiplier();
                Timer += 1;

                if (Timer >= tickUntil || LongEventHandler.eventQueue.Count > 0)
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
            if (!Multiplayer.IsReplay || skipTo >= 0) return 1f;

            if (replayTimeSpeed == TimeSpeed.Paused)
                return 0f;

            ITickable tickable = CurrentTickable();
            if (tickable.TimePerTick(tickable.TimeSpeed) == 0f)
                return 1 / 100f; // So paused sections of the timeline are skipped through

            return tickable.TimePerTick(replayTimeSpeed) / tickable.TimePerTick(tickable.TimeSpeed);
        }
    }

    public interface ITickable
    {
        float RealTimeToTickThrough { get; set; }

        TimeSpeed TimeSpeed { get; }

        Queue<ScheduledCommand> Cmds { get; }

        float TimePerTick(TimeSpeed speed);

        void Tick();

        void ExecuteCmd(ScheduledCommand cmd);
    }

    public class ConstantTicker : ITickable
    {
        public static bool ticking;

        public float RealTimeToTickThrough { get; set; }
        public TimeSpeed TimeSpeed => TimeSpeed.Normal;
        public Queue<ScheduledCommand> Cmds => cmds;
        public Queue<ScheduledCommand> cmds = new Queue<ScheduledCommand>();

        public void ExecuteCmd(ScheduledCommand cmd)
        {
        }

        public float TimePerTick(TimeSpeed speed) => 1f;

        public void Tick()
        {
            ticking = true;

            try
            {
                //TickResearch();

                // Not really deterministic but here for possible future server-side game state verification
                Extensions.PushFaction(null, Multiplayer.RealPlayerFaction);
                TickSync();
                //SyncResearch.ConstantTick();
                Extensions.PopFaction();

                var sync = Multiplayer.game.sync;
                if (sync.ShouldCollect && TickPatch.Timer % 30 == 0 && sync.current != null)
                {
                    if (Multiplayer.LocalServer != null || Multiplayer.arbiterInstance)
                        Multiplayer.Client.Send(Packets.Client_SyncInfo, sync.current.Serialize());

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

                    if (TickPatch.Timer - data.timestamp > 30)
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

        public void TickResearch()
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
