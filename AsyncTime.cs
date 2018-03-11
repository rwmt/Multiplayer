using Harmony;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer
{
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickManagerUpdate))]
    public static class TickManagerUpdatePatch
    {
        static void Prefix()
        {
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                AsyncTimeMapComp comp = maps[i].GetComponent<AsyncTimeMapComp>();

                if (comp.timeSpeed == TimeSpeed.Paused) continue;

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
        }

        static void Postfix()
        {
            if (Find.VisibleMap != null)
            {
                AsyncTimeMapComp comp = Find.VisibleMap.GetComponent<AsyncTimeMapComp>();
                Shader.SetGlobalFloat(ShaderPropertyIDs.GameSeconds, comp.mapTicks.TicksToSeconds());
            }
        }
    }

    [HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
    public static class MapUpdatePatch
    {
        static void Prefix(Map __instance, ref Container<int, TimeSpeed> __state)
        {
            __state = new Container<int, TimeSpeed>(Find.TickManager.TicksGame, Find.TickManager.CurTimeSpeed);

            AsyncTimeMapComp comp = __instance.GetComponent<AsyncTimeMapComp>();
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
            return AsyncTimeMapComp.tickingMap;
        }
    }

    [HarmonyPatch(typeof(Map), nameof(Map.MapPostTick))]
    public static class CancelMapPostTick
    {
        static bool Prefix()
        {
            return AsyncTimeMapComp.tickingMap;
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.RegisterAllTickabilityFor))]
    public static class TickListAdd
    {
        static bool Prefix(Thing t)
        {
            AsyncTimeMapComp comp = t.Map.GetComponent<AsyncTimeMapComp>();
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
            AsyncTimeMapComp comp = t.Map.GetComponent<AsyncTimeMapComp>();
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
    public static class DoMapTimeControls
    {
        static void Prefix(ref Container<TimeSpeed> __state)
        {
            if (WorldRendererUtility.WorldRenderedNow || Find.VisibleMap == null) return;

            AsyncTimeMapComp comp = Find.VisibleMap.GetComponent<AsyncTimeMapComp>();
            __state = Find.TickManager.CurTimeSpeed;

            if (Event.current.type == EventType.repaint)
                Find.TickManager.CurTimeSpeed = comp.timeSpeed;
            else
                Find.TickManager.CurTimeSpeed = comp.requestedSpeed;
        }

        static void Postfix(Rect timerRect, Container<TimeSpeed> __state)
        {
            if (__state == null) return;

            AsyncTimeMapComp comp = Find.VisibleMap.GetComponent<AsyncTimeMapComp>();
            if (Event.current.type != EventType.repaint && Find.TickManager.CurTimeSpeed != comp.requestedSpeed)
            {
                comp.requestedSpeed = Find.TickManager.CurTimeSpeed;
                comp.timeSpeed = comp.requestedSpeed;
            }

            // render the requested time indicator
            GUI.BeginGroup(timerRect);
            Widgets.DrawLineHorizontal(TimeControls.TimeButSize.x * (int)comp.requestedSpeed + 5, TimeControls.TimeButSize.y - 2, TimeControls.TimeButSize.x - 10);
            GUI.EndGroup();

            Find.TickManager.CurTimeSpeed = __state.Value;
        }
    }

    public static class SetMapTime
    {
        static void Prefix(ref Container<int, TimeSpeed> __state)
        {
            if (Find.VisibleMap == null || WorldRendererUtility.WorldRenderedNow) return;
            __state = new Container<int, TimeSpeed>(Find.TickManager.TicksGame, Find.TickManager.CurTimeSpeed);
            AsyncTimeMapComp comp = Find.VisibleMap.GetComponent<AsyncTimeMapComp>();
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
            ColonistBar bar = Find.ColonistBar;
            if (bar.Entries.Count == 0 || bar.Entries[bar.Entries.Count - 1].group == 0) return;

            int curGroup = -1;
            foreach (ColonistBar.Entry entry in bar.Entries)
            {
                if (entry.map == null || curGroup == entry.group) continue;

                float alpha = 1.0f;
                if (entry.map != Find.VisibleMap || WorldRendererUtility.WorldRenderedNow)
                    alpha = 0.75f;

                AsyncTimeMapComp comp = entry.map.GetComponent<AsyncTimeMapComp>();
                Rect rect = (Rect)groupFrameRect.Invoke(bar.drawer, new object[] { entry.group });
                Rect button = new Rect(rect.x - TimeControls.TimeButSize.x / 2f, rect.yMax - TimeControls.TimeButSize.y / 2f, TimeControls.TimeButSize.x, TimeControls.TimeButSize.y);
                Widgets.DrawRectFast(button, new Color(0.5f, 0.5f, 0.5f, 0.4f * alpha));
                Widgets.ButtonImage(button, SpeedButtonTextures[(int)comp.timeSpeed]);

                curGroup = entry.group;
            }
        }
    }

    public class AsyncTimeMapComp : MapComponent
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

        public int mapTicks;
        public TimeSpeed timeSpeed = TimeSpeed.Normal;
        public float realTimeToTickThrough;
        public bool forcedNormalSpeed;

        public TimeSpeed requestedSpeed = TimeSpeed.Normal;

        public TickList tickListNormal = new TickList(TickerType.Normal);
        public TickList tickListRare = new TickList(TickerType.Rare);
        public TickList tickListLong = new TickList(TickerType.Long);

        public AsyncTimeMapComp(Map map) : base(map)
        {
        }

        public void Tick()
        {
            tickingMap = true;

            int worldTicks = Find.TickManager.TicksGame;
            TimeSpeed worldSpeed = Find.TickManager.CurTimeSpeed;
            Find.TickManager.DebugSetTicksGame(mapTicks);
            Find.TickManager.CurTimeSpeed = timeSpeed;

            map.MapPreTick();
            mapTicks++;
            Find.TickManager.DebugSetTicksGame(mapTicks);

            tickListNormal.Tick();
            tickListRare.Tick();
            tickListLong.Tick();

            map.MapPostTick();

            Find.TickManager.DebugSetTicksGame(worldTicks);
            Find.TickManager.CurTimeSpeed = worldSpeed;
            tickingMap = false;
        }
    }
}
