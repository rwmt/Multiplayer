using Harmony;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickManagerUpdate))]
    public static class TickManagerUpdatePatch
    {
        static void Prefix()
        {
            if (Multiplayer.client == null) return;

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                MapAsyncTimeComp comp = maps[i].GetComponent<MapAsyncTimeComp>();

                if (comp.timeSpeed != TimeSpeed.Paused && comp.Timer < TickPatch.tickUntil)
                {
                    if (Mathf.Abs(Time.deltaTime - comp.CurTimePerTick) < comp.CurTimePerTick * 0.2f)
                        comp.realTimeToTickThrough += comp.CurTimePerTick;
                    else
                        comp.realTimeToTickThrough += Time.deltaTime;

                    int num = 0;
                    while (comp.realTimeToTickThrough > 0f && num < comp.TickRateMultiplier * 2f)
                    {
                        comp.Tick();

                        comp.realTimeToTickThrough -= comp.CurTimePerTick;
                        num++;
                    }

                    if (comp.realTimeToTickThrough > 0f)
                        comp.realTimeToTickThrough = 0f;
                }

                if (!LongEventHandler.ShouldWaitForEvent && Current.Game != null && Find.World != null)
                    comp.ExecuteMapCmdsWhilePaused();
            }
        }

        static void Postfix()
        {
            if (Multiplayer.client == null || Find.VisibleMap == null) return;

            MapAsyncTimeComp comp = Find.VisibleMap.GetComponent<MapAsyncTimeComp>();
            Shader.SetGlobalFloat(ShaderPropertyIDs.GameSeconds, comp.mapTicks.TicksToSeconds());
        }
    }

    [HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
    public static class MapUpdatePatch
    {
        static void Prefix(Map __instance, ref Container<int, TimeSpeed> __state)
        {
            if (Multiplayer.client == null) return;

            __state = new Container<int, TimeSpeed>(Find.TickManager.TicksGame, Find.TickManager.CurTimeSpeed);

            MapAsyncTimeComp comp = __instance.GetComponent<MapAsyncTimeComp>();
            Find.TickManager.DebugSetTicksGame(comp.mapTicks);
            Find.TickManager.CurTimeSpeed = comp.timeSpeed;
        }

        static void Postfix(Container<int, TimeSpeed> __state)
        {
            if (__state == null) return;

            Find.TickManager.DebugSetTicksGame(__state.First);
            Find.TickManager.CurTimeSpeed = __state.Second;
        }
    }

    [HarmonyPatch(typeof(Map), nameof(Map.MapPreTick))]
    public static class CancelMapPreTick
    {
        static bool Prefix()
        {
            return Multiplayer.client == null || MapAsyncTimeComp.tickingMap;
        }
    }

    [HarmonyPatch(typeof(Map), nameof(Map.MapPostTick))]
    public static class CancelMapPostTick
    {
        static bool Prefix()
        {
            return Multiplayer.client == null || MapAsyncTimeComp.tickingMap;
        }
    }

    [HarmonyPatch(typeof(Autosaver), nameof(Autosaver.AutosaverTick))]
    public static class DisableAutosaver
    {
        static bool Prefix()
        {
            return Multiplayer.client == null;
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.RegisterAllTickabilityFor))]
    public static class TickListAdd
    {
        static void Prefix(Thing t)
        {
            MapAsyncTimeComp comp = t.Map.GetComponent<MapAsyncTimeComp>();

            if (t.def.tickerType == TickerType.Normal)
                comp.tickListNormal.RegisterThing(t);
            else if (t.def.tickerType == TickerType.Rare)
                comp.tickListRare.RegisterThing(t);
            else if (t.def.tickerType == TickerType.Long)
                comp.tickListLong.RegisterThing(t);
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DeRegisterAllTickabilityFor))]
    public static class TickListRemove
    {
        static void Prefix(Thing t)
        {
            MapAsyncTimeComp comp = t.Map.GetComponent<MapAsyncTimeComp>();

            if (t.def.tickerType == TickerType.Normal)
                comp.tickListNormal.DeregisterThing(t);
            else if (t.def.tickerType == TickerType.Rare)
                comp.tickListRare.DeregisterThing(t);
            else if (t.def.tickerType == TickerType.Long)
                comp.tickListLong.DeregisterThing(t);
        }
    }

    [HarmonyPatch(typeof(TickList), nameof(TickList.Tick))]
    public static class TickListTickPatch
    {
        static bool Prefix()
        {
            return Multiplayer.client == null || MapAsyncTimeComp.tickingMap;
        }
    }

    [HarmonyPatch(typeof(TimeControls), nameof(TimeControls.DoTimeControlsGUI))]
    public static class MapTimeControl
    {
        static void Prefix(ref Container<TimeSpeed> __state)
        {
            if (Multiplayer.client == null || WorldRendererUtility.WorldRenderedNow || Find.VisibleMap == null) return;

            // remember the state
            __state = Find.TickManager.CurTimeSpeed;

            MapAsyncTimeComp comp = Find.VisibleMap.GetComponent<MapAsyncTimeComp>();
            Find.TickManager.CurTimeSpeed = comp.timeSpeed;
        }

        static void Postfix(Rect timerRect, Container<TimeSpeed> __state)
        {
            if (__state == null) return;

            Map map = Find.VisibleMap;
            MapAsyncTimeComp comp = map.GetComponent<MapAsyncTimeComp>();
            if (Find.TickManager.CurTimeSpeed != comp.lastSpeed)
            {
                Multiplayer.client.SendCommand(CommandType.MAP_TIME_SPEED, map.uniqueID, (byte)Find.TickManager.CurTimeSpeed);
            }

            // restore the state
            Find.TickManager.CurTimeSpeed = __state.Value;
        }
    }

    public static class SetMapTime
    {
        static void Prefix(ref Container<int, TimeSpeed> __state)
        {
            if (Multiplayer.client == null || WorldRendererUtility.WorldRenderedNow || Find.VisibleMap == null) return;

            __state = new Container<int, TimeSpeed>(Find.TickManager.TicksGame, Find.TickManager.CurTimeSpeed);
            MapAsyncTimeComp comp = Find.VisibleMap.GetComponent<MapAsyncTimeComp>();
            Find.TickManager.DebugSetTicksGame(comp.mapTicks);
            Find.TickManager.CurTimeSpeed = comp.timeSpeed;
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
        public static readonly Texture2D[] SpeedButtonTextures = new Texture2D[]
        {
            ContentFinder<Texture2D>.Get("UI/TimeControls/TimeSpeedButton_Pause", true),
            ContentFinder<Texture2D>.Get("UI/TimeControls/TimeSpeedButton_Normal", true),
            ContentFinder<Texture2D>.Get("UI/TimeControls/TimeSpeedButton_Fast", true),
            ContentFinder<Texture2D>.Get("UI/TimeControls/TimeSpeedButton_Superfast", true),
            ContentFinder<Texture2D>.Get("UI/TimeControls/TimeSpeedButton_Superfast", true)
        };

        static MethodInfo groupFrameRect = AccessTools.Method(typeof(ColonistBarColonistDrawer), "GroupFrameRect");

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
                Rect rect = (Rect)groupFrameRect.Invoke(bar.drawer, new object[] { entry.group });
                Rect button = new Rect(rect.x - TimeControls.TimeButSize.x / 2f, rect.yMax - TimeControls.TimeButSize.y / 2f, TimeControls.TimeButSize.x, TimeControls.TimeButSize.y);
                Widgets.DrawRectFast(button, new Color(0.5f, 0.5f, 0.5f, 0.4f * alpha));
                Widgets.ButtonImage(button, SpeedButtonTextures[(int)comp.timeSpeed]);

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
            return Multiplayer.client == null || !TickPatch.tickingWorld;
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

    public class MapAsyncTimeComp : MapComponent
    {
        public static bool tickingMap;

        public float CurTimePerTick
        {
            get
            {
                if (TickRateMultiplier == 0f)
                    return 0f;
                return 1f / (60f * TickRateMultiplier);
            }
        }

        public float TickRateMultiplier
        {
            get
            {
                if (timeSpeed == TimeSpeed.Paused)
                    return 0;
                if (forcedNormalSpeed)
                    return 1;
                if (timeSpeed == TimeSpeed.Fast)
                    return 3;
                if (timeSpeed == TimeSpeed.Superfast)
                    return 6;
                return 1;
            }
        }

        public int Timer => (int)timerInt;

        public Queue<ScheduledCommand> scheduledCmds = new Queue<ScheduledCommand>();

        public double timerInt;
        public int mapTicks;
        public TimeSpeed lastSpeed = TimeSpeed.Paused;
        public TimeSpeed timeSpeed = TimeSpeed.Paused;
        public float realTimeToTickThrough;
        public bool forcedNormalSpeed;

        public Storyteller storyteller;

        public TickList tickListNormal = new TickList(TickerType.Normal);
        public TickList tickListRare = new TickList(TickerType.Rare);
        public TickList tickListLong = new TickList(TickerType.Long);

        static MethodInfo skyTargetMethod = typeof(SkyManager).GetMethod("CurrentSkyTarget", BindingFlags.NonPublic | BindingFlags.Instance);
        static FieldInfo curSkyGlowField = typeof(SkyManager).GetField("curSkyGlowInt", BindingFlags.NonPublic | BindingFlags.Instance);

        public MapAsyncTimeComp(Map map) : base(map)
        {
            storyteller = new Storyteller(StorytellerDefOf.Cassandra, DifficultyDefOf.Medium);
        }

        public void Tick()
        {
            tickingMap = true;

            PreContext();

            float tickRate = TickRateMultiplier;

            SimpleProfiler.Start();

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

            while (scheduledCmds.Count > 0 && scheduledCmds.Peek().ticks == Timer)
            {
                ScheduledCommand cmd = scheduledCmds.Dequeue();
                OnMainThread.ExecuteMapCmd(cmd, new ByteReader(cmd.data));
            }

            PostContext();

            SimpleProfiler.Pause();
            if (mapTicks % 300 == 0)
            {
                SimpleProfiler.Print("profiler_" + Multiplayer.username + "_tick.txt");
                SimpleProfiler.Init(Multiplayer.username);

                map.GetComponent<MultiplayerMapComp>().SetFaction(map.ParentFaction);
                byte[] mapData = ScribeUtil.WriteExposable(map, "map", true);
                File.WriteAllBytes("map_0_" + Multiplayer.username + ".xml", mapData);
                map.GetComponent<MultiplayerMapComp>().SetFaction(Multiplayer.RealPlayerFaction);
            }

            if (tickRate >= 1)
                timerInt += 1f / tickRate;

            tickingMap = false;
        }

        private int worldTicks;
        private TimeSpeed worldSpeed;
        private Storyteller globalStoryteller;

        public void PreContext()
        {
            worldTicks = Find.TickManager.TicksGame;
            worldSpeed = Find.TickManager.CurTimeSpeed;
            Find.TickManager.DebugSetTicksGame(mapTicks);
            Find.TickManager.CurTimeSpeed = timeSpeed;

            globalStoryteller = Current.Game.storyteller;
            Current.Game.storyteller = storyteller;
            StorytellerTargetsPatch.target = map;

            UniqueIdsPatch.CurrentBlock = map.GetComponent<MultiplayerMapComp>().mapIdBlock;

            Multiplayer.Seed = map.uniqueID.Combine(mapTicks).Combine(Multiplayer.WorldComp.sessionId);

            // Reset the effects of SkyManagerUpdate called during Update
            SkyTarget target = (SkyTarget)skyTargetMethod.Invoke(map.skyManager, new object[0]);
            curSkyGlowField.SetValue(map.skyManager, target.glow);
        }

        public void PostContext()
        {
            UniqueIdsPatch.CurrentBlock = null;

            Current.Game.storyteller = globalStoryteller;
            StorytellerTargetsPatch.target = null;

            Find.TickManager.DebugSetTicksGame(worldTicks);
            Find.TickManager.CurTimeSpeed = worldSpeed;
        }

        public void ExecuteMapCmdsWhilePaused()
        {
            PreContext();

            while (scheduledCmds.Count > 0 && timeSpeed == TimeSpeed.Paused)
            {
                ScheduledCommand cmd = scheduledCmds.Dequeue();
                OnMainThread.ExecuteMapCmd(cmd, new ByteReader(cmd.data));
            }

            PostContext();
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref mapTicks, "mapTicks");
            Scribe_Values.Look(ref timerInt, "timer");
            Scribe_Values.Look(ref timeSpeed, "timeSpeed");
        }

        public void SetTimeSpeed(TimeSpeed speed)
        {
            timeSpeed = speed;
            lastSpeed = speed;
        }
    }
}
