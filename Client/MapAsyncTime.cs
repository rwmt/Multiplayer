using Harmony;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

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

                foreach (Map map in Find.Maps)
                    yield return map.AsyncTime();
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
            else if (!Multiplayer.IsReplay && delta < 1.5 && tickUntil - Timer > 8)
                accumulator += Math.Min(100, tickUntil - Timer - 8);

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

            while (accumulator > 0 || (skipTo >= 0 && Timer < skipTo && watch.ElapsedMilliseconds < 25))
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

                    Tick(tickable);
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

        private static void Tick(ITickable tickable)
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

    [MpPatch(typeof(Map), nameof(Map.MapPreTick))]
    [MpPatch(typeof(Map), nameof(Map.MapPostTick))]
    [MpPatch(typeof(TickList), nameof(TickList.Tick))]
    static class CancelMapManagersTick
    {
        static bool Prefix() => Multiplayer.Client == null || MapAsyncTimeComp.tickingMap != null;
    }

    [HarmonyPatch(typeof(Autosaver), nameof(Autosaver.AutosaverTick))]
    static class DisableAutosaver
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
    static class MapUpdateMarker
    {
        public static bool updating;

        static void Prefix() => updating = true;
        static void Postfix() => updating = false;
    }

    [MpPatch(typeof(PowerNetManager), nameof(PowerNetManager.UpdatePowerNetsAndConnections_First))]
    [MpPatch(typeof(GlowGrid), nameof(GlowGrid.GlowGridUpdate_First))]
    [MpPatch(typeof(RegionGrid), nameof(RegionGrid.UpdateClean))]
    [MpPatch(typeof(RegionAndRoomUpdater), nameof(RegionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms))]
    static class CancelMapManagersUpdate
    {
        static bool Prefix() => Multiplayer.Client == null || !MapUpdateMarker.updating;
    }

    [HarmonyPatch(typeof(DateNotifier), nameof(DateNotifier.DateNotifierTick))]
    static class DateNotifierPatch
    {
        static void Prefix(DateNotifier __instance, ref int? __state)
        {
            if (Multiplayer.Client == null && Multiplayer.RealPlayerFaction != null) return;

            Map map = __instance.FindPlayerHomeWithMinTimezone();
            if (map == null) return;

            __state = Find.TickManager.TicksGame;
            FactionContext.Push(Multiplayer.RealPlayerFaction);
            Find.TickManager.DebugSetTicksGame(map.AsyncTime().mapTicks);
        }

        static void Postfix(int? __state)
        {
            if (!__state.HasValue) return;
            Find.TickManager.DebugSetTicksGame(__state.Value);
            FactionContext.Pop();
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.RegisterAllTickabilityFor))]
    public static class TickListAdd
    {
        static bool Prefix(Thing t)
        {
            if (Multiplayer.Client == null) return true;

            MapAsyncTimeComp comp = t.Map.GetComponent<MapAsyncTimeComp>();
            TickerType tickerType = t.def.tickerType;

            if (tickerType == TickerType.Normal)
                comp.tickListNormal.RegisterThing(t);
            else if (tickerType == TickerType.Rare)
                comp.tickListRare.RegisterThing(t);
            else if (tickerType == TickerType.Long)
                comp.tickListLong.RegisterThing(t);

            return false;
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DeRegisterAllTickabilityFor))]
    public static class TickListRemove
    {
        static bool Prefix(Thing t)
        {
            if (Multiplayer.Client == null) return true;

            MapAsyncTimeComp comp = t.Map.GetComponent<MapAsyncTimeComp>();
            TickerType tickerType = t.def.tickerType;

            if (tickerType == TickerType.Normal)
                comp.tickListNormal.DeregisterThing(t);
            else if (tickerType == TickerType.Rare)
                comp.tickListRare.DeregisterThing(t);
            else if (tickerType == TickerType.Long)
                comp.tickListLong.DeregisterThing(t);

            return false;
        }
    }

    [HarmonyPatch(typeof(TimeControls), nameof(TimeControls.DoTimeControlsGUI))]
    public static class TimeControlPatch
    {
        private static TimeSpeed prevSpeed;
        private static TimeSpeed savedSpeed;

        static void Prefix(ref ITickable __state)
        {
            if (Multiplayer.Client == null) return;
            if (!WorldRendererUtility.WorldRenderedNow && Find.CurrentMap == null) return;

            ITickable tickable = Multiplayer.WorldComp;
            if (!WorldRendererUtility.WorldRenderedNow)
                tickable = Find.CurrentMap.AsyncTime();

            TimeSpeed speed = tickable.TimeSpeed;
            if (Multiplayer.IsReplay)
                speed = TickPatch.replayTimeSpeed;

            savedSpeed = Find.TickManager.CurTimeSpeed;

            Find.TickManager.CurTimeSpeed = speed;
            prevSpeed = speed;
            __state = tickable;
        }

        static void Postfix(ITickable __state)
        {
            if (__state == null) return;

            TimeSpeed newSpeed = Find.TickManager.CurTimeSpeed;
            Find.TickManager.CurTimeSpeed = savedSpeed;

            if (prevSpeed == newSpeed) return;

            if (Multiplayer.IsReplay)
                TickPatch.replayTimeSpeed = newSpeed;

            TimeControl.SendTimeChange(__state, newSpeed);
        }
    }

    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    [StaticConstructorOnStartup]
    public static class ColonistBarTimeControl
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null) return;

            ColonistBar bar = Find.ColonistBar;
            if (bar.Entries.Count == 0 || bar.Entries.Last().group == 0) return;

            int curGroup = -1;
            foreach (ColonistBar.Entry entry in bar.Entries)
            {
                if (entry.map == null || curGroup == entry.group) continue;

                float alpha = 1.0f;
                if (entry.map != Find.CurrentMap || WorldRendererUtility.WorldRenderedNow)
                    alpha = 0.75f;

                Rect rect = bar.drawer.GroupFrameRect(entry.group);

                Rect button = new Rect(rect.x - TimeControls.TimeButSize.x / 2f, rect.yMax - TimeControls.TimeButSize.y / 2f, TimeControls.TimeButSize.x, TimeControls.TimeButSize.y);
                TimeControl.TimeControlButton(button, entry.map.AsyncTime(), alpha);

                curGroup = entry.group;
            }
        }
    }

    [HarmonyPatch(typeof(MainButtonWorker), nameof(MainButtonWorker.DoButton))]
    static class MainButtonWorldTimeControl
    {
        static void Prefix(MainButtonWorker __instance, Rect rect, ref Rect? __state)
        {
            if (Multiplayer.Client == null) return;
            if (__instance.def != MainButtonDefOf.World) return;
            if (__instance.Disabled) return;
            if (Find.CurrentMap == null) return;

            Rect button = new Rect(rect.xMax - TimeControls.TimeButSize.x - 5f, rect.y + (rect.height - TimeControls.TimeButSize.y) / 2f, TimeControls.TimeButSize.x, TimeControls.TimeButSize.y);
            __state = button;

            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)
                TimeControl.TimeControlButton(__state.Value, Multiplayer.WorldComp, 0.5f);
        }

        static void Postfix(MainButtonWorker __instance, Rect? __state)
        {
            if (__state == null) return;

            if (Event.current.type == EventType.Repaint)
                TimeControl.TimeControlButton(__state.Value, Multiplayer.WorldComp, 0.5f);
        }
    }

    static class TimeControl
    {
        public static void TimeControlButton(Rect button, ITickable tickable, float alpha)
        {
            Widgets.DrawRectFast(button, new Color(0.5f, 0.5f, 0.5f, 0.4f * alpha));

            int speed = (int)tickable.TimeSpeed;
            if (Widgets.ButtonImage(button, TexButton.SpeedButtonTextures[speed]))
            {
                int dir = Event.current.button == 0 ? 1 : -1;
                SendTimeChange(tickable, (TimeSpeed)GenMath.PositiveMod(speed + dir, (int)TimeSpeed.Ultrafast));
                Event.current.Use();
            }
        }

        public static void SendTimeChange(ITickable tickable, TimeSpeed newSpeed)
        {
            if (tickable is MultiplayerWorldComp)
                Multiplayer.Client.SendCommand(CommandType.WorldTimeSpeed, ScheduledCommand.Global, (byte)newSpeed);
            else if (tickable is MapAsyncTimeComp comp)
                Multiplayer.Client.SendCommand(CommandType.MapTimeSpeed, comp.map.uniqueID, (byte)newSpeed);
        }
    }

    [HarmonyPatch(typeof(PawnTweener), nameof(PawnTweener.PreDrawPosCalculation))]
    static class PreDrawCalcMarker
    {
        public static bool calculating;

        static void Prefix() => calculating = true;
        static void Postfix() => calculating = false;
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickRateMultiplier), MethodType.Getter)]
    static class TickRateMultiplierDuringReplay
    {
        static void Postfix(ref float __result)
        {
            if (!PreDrawCalcMarker.calculating) return;
            if (Multiplayer.Client == null) return;
            if (WorldRendererUtility.WorldRenderedNow) return;
            if (!Multiplayer.IsReplay) return;

            __result = TickPatch.skipTo >= 0 ? 6 : Find.CurrentMap.AsyncTime().TickRateMultiplier(TickPatch.replayTimeSpeed);
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.Paused), MethodType.Getter)]
    static class TickManagerPausedDuringReplay
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client == null) return;
            if (WorldRendererUtility.WorldRenderedNow) return;
            if (!Multiplayer.IsReplay) return;

            __result = Find.CurrentMap.AsyncTime().TickRateMultiplier(TickPatch.replayTimeSpeed) == 0;
        }
    }

    [HarmonyPatch(typeof(Storyteller))]
    [HarmonyPatch(nameof(Storyteller.StorytellerTick))]
    public class StorytellerTickPatch
    {
        static bool Prefix()
        {
            return Multiplayer.Client == null || Multiplayer.Ticking;
        }
    }

    [HarmonyPatch(typeof(Storyteller))]
    [HarmonyPatch(nameof(Storyteller.AllIncidentTargets), MethodType.Getter)]
    public class StorytellerTargetsPatch
    {
        static void Postfix(List<IIncidentTarget> __result)
        {
            if (Multiplayer.Client == null) return;

            if (Multiplayer.MapContext != null)
            {
                __result.Clear();
                __result.Add(Multiplayer.MapContext);
            }
            else if (MultiplayerWorldComp.tickingWorld)
            {
                __result.Clear();

                foreach (var caravan in Find.WorldObjects.Caravans)
                    if (caravan.IsPlayerControlled)
                        __result.Add(caravan);

                __result.Add(Find.World);
            }
        }
    }

    public class MapAsyncTimeComp : MapComponent, ITickable
    {
        public static Map tickingMap;
        public static Map executingCmdMap;

        public float TimePerTick(TimeSpeed speed)
        {
            if (TickRateMultiplier(speed) == 0f)
                return 0f;
            return 1f / TickRateMultiplier(speed);
        }

        public float TickRateMultiplier(TimeSpeed speed)
        {
            var comp = map.MpComp();
            if (comp.caravanForming != null || comp.mapDialogs.Any())
                return 0f;

            switch (speed)
            {
                case TimeSpeed.Paused:
                    return 0f;
                case TimeSpeed.Normal:
                    return 1f;
                case TimeSpeed.Fast:
                    return 3f;
                case TimeSpeed.Superfast:
                    if (nothingHappeningCached)
                        return 12f;
                    return 6f;
                case TimeSpeed.Ultrafast:
                    return 15f;
                default:
                    return -1f;
            }
        }

        public TimeSpeed TimeSpeed
        {
            get => timeSpeedInt;
            set => timeSpeedInt = value;
        }

        public bool Paused => TickRateMultiplier(TimeSpeed) == 0f;

        public float RealTimeToTickThrough { get; set; }

        public Queue<ScheduledCommand> Cmds { get => cmds; }

        public int mapTicks;
        private TimeSpeed timeSpeedInt;
        public bool forcedNormalSpeed;

        public Storyteller storyteller;

        public TickList tickListNormal = new TickList(TickerType.Normal);
        public TickList tickListRare = new TickList(TickerType.Rare);
        public TickList tickListLong = new TickList(TickerType.Long);

        // Shared random state for ticking and commands
        public ulong randState = 1;

        public Queue<ScheduledCommand> cmds = new Queue<ScheduledCommand>();

        public MapAsyncTimeComp(Map map) : base(map)
        {
        }

        public int tickRel;

        public void Tick()
        {
            tickingMap = map;
            PreContext();

            //SimpleProfiler.Start();

            try
            {
                map.MapPreTick();
                mapTicks++;
                Find.TickManager.ticksGameInt = mapTicks;

                tickListNormal.Tick();
                tickListRare.Tick();
                tickListLong.Tick();

                TickMapTrading();

                storyteller.StorytellerTick();

                map.MapPostTick();

                UpdateManagers();
                CacheNothingHappening();
            }
            finally
            {
                PostContext();

                Multiplayer.game.sync.TryAddMap(map.uniqueID, randState);

                tickingMap = null;

                //SimpleProfiler.Pause();
            }
        }

        public void TickMapTrading()
        {
            var trading = Multiplayer.WorldComp.trading;

            for (int i = trading.Count - 1; i >= 0; i--)
            {
                var session = trading[i];
                if (session.playerNegotiator.Map != map) continue;

                if (session.ShouldCancel())
                {
                    Multiplayer.WorldComp.RemoveTradeSession(session);
                    continue;
                }
            }
        }

        // These are normally called in Map.MapUpdate() and react to changes in the game state even when the game is paused (not ticking)
        // Update() methods are not deterministic, but in multiplayer all game state changes (which don't happen during ticking) happen in commands
        // Thus these methods can be moved to Tick() and ExecuteCmd()
        public void UpdateManagers()
        {
            map.regionGrid.UpdateClean();
            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();

            map.powerNetManager.UpdatePowerNetsAndConnections_First();
            map.glowGrid.GlowGridUpdate_First();
        }

        private PrevTime? prevTime;
        private Storyteller prevStoryteller;

        public void PreContext()
        {
            map.PushFaction(map.ParentFaction);

            prevTime = PrevTime.GetAndSetToMap(map);

            prevStoryteller = Current.Game.storyteller;
            Current.Game.storyteller = storyteller;

            //UniqueIdsPatch.CurrentBlock = map.MpComp().mapIdBlock;
            UniqueIdsPatch.CurrentBlock = Multiplayer.GlobalIdBlock;

            Rand.StateCompressed = randState;

            // Reset the effects of SkyManager.SkyManagerUpdate
            map.skyManager.curSkyGlowInt = map.skyManager.CurrentSkyTarget().glow;
        }

        public void PostContext()
        {
            UniqueIdsPatch.CurrentBlock = null;

            Current.Game.storyteller = prevStoryteller;

            prevTime?.Set();

            randState = Rand.StateCompressed;

            map.PopFaction();
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref mapTicks, "mapTicks");
            Scribe_Deep.Look(ref storyteller, "storyteller");
            Scribe_Values.Look(ref timeSpeedInt, "timeSpeed");
        }

        public override void FinalizeInit()
        {
            cmds = new Queue<ScheduledCommand>(OnMainThread.cachedMapCmds.GetValueSafe(map.uniqueID) ?? new List<ScheduledCommand>());
            Log.Message("init map with cmds " + cmds.Count);
        }

        public void ExecuteCmd(ScheduledCommand cmd)
        {
            ByteReader data = new ByteReader(cmd.data);
            MpContext context = data.MpContext();

            CommandType cmdType = cmd.type;

            executingCmdMap = map;
            TickPatch.currentExecutingCmdIssuedBySelf = cmd.issuedBySelf && TickPatch.skipTo < 0;

            CurrentMapGetPatch.currentMap = map;
            CurrentMapSetPatch.ignore = true;

            PreContext();
            map.PushFaction(cmd.GetFaction());

            context.map = map;

            List<object> prevSelected = Find.Selector.selected;
            if (!TickPatch.currentExecutingCmdIssuedBySelf)
                Find.Selector.selected = new List<object>();

            try
            {
                if (cmdType == CommandType.Sync)
                {
                    Sync.HandleCmd(data);
                }

                if (cmdType == CommandType.CreateMapFactionData)
                {
                    HandleMapFactionData(cmd, data);
                }

                if (cmdType == CommandType.MapTimeSpeed)
                {
                    TimeSpeed speed = (TimeSpeed)data.ReadByte();
                    TimeSpeed = speed;

                    MpLog.Log("Set map time speed " + speed);
                }

                if (cmdType == CommandType.MapIdBlock)
                {
                    IdBlock block = IdBlock.Deserialize(data);

                    if (map != null)
                    {
                        //map.MpComp().mapIdBlock = block;
                    }
                }

                if (cmdType == CommandType.Designator)
                {
                    HandleDesignator(cmd, data);
                }

                if (cmdType == CommandType.SpawnPawn)
                {
                    /*Pawn pawn = ScribeUtil.ReadExposable<Pawn>(data.ReadPrefixedBytes());

                    IntVec3 spawn = CellFinderLoose.TryFindCentralCell(map, 7, 10, (IntVec3 x) => !x.Roofed(map));
                    GenSpawn.Spawn(pawn, spawn, map);
                    Log.Message("spawned " + pawn);*/
                }

                if (cmdType == CommandType.Forbid)
                {
                    HandleForbid(cmd, data);
                }

                UpdateManagers();
            }
            catch (Exception e)
            {
                Log.Error($"Map cmd exception ({cmdType}): {e}");
            }
            finally
            {
                if (!TickPatch.currentExecutingCmdIssuedBySelf)
                    Find.Selector.selected = prevSelected;

                CurrentMapSetPatch.ignore = false;
                CurrentMapGetPatch.currentMap = null;
                map.PopFaction();
                PostContext();
                TickPatch.currentExecutingCmdIssuedBySelf = false;
                executingCmdMap = null;

                Multiplayer.game.sync.TryAddCmd(randState);
            }
        }

        private void HandleForbid(ScheduledCommand cmd, ByteReader data)
        {
            int thingId = data.ReadInt32();
            bool value = data.ReadBool();

            ThingWithComps thing = map.listerThings.AllThings.Find(t => t.thingIDNumber == thingId) as ThingWithComps;
            if (thing == null) return;

            CompForbiddable forbiddable = thing.GetComp<CompForbiddable>();
            if (forbiddable == null) return;

            forbiddable.Forbidden = value;
        }

        private void HandleMapFactionData(ScheduledCommand cmd, ByteReader data)
        {
            int factionId = data.ReadInt32();

            Faction faction = Find.FactionManager.GetById(factionId);
            MultiplayerMapComp comp = map.MpComp();

            if (!comp.factionMapData.ContainsKey(factionId))
            {
                FactionMapData factionMapData = FactionMapData.New(factionId, map);
                comp.factionMapData[factionId] = factionMapData;

                factionMapData.areaManager.AddStartingAreas();
                map.pawnDestinationReservationManager.RegisterFaction(faction);

                MpLog.Log($"New map faction data for {faction.GetUniqueLoadID()}");
            }
        }

        private void HandleDesignator(ScheduledCommand command, ByteReader data)
        {
            DesignatorMode mode = Sync.ReadSync<DesignatorMode>(data);
            Designator designator = Sync.ReadSync<Designator>(data);
            if (designator == null) return;

            try
            {
                if (!SetDesignatorState(designator, data)) return;

                if (mode == DesignatorMode.SingleCell)
                {
                    IntVec3 cell = Sync.ReadSync<IntVec3>(data);
                    designator.DesignateSingleCell(cell);
                    designator.Finalize(true);
                }
                else if (mode == DesignatorMode.MultiCell)
                {
                    IntVec3[] cells = Sync.ReadSync<IntVec3[]>(data);
                    designator.DesignateMultiCell(cells);
                }
                else if (mode == DesignatorMode.Thing)
                {
                    Thing thing = Sync.ReadSync<Thing>(data);

                    if (thing != null)
                    {
                        designator.DesignateThing(thing);
                        designator.Finalize(true);
                    }
                }

                foreach (Zone zone in map.zoneManager.AllZones)
                    zone.cellsShuffled = true;
            }
            finally
            {
                DesignatorInstallPatch.thingToInstall = null;
            }
        }

        private bool SetDesignatorState(Designator designator, ByteReader data)
        {
            if (designator is Designator_AreaAllowed)
            {
                Area area = Sync.ReadSync<Area>(data);
                if (area == null) return false;
                Designator_AreaAllowed.selectedArea = area;
            }

            if (designator is Designator_Place place)
            {
                place.placingRot = Sync.ReadSync<Rot4>(data);
            }

            if (designator is Designator_Build build && build.PlacingDef.MadeFromStuff)
            {
                ThingDef stuffDef = Sync.ReadSync<ThingDef>(data);
                if (stuffDef == null) return false;
                build.stuffDef = stuffDef;
            }

            if (designator is Designator_Install)
            {
                Thing thing = Sync.ReadSync<Thing>(data);
                if (thing == null) return false;
                DesignatorInstallPatch.thingToInstall = thing;
            }

            return true;
        }

        private bool nothingHappeningCached;

        private void CacheNothingHappening()
        {
            nothingHappeningCached = true;
            var list = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);

            for (int j = 0; j < list.Count; j++)
            {
                Pawn pawn = list[j];
                if (pawn.HostFaction == null && pawn.RaceProps.Humanlike && pawn.Awake())
                    nothingHappeningCached = false;
            }

            if (nothingHappeningCached && map.IsPlayerHome && map.dangerWatcher.DangerRating >= StoryDanger.Low)
                nothingHappeningCached = false;
        }

        public override string ToString()
        {
            return $"{nameof(MapAsyncTimeComp)}_{map}";
        }
    }

    public enum DesignatorMode : byte
    {
        SingleCell,
        MultiCell,
        Thing
    }

    public class SyncInfoBuffer
    {
        public List<SyncInfo> buffer = new List<SyncInfo>();

        public bool ShouldCollect => !Multiplayer.IsReplay && TickPatch.skipTo < 0;

        public SyncInfo current;
        private SyncInfo Current
        {
            get
            {
                if (current != null)
                    return current;

                current = new SyncInfo(TickPatch.Timer)
                {
                    local = true
                };

                return current;
            }
        }

        public void Add(SyncInfo info)
        {
            if (buffer.Count == 0)
            {
                buffer.Add(info);
                return;
            }

            if (buffer[0].local == info.local)
            {
                buffer.Add(info);
                if (buffer.Count > 30)
                    buffer.RemoveAt(0);
            }
            else
            {
                while (buffer.Count > 0 && buffer[0].startTick < info.startTick)
                    buffer.RemoveAt(0);

                if (buffer.Count == 0)
                {
                    buffer.Add(info);
                }
                else if (buffer.First().startTick == info.startTick)
                {
                    var first = buffer.RemoveFirst();
                    var error = first.Compare(info);
                    if (error != null)
                        OnDesynced(first, info);
                }
            }
        }

        private void OnDesynced(SyncInfo one, SyncInfo two)
        {
            if (Multiplayer.session.desynced) return;

            Multiplayer.Client.Send(Packets.Client_Desynced);
            Multiplayer.session.desynced = true;

            try
            {
                var desyncFile = PrepareNextDesyncFile();

                var replay = Replay.ForSaving(desyncFile, Multiplayer.DesyncsDir);
                replay.WriteCurrentData();

                var local = one.local ? one : two;
                var remote = !one.local ? one : two;
                var savedGame = ScribeUtil.WriteExposable(Verse.Current.Game, "game");

                var zip = replay.ZipFile;
                zip.AddEntry("sync_local", local.Serialize());
                zip.AddEntry("sync_remote", remote.Serialize());
                zip.AddEntry("game_snapshot", savedGame);
                zip.Save();
            }
            catch (Exception e)
            {
                Log.Error($"Exception writing desync info: {e}");
            }

            Find.WindowStack.windows.Clear();
            Find.WindowStack.Add(new DesyncedWindow());
        }

        private string PrepareNextDesyncFile()
        {
            var files = new DirectoryInfo(Multiplayer.DesyncsDir).GetFiles("Desync-*.zip");

            const int MaxFiles = 5;
            if (files.Length > MaxFiles - 1)
                files.OrderByDescending(f => f.LastWriteTime).Skip(MaxFiles - 1).Do(f => f.Delete());

            int max = 0;
            foreach (var f in files)
                if (int.TryParse(f.Name.Substring(7, f.Name.Length - 7 - 4), out int result) && result > max)
                    max = result;

            return $"Desync-{max + 1:00}";
        }

        public void TryAddCmd(ulong state)
        {
            if (!ShouldCollect) return;
            Current.cmds.Add((uint)(state >> 32));
        }

        public void TryAddWorld(ulong state)
        {
            if (!ShouldCollect) return;
            Current.world.Add((uint)(state >> 32));
        }

        public void TryAddMap(int map, ulong state)
        {
            if (!ShouldCollect) return;
            Current.GetForMap(map).Add((uint)(state >> 32));
        }
    }

    public class SyncInfo
    {
        public bool local;
        public int startTick;
        public List<uint> cmds = new List<uint>();
        public List<uint> world = new List<uint>();
        public List<SyncMapInfo> maps = new List<SyncMapInfo>();

        public SyncInfo(int startTick)
        {
            this.startTick = startTick;
        }

        public string Compare(SyncInfo other)
        {
            if (!maps.Select(m => m.mapId).SequenceEqual(other.maps.Select(m => m.mapId)))
                return $"Map instances don't match";

            for (int i = 0; i < maps.Count; i++)
            {
                if (!maps[i].map.SequenceEqual(other.maps[i].map))
                    return $"Wrong random state on map {maps[i].mapId}";
            }

            if (!world.SequenceEqual(other.world))
                return "Wrong random state for the world";

            if (!cmds.SequenceEqual(other.cmds))
                return "Random state from commands doesn't match";

            return null;
        }

        public List<uint> GetForMap(int mapId)
        {
            var result = maps.Find(m => m.mapId == mapId);
            if (result != null) return result.map;
            maps.Add(result = new SyncMapInfo(mapId));
            return result.map;
        }

        public byte[] Serialize()
        {
            var writer = new ByteWriter();

            writer.WriteInt32(startTick);
            writer.WritePrefixedUInts(cmds);
            writer.WritePrefixedUInts(world);

            writer.WriteInt32(maps.Count);
            foreach (var map in maps)
            {
                writer.WriteInt32(map.mapId);
                writer.WritePrefixedUInts(map.map);
            }

            return writer.GetArray();
        }

        public static SyncInfo Deserialize(ByteReader data)
        {
            var startTick = data.ReadInt32();

            var cmds = new List<uint>(data.ReadPrefixedUInts());
            var world = new List<uint>(data.ReadPrefixedUInts());

            var maps = new List<SyncMapInfo>();
            int mapCount = data.ReadInt32();
            for (int i = 0; i < mapCount; i++)
            {
                int mapId = data.ReadInt32();
                var mapData = new List<uint>(data.ReadPrefixedUInts());
                maps.Add(new SyncMapInfo(mapId) { map = mapData });
            }

            return new SyncInfo(startTick)
            {
                cmds = cmds,
                world = world,
                maps = maps
            };
        }
    }

    public class SyncMapInfo
    {
        public int mapId;
        public List<uint> map = new List<uint>();

        public SyncMapInfo(int mapId)
        {
            this.mapId = mapId;
        }
    }

}
