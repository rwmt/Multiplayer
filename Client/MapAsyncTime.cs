using Harmony;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickManagerUpdate))]
    public static class TickPatch
    {
        const float TimeStep = 1 / 6f;

        public static int Timer => (int)timerInt;

        public static float accumulator;
        public static double timerInt;
        public static int tickUntil;

        public static IEnumerable<ITickable> AllTickables
        {
            get
            {
                MultiplayerWorldComp comp = Multiplayer.WorldComp;
                yield return comp;
                yield return comp.ticker;

                foreach (Map map in Find.Maps)
                    yield return map.GetComponent<MapAsyncTimeComp>();
            }
        }

        static bool Prefix()
        {
            if (Multiplayer.client == null) return true;

            float delta = Time.deltaTime * 60f;
            if (delta > 3f)
                delta = 3f;

            accumulator += delta;

            if (Timer >= tickUntil)
                accumulator = 0;
            else if (delta < 1.5f && tickUntil - timerInt > 5)
                accumulator *= 2f;

            Tick();

            return false;
        }

        static void Postfix()
        {
            if (Multiplayer.client == null || Find.VisibleMap == null) return;

            MapAsyncTimeComp comp = Find.VisibleMap.GetComponent<MapAsyncTimeComp>();
            Shader.SetGlobalFloat(ShaderPropertyIDs.GameSeconds, comp.mapTicks.TicksToSeconds());
        }

        public static void Tick()
        {
            while (accumulator > 0)
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
                    if (tickable.CurTimePerTick == 0)
                        continue;

                    tickable.RealTimeToTickThrough += TimeStep;

                    while (tickable.RealTimeToTickThrough >= 0)
                    {
                        tickable.RealTimeToTickThrough -= tickable.CurTimePerTick;
                        tickable.Tick();
                    }
                }

                accumulator -= TimeStep;
                timerInt += TimeStep;
            }
        }
    }

    public interface ITickable
    {
        float RealTimeToTickThrough { get; set; }

        float CurTimePerTick { get; }

        TimeSpeed TimeSpeed { get; }

        Queue<ScheduledCommand> Cmds { get; }

        void Tick();

        void ExecuteCmd(ScheduledCommand cmd);
    }

    public class ConstantTicker : ITickable
    {
        public static bool ticking;

        public float RealTimeToTickThrough { get; set; }
        public float CurTimePerTick => 1f;
        public TimeSpeed TimeSpeed => TimeSpeed.Normal;
        public Queue<ScheduledCommand> Cmds => cmds;
        public Queue<ScheduledCommand> cmds = new Queue<ScheduledCommand>();

        public void ExecuteCmd(ScheduledCommand cmd)
        {
        }

        public void Tick()
        {
            ticking = true;

            try
            {
                TickSync();
                TickResearch();
                SyncResearch.ConstantTick();
            }
            finally
            {
                ticking = false;
            }
        }

        public void TickSync()
        {
            Extensions.PushFaction(null, Multiplayer.RealPlayerFaction);

            foreach (SyncField f in Sync.bufferedFields)
            {
                if (!f.inGameLoop) continue;

                Sync.bufferedChanges[f].RemoveAll((k, data) =>
                {
                    if (!data.sent && TickPatch.Timer - data.timestamp > 30)
                    {
                        f.DoSync(k.first, data.toSend, k.second);
                        data.sent = true;
                    }

                    return !Equals(k.first.GetPropertyOrField(f.memberPath, k.second), data.currentValue);
                });
            }

            Extensions.PopFaction(null);
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

                Extensions.PopFaction(null);
            }
        }
    }

    [HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
    public static class MapUpdateTimePatch
    {
        static void Prefix(Map __instance, ref Container<int, TimeSpeed> __state)
        {
            if (Multiplayer.client == null) return;

            __state = new Container<int, TimeSpeed>(Find.TickManager.TicksGame, Find.TickManager.CurTimeSpeed);

            MapAsyncTimeComp comp = __instance.GetComponent<MapAsyncTimeComp>();
            Find.TickManager.DebugSetTicksGame(comp.mapTicks);
            Find.TickManager.CurTimeSpeed = comp.TimeSpeed;
        }

        static void Postfix(Container<int, TimeSpeed> __state)
        {
            if (__state == null) return;

            Find.TickManager.DebugSetTicksGame(__state.First);
            Find.TickManager.CurTimeSpeed = __state.Second;
        }
    }

    [MpPatch(typeof(Map), nameof(Map.MapPreTick))]
    [MpPatch(typeof(Map), nameof(Map.MapPostTick))]
    [MpPatch(typeof(TickList), nameof(TickList.Tick))]
    static class CancelMapManagersTick
    {
        static bool Prefix() => Multiplayer.client == null || MapAsyncTimeComp.tickingMap != null;
    }

    [HarmonyPatch(typeof(Autosaver), nameof(Autosaver.AutosaverTick))]
    static class DisableAutosaver
    {
        static bool Prefix() => Multiplayer.client == null;
    }

    [HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
    static class MapUpdateMarker
    {
        public static bool updating;

        static void Prefix() => updating = true;
        static void Postfix() => updating = false;
    }

    [MpPatch(typeof(PowerNetManager), nameof(PowerNetManager.UpdatePowerNetsAndConnections_First))]
    [MpPatch(typeof(RegionGrid), nameof(RegionGrid.UpdateClean))]
    [MpPatch(typeof(GlowGrid), nameof(GlowGrid.GlowGridUpdate_First))]
    [MpPatch(typeof(RegionAndRoomUpdater), nameof(RegionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms))]
    static class CancelMapManagersUpdate
    {
        static bool Prefix() => Multiplayer.client == null || !MapUpdateMarker.updating;
    }

    [HarmonyPatch(typeof(DateNotifier), nameof(DateNotifier.DateNotifierTick))]
    static class DateNotifierPatch
    {
        static void Prefix(DateNotifier __instance, ref int? __state)
        {
            if (Multiplayer.client == null && Multiplayer.RealPlayerFaction != null) return;

            __state = Find.TickManager.TicksGame;
            FactionContext.Push(Multiplayer.RealPlayerFaction);
            Map map = __instance.FindPlayerHomeWithMinTimezone();
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
            if (Multiplayer.client == null) return true;
            MapAsyncTimeComp comp = t.Map.GetComponent<MapAsyncTimeComp>();

            if (t.def.tickerType == TickerType.Normal)
                comp.tickListNormal.RegisterThing(t);
            else if (t.def.tickerType == TickerType.Rare)
                comp.tickListRare.RegisterThing(t);
            else if (t.def.tickerType == TickerType.Long)
                comp.tickListLong.RegisterThing(t);

            return false;
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DeRegisterAllTickabilityFor))]
    public static class TickListRemove
    {
        static bool Prefix(Thing t)
        {
            if (Multiplayer.client == null) return true;
            MapAsyncTimeComp comp = t.Map.GetComponent<MapAsyncTimeComp>();

            if (t.def.tickerType == TickerType.Normal)
                comp.tickListNormal.DeregisterThing(t);
            else if (t.def.tickerType == TickerType.Rare)
                comp.tickListRare.DeregisterThing(t);
            else if (t.def.tickerType == TickerType.Long)
                comp.tickListLong.DeregisterThing(t);

            return false;
        }
    }

    [HarmonyPatch(typeof(TimeControls), nameof(TimeControls.DoTimeControlsGUI))]
    public static class TimeControlPatch
    {
        static void Prefix(ref bool __state)
        {
            if (Multiplayer.client == null || (!WorldRendererUtility.WorldRenderedNow && Find.VisibleMap == null)) return;

            ITickable tickable = WorldRendererUtility.WorldRenderedNow ? Multiplayer.WorldComp : (ITickable)(Find.VisibleMap?.GetComponent<MapAsyncTimeComp>());
            Find.TickManager.CurTimeSpeed = tickable.TimeSpeed;
            __state = true;
        }

        static void Postfix(bool __state)
        {
            if (!__state) return;

            ITickable tickable = WorldRendererUtility.WorldRenderedNow ? Multiplayer.WorldComp : (ITickable)(Find.VisibleMap?.GetComponent<MapAsyncTimeComp>());
            if (Find.TickManager.CurTimeSpeed == tickable.TimeSpeed) return;

            if (WorldRendererUtility.WorldRenderedNow)
                Multiplayer.client.SendCommand(CommandType.WORLD_TIME_SPEED, ScheduledCommand.Global, (byte)Find.TickManager.CurTimeSpeed);
            else if (Find.VisibleMap != null)
                Multiplayer.client.SendCommand(CommandType.MAP_TIME_SPEED, Find.VisibleMap.uniqueID, (byte)Find.TickManager.CurTimeSpeed);
        }
    }

    public static class SetMapTimeForUI
    {
        static void Prefix(ref Container<int, TimeSpeed> __state)
        {
            if (Multiplayer.client == null || WorldRendererUtility.WorldRenderedNow || Find.VisibleMap == null) return;

            Map map = Find.VisibleMap;
            __state = new Container<int, TimeSpeed>(Find.TickManager.TicksGame, Find.TickManager.CurTimeSpeed);
            MapAsyncTimeComp comp = map.GetComponent<MapAsyncTimeComp>();
            Find.TickManager.DebugSetTicksGame(comp.mapTicks);
            Find.TickManager.CurTimeSpeed = comp.TimeSpeed;
        }

        static void Postfix(Container<int, TimeSpeed> __state)
        {
            if (__state == null) return;

            Find.TickManager.DebugSetTicksGame(__state.First);
            Find.TickManager.CurTimeSpeed = __state.Second;
        }
    }

    [HarmonyPatch(typeof(PortraitsCache))]
    [HarmonyPatch(nameof(PortraitsCache.IsAnimated))]
    public static class PawnPortraitMapTime
    {
        static void Prefix(Pawn pawn, ref Container<int, TimeSpeed> __state)
        {
            if (Multiplayer.client == null || Current.ProgramState != ProgramState.Playing) return;

            Map map = pawn.Map;
            if (map == null) return;

            __state = new Container<int, TimeSpeed>(Find.TickManager.TicksGame, Find.TickManager.CurTimeSpeed);
            MapAsyncTimeComp comp = map.GetComponent<MapAsyncTimeComp>();
            Find.TickManager.DebugSetTicksGame(comp.mapTicks);
            Find.TickManager.CurTimeSpeed = comp.TimeSpeed;
        }

        static void Postfix(Container<int, TimeSpeed> __state)
        {
            if (__state == null) return;

            Find.TickManager.DebugSetTicksGame(__state.First);
            Find.TickManager.CurTimeSpeed = __state.Second;
        }
    }

    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    [StaticConstructorOnStartup]
    public static class ColonistBarTimeControl
    {
        static void Postfix()
        {
            if (Multiplayer.client == null) return;

            ColonistBar bar = Find.ColonistBar;
            if (bar.Entries.Count == 0 || bar.Entries[bar.Entries.Count - 1].group == 0) return;

            int curGroup = -1;
            foreach (ColonistBar.Entry entry in bar.Entries)
            {
                if (entry.map == null || curGroup == entry.group) continue;

                float alpha = 1.0f;
                if (entry.map != Find.VisibleMap || WorldRendererUtility.WorldRenderedNow)
                    alpha = 0.75f;

                MapAsyncTimeComp comp = entry.map.GetComponent<MapAsyncTimeComp>();
                Rect rect = bar.drawer.GroupFrameRect(entry.group);
                Rect button = new Rect(rect.x - TimeControls.TimeButSize.x / 2f, rect.yMax - TimeControls.TimeButSize.y / 2f, TimeControls.TimeButSize.x, TimeControls.TimeButSize.y);
                Widgets.DrawRectFast(button, new Color(0.5f, 0.5f, 0.5f, 0.4f * alpha));
                Widgets.ButtonImage(button, TexButton.SpeedButtonTextures[(int)comp.TimeSpeed]);

                curGroup = entry.group;
            }
        }
    }

    [HarmonyPatch(typeof(Storyteller))]
    [HarmonyPatch(nameof(Storyteller.StorytellerTick))]
    public class StorytellerTickPatch
    {
        static bool Prefix()
        {
            // The storyteller is currently only enabled for maps
            return Multiplayer.client == null || !MultiplayerWorldComp.tickingWorld;
        }
    }

    [HarmonyPatch(typeof(Storyteller))]
    [HarmonyPatch(nameof(Storyteller.AllIncidentTargets), PropertyMethod.Getter)]
    public class StorytellerTargetsPatch
    {
        public static Map target;

        static void Postfix(ref List<IIncidentTarget> __result)
        {
            if (Multiplayer.client == null) return;
            if (target == null) return;

            __result.Clear();
            __result.Add(target);
        }
    }

    public class MapAsyncTimeComp : MapComponent, ITickable
    {
        public static Map tickingMap;

        public float CurTimePerTick
        {
            get
            {
                if (TickRateMultiplier == 0f)
                    return 0f;
                return 1f / TickRateMultiplier;
            }
        }

        public float TickRateMultiplier
        {
            get
            {
                if (TimeSpeed == TimeSpeed.Paused)
                    return 0;
                if (forcedNormalSpeed)
                    return 1;
                if (TimeSpeed == TimeSpeed.Fast)
                    return 3;
                // todo speed up when nothing is happening
                if (TimeSpeed == TimeSpeed.Superfast)
                    return 6;
                return 1;
            }
        }

        public TimeSpeed TimeSpeed
        {
            get => timeSpeedInt;
            set => timeSpeedInt = value;
        }

        public float RealTimeToTickThrough { get; set; }

        public Queue<ScheduledCommand> Cmds { get => cmds; }

        public int mapTicks;
        public TimeSpeed timeSpeedInt;
        public bool forcedNormalSpeed; // todo save?

        public Storyteller storyteller;

        public TickList tickListNormal = new TickList(TickerType.Normal);
        public TickList tickListRare = new TickList(TickerType.Rare);
        public TickList tickListLong = new TickList(TickerType.Long);

        // Shared random state for ticking and commands
        public ulong randState = 1;

        public Queue<ScheduledCommand> cmds = new Queue<ScheduledCommand>();

        public MapAsyncTimeComp(Map map) : base(map)
        {
            storyteller = new Storyteller(StorytellerDefOf.Cassandra, DifficultyDefOf.Medium);
        }

        public void Tick()
        {
            tickingMap = map;
            PreContext();

            //SimpleProfiler.Start();

            try
            {
                UpdateRegionsAndRooms();

                map.MapPreTick();
                mapTicks++;
                Find.TickManager.DebugSetTicksGame(mapTicks);

                tickListNormal.Tick();
                tickListRare.Tick();
                tickListLong.Tick();

                map.PushFaction(map.ParentFaction);
                storyteller.StorytellerTick();
                map.PopFaction();

                map.MapPostTick();

                UpdateManagers();
            }
            finally
            {
                PostContext();

                tickingMap = null;

                //SimpleProfiler.Pause();

                if (!Multiplayer.simulating && false)
                    if (mapTicks % 300 == 0 && SimpleProfiler.available)
                    {
                        SimpleProfiler.Print("profiler_" + Multiplayer.username + "_tick.txt");
                        SimpleProfiler.Init(Multiplayer.username);

                        map.GetComponent<MultiplayerMapComp>().SetFaction(map.ParentFaction);
                        byte[] mapData = ScribeUtil.WriteExposable(map, "map", true);
                        File.WriteAllBytes("map_0_" + Multiplayer.username + ".xml", mapData);
                        map.GetComponent<MultiplayerMapComp>().SetFaction(Multiplayer.RealPlayerFaction);
                    }
            }
        }

        public void UpdateRegionsAndRooms()
        {
            map.regionGrid.UpdateClean();
            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
        }

        public void UpdateManagers()
        {
            map.powerNetManager.UpdatePowerNetsAndConnections_First();
            map.glowGrid.GlowGridUpdate_First();
        }

        private int worldTicks;
        private TimeSpeed worldSpeed;
        private Storyteller globalStoryteller;

        public void PreContext()
        {
            map.PushFaction(map.ParentFaction);

            worldTicks = Find.TickManager.TicksGame;
            worldSpeed = Find.TickManager.CurTimeSpeed;
            Find.TickManager.DebugSetTicksGame(mapTicks);
            Find.TickManager.CurTimeSpeed = TimeSpeed;

            globalStoryteller = Current.Game.storyteller;
            Current.Game.storyteller = storyteller;
            StorytellerTargetsPatch.target = map;

            UniqueIdsPatch.CurrentBlock = map.GetComponent<MultiplayerMapComp>().mapIdBlock;

            Rand.StateCompressed = randState;

            // Reset the effects of SkyManager.SkyManagerUpdate
            map.skyManager.curSkyGlowInt = map.skyManager.CurrentSkyTarget().glow;
        }

        public void PostContext()
        {
            UniqueIdsPatch.CurrentBlock = null;

            Current.Game.storyteller = globalStoryteller;
            StorytellerTargetsPatch.target = null;

            Find.TickManager.DebugSetTicksGame(worldTicks);
            Find.TickManager.CurTimeSpeed = worldSpeed;

            randState = Rand.StateCompressed;

            map.PopFaction();
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref mapTicks, "mapTicks");
            Scribe_Values.Look(ref timeSpeedInt, "timeSpeed");
        }

        public void ExecuteCmd(ScheduledCommand cmd)
        {
            OnMainThread.ExecuteMapCmd(cmd, new ByteReader(cmd.data));
        }
    }
}
