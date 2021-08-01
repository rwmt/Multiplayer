using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.AsyncTime
{
    [HarmonyPatch]
    static class CancelMapManagersTick
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Map), nameof(Map.MapPreTick));
            yield return AccessTools.Method(typeof(Map), nameof(Map.MapPostTick));
        }

        static bool Prefix() => Multiplayer.Client == null || AsyncTimeComp.tickingMap != null;
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

    [HarmonyPatch]
    static class CancelMapManagersUpdate
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PowerNetManager), nameof(PowerNetManager.UpdatePowerNetsAndConnections_First));
            yield return AccessTools.Method(typeof(GlowGrid), nameof(GlowGrid.GlowGridUpdate_First));
            yield return AccessTools.Method(typeof(RegionGrid), nameof(RegionGrid.UpdateClean));
            yield return AccessTools.Method(typeof(RegionAndRoomUpdater), nameof(RegionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms));
        }

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
            if (Multiplayer.Client == null || t.Map == null) return true;

            AsyncTimeComp comp = t.Map.AsyncTime();
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
            if (Multiplayer.Client == null || t.Map == null) return true;

            AsyncTimeComp comp = t.Map.AsyncTime();
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
    static class TimeControlsMarker
    {
        public static bool drawingTimeControls;

        static void Prefix() => drawingTimeControls = true;
        static void Postfix() => drawingTimeControls = false;
    }

    [HotSwappable]
    [HarmonyPatch(typeof(TimeControls), nameof(TimeControls.DoTimeControlsGUI))]
    public static class TimeControlPatch
    {
        private static TimeSpeed prevSpeed;
        private static TimeSpeed savedSpeed;
        private static bool keyPressed;

        public static int? tickableToHighlight;

        static void Prefix(ref ITickable __state)
        {
            if (Multiplayer.Client == null) return;
            if (!WorldRendererUtility.WorldRenderedNow && Find.CurrentMap == null) return;

            ITickable tickable = Multiplayer.WorldComp;
            if (!WorldRendererUtility.WorldRenderedNow && MultiplayerWorldComp.asyncTime)
                tickable = Find.CurrentMap.AsyncTime();

            TimeSpeed speed = tickable.TimeSpeed;
            if (Multiplayer.IsReplay)
                speed = TickPatch.replayTimeSpeed;

            savedSpeed = Find.TickManager.CurTimeSpeed;

            Find.TickManager.CurTimeSpeed = speed;
            prevSpeed = speed;
            keyPressed = Event.current.isKey;
            __state = tickable;
        }

        static void Postfix(ITickable __state, Rect timerRect)
        {
            if (__state == null) return;

            Rect btn = new Rect(timerRect.x, timerRect.y, TimeControls.TimeButSize.x, TimeControls.TimeButSize.y);
            float normalSpeed = __state.ActualRateMultiplier(TimeSpeed.Normal);
            float fastSpeed = __state.ActualRateMultiplier(TimeSpeed.Fast);

            if (normalSpeed == 0f) // Completely paused
                Widgets.DrawLineHorizontal(btn.x + btn.width, btn.y + btn.height / 2f, btn.width * 3f);
            else if (normalSpeed == fastSpeed)  // Slowed down
                Widgets.DrawLineHorizontal(btn.x + btn.width * 2f, btn.y + btn.height / 2f, btn.width * 2f);

            if (Mouse.IsOver(btn.Width(btn.width * 4f)) && Event.current.type == EventType.Repaint)
            {
                var tickable = WorldRendererUtility.WorldRenderedNow ? Multiplayer.WorldComp : (ITickable)Find.CurrentMap.AsyncTime();
                if (tickable != null)
                    tickableToHighlight =
                        tickable.ActualRateMultiplier(TimeSpeed.Normal) == 0f ? tickable.TickableId : (int?)null;
            }

            TimeSpeed newSpeed = Find.TickManager.CurTimeSpeed;
            Find.TickManager.CurTimeSpeed = savedSpeed;

            if (prevSpeed == newSpeed) return;

            if (Multiplayer.IsReplay)
                TickPatch.replayTimeSpeed = newSpeed;

            // Prevent multiple players changing the speed too quickly
            if (keyPressed && Time.realtimeSinceStartup - MultiplayerWorldComp.lastSpeedChange < 0.4f)
                return;

            TimeControl.SendTimeChange(__state, newSpeed);
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    static class DoSingleTickShortcut
    {
        static bool Prefix()
        {
            if (Multiplayer.Client == null || !TimeControlsMarker.drawingTimeControls)
                return true;

            if (TickPatch.Timer < TickPatch.tickUntil)
            {
                var replaySpeed = TickPatch.replayTimeSpeed;
                TickPatch.replayTimeSpeed = TimeSpeed.Normal;
                TickPatch.accumulator = 1;

                TickPatch.Tick();

                TickPatch.accumulator = 0;
                TickPatch.replayTimeSpeed = replaySpeed;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ShowGroupFrames), MethodType.Getter)]
    static class AlwaysShowColonistBarFrames
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client == null) return;
            __result = true;
        }
    }

    [HotSwappable]
    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    public static class ColonistBarTimeControl
    {
        public static Color normalBgColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
        public static Color pauseBgColor = new Color(1f, 0.5f, 0.5f, 0.4f);
        public static float btnWidth = TimeControls.TimeButSize.x;
        public static float btnHeight = TimeControls.TimeButSize.y;

        static void Prefix(ref bool __state)
        {
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)
            {
                DrawButtons();
                __state = true;
            }
        }

        static void Postfix(bool __state)
        {
            if (!__state)
                DrawButtons();
        }

        static void DrawButtons()
        {
            if (Multiplayer.Client == null) return;

            ColonistBar bar = Find.ColonistBar;
            if (bar.Entries.Count == 0) return;

            int curGroup = -1;
            foreach (var entry in bar.Entries)
            {
                if (entry.pawn == null || entry.pawn.Dead || curGroup == entry.group) continue;

                ITickable entryTickable = entry.map?.AsyncTime();
                if (entryTickable == null) entryTickable = Multiplayer.WorldComp;

                Rect groupBar = bar.drawer.GroupFrameRect(entry.group);
                float drawXPos = groupBar.x;
                Color bgColor = (entryTickable.ActualRateMultiplier(TimeSpeed.Normal) == 0f) ? pauseBgColor : normalBgColor;

                if (MultiplayerWorldComp.asyncTime)
                {
                    Rect button = new Rect(drawXPos, groupBar.yMax, btnWidth, btnHeight);

                    if (entry.map != null)
                    {
                        TimeControl.TimeControlButton(button, bgColor, entryTickable);
                        drawXPos += TimeControls.TimeButSize.x;
                    }
                    else if (entryTickable.ActualRateMultiplier(TimeSpeed.Normal) == 0f)
                    {
                        TimeControl.TimeIndicateBlockingPause(button, bgColor);
                        drawXPos += TimeControls.TimeButSize.x;
                    }
                }
                else if (entryTickable.TickRateMultiplier(TimeSpeed.Normal) == 0f)
                {
                    Rect button = new Rect(drawXPos, groupBar.yMax, btnWidth, btnHeight);
                    TimeControl.TimeIndicateBlockingPause(button, bgColor);
                    drawXPos += TimeControls.TimeButSize.x;
                }

                List<FloatMenuOption> options = GetBlockingWindowOptions(entry, entryTickable);
                if (!options.NullOrEmpty())
                {
                    Rect button = new Rect(drawXPos, groupBar.yMax, btnWidth, btnHeight);
                    DrawWindowShortcuts(button, bgColor, options);

                    if (TimeControlPatch.tickableToHighlight != null && Event.current.type == EventType.Repaint)
                    {
                        if (TimeControlPatch.tickableToHighlight.Value == entryTickable.TickableId && ((int)(Time.time * 3)) % 2 == 0)
                            Widgets.DrawBox(button, 2);
                        TimeControlPatch.tickableToHighlight = null;
                    }
                }

                curGroup = entry.group;
            }
        }

        static void DrawWindowShortcuts(Rect button, Color bgColor, List<FloatMenuOption> options)
        {
            Widgets.DrawRectFast(button, bgColor);

            if (Widgets.ButtonImage(button, TexButton.OpenStatsReport))
            {
                if (options.Count == 1)
                    options[0].action();
                else
                    Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        static List<FloatMenuOption> GetBlockingWindowOptions(ColonistBar.Entry entry, ITickable tickable)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            var split = Multiplayer.WorldComp.splitSession;

            if (split != null && split.Caravan.pawns.Contains(entry.pawn) == true)
            {
                options.Add(new FloatMenuOption("MpCaravanSplittingSession".Translate(), () =>
                {
                    SwitchMap(entry.map);
                    CameraJumper.TryJumpAndSelect(entry.pawn);
                    Multiplayer.WorldComp.splitSession.OpenWindow();
                }));
            }

            if (Multiplayer.WorldComp.trading.FirstOrDefault(t => t.playerNegotiator?.Map == entry.map) is MpTradeSession trade)
            {
                options.Add(new FloatMenuOption("MpTradingSession".Translate(), () =>
                {
                    SwitchMap(entry.map);
                    CameraJumper.TryJumpAndSelect(trade.playerNegotiator);
                    Find.WindowStack.Add(new TradingWindow() { selectedTab = Multiplayer.WorldComp.trading.IndexOf(trade) });
                }));
            }

            if (entry.map?.MpComp().transporterLoading != null)
            {
                options.Add(new FloatMenuOption("MpTransportLoadingSession".Translate(), () =>
                {
                    SwitchMap(entry.map);
                    entry.map.MpComp().transporterLoading.OpenWindow();
                }));
            }

            if (entry.map?.MpComp().caravanForming != null)
            {
                options.Add(new FloatMenuOption("MpCaravanFormingSession".Translate(), () =>
                {
                    SwitchMap(entry.map);
                    entry.map.MpComp().caravanForming.OpenWindow();
                }));
            }

            return options;
        }

        static void SwitchMap(Map map)
        {
            if (map == null)
            {
                Find.World.renderer.wantedMode = WorldRenderMode.Planet;
            }
            else
            {
                if (WorldRendererUtility.WorldRenderedNow) CameraJumper.TryHideWorld();
                Current.Game.CurrentMap = map;
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
            if (!MultiplayerWorldComp.asyncTime) return;

            Rect button = new Rect(rect.xMax - TimeControls.TimeButSize.x - 5f, rect.y + (rect.height - TimeControls.TimeButSize.y) / 2f, TimeControls.TimeButSize.x, TimeControls.TimeButSize.y);
            __state = button;

            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)
                TimeControl.TimeControlButton(__state.Value, ColonistBarTimeControl.normalBgColor, Multiplayer.WorldComp);
        }

        static void Postfix(MainButtonWorker __instance, Rect? __state)
        {
            if (__state == null) return;

            if (Event.current.type == EventType.Repaint)
                TimeControl.TimeControlButton(__state.Value, ColonistBarTimeControl.normalBgColor, Multiplayer.WorldComp);
        }
    }

    static class TimeControl
    {
        public static void TimeIndicateBlockingPause(Rect button, Color bgColor)
        {
            Widgets.DrawRectFast(button, bgColor);
            Widgets.ButtonImage(button, TexButton.SpeedButtonTextures[0], doMouseoverSound: false);
        }

        public static void TimeControlButton(Rect button, Color bgColor, ITickable tickable)
        {
            int speed = (int)tickable.TimeSpeed;
            if (tickable.ActualRateMultiplier(TimeSpeed.Normal) == 0f)
                speed = 0;
            Widgets.DrawRectFast(button, bgColor);
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
            else if (tickable is AsyncTimeComp comp)
                Multiplayer.Client.SendCommand(CommandType.MapTimeSpeed, comp.map.uniqueID, (byte)newSpeed);
        }
    }

    [HarmonyPatch(typeof(PawnTweener), nameof(PawnTweener.PreDrawPosCalculation))]
    static class PreDrawCalcMarker
    {
        public static Pawn calculating;

        static void Prefix(PawnTweener __instance) => calculating = __instance.pawn;
        static void Postfix() => calculating = null;
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickRateMultiplier), MethodType.Getter)]
    static class TickRateMultiplierPatch
    {
        static void Postfix(ref float __result)
        {
            if (PreDrawCalcMarker.calculating == null) return;
            if (Multiplayer.Client == null) return;
            if (WorldRendererUtility.WorldRenderedNow) return;

            var map = PreDrawCalcMarker.calculating.Map ?? Find.CurrentMap;
            var asyncTime = map.AsyncTime();
            var timeSpeed = Multiplayer.IsReplay ? TickPatch.replayTimeSpeed : asyncTime.TimeSpeed;

            __result = TickPatch.Skipping ? 6 : asyncTime.ActualRateMultiplier(timeSpeed);
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.Paused), MethodType.Getter)]
    static class TickManagerPausedPatch
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client == null) return;
            if (WorldRendererUtility.WorldRenderedNow) return;

            var asyncTime = Find.CurrentMap.AsyncTime();
            var timeSpeed = Multiplayer.IsReplay ? TickPatch.replayTimeSpeed : asyncTime.TimeSpeed;

            __result = asyncTime.ActualRateMultiplier(timeSpeed) == 0;
        }
    }

    [HarmonyPatch]
    public class StorytellerTickPatch
    {
        public static bool updating;
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Storyteller), nameof(Storyteller.StorytellerTick));
            yield return AccessTools.Method(typeof(StoryWatcher), nameof(StoryWatcher.StoryWatcherTick));
        }

        static bool Prefix()
        {
            updating = true;
            return Multiplayer.Client == null || Multiplayer.Ticking;
        }

        static void Postfix() => updating = false;
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

    // The MP Mod's ticker calls Storyteller.StorytellerTick() on both the World and each Map, each tick
    // This patch aims to ensure each "spawn raid" Quest is only triggered once, to prevent 2x or 3x sized raids
    [HarmonyPatch(typeof(Quest), nameof(Quest.PartsListForReading), MethodType.Getter)]
    public class QuestPartsListForReadingPatch
    {
        static void Postfix(ref List<QuestPart> __result)
        {
            if (StorytellerTickPatch.updating)
            {
                __result = __result.Where(questPart => {
                    if (questPart is QuestPart_ThreatsGenerator questPartThreatsGenerator)
                    {
                        return questPartThreatsGenerator?.mapParent?.Map == Multiplayer.MapContext;
                    }
                    return true;
                }).ToList();
            }
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.Notify_GeneratedPotentiallyHostileMap))]
    static class GeneratedHostileMapPatch
    {
        static bool Prefix() => Multiplayer.Client == null;

        static void Postfix()
        {
            if (Multiplayer.Client == null) return;

            // The newly generated map
            Find.Maps.LastOrDefault()?.AsyncTime().slower.SignalForceNormalSpeedShort();
        }
    }

    [HarmonyPatch(typeof(StorytellerUtility), nameof(StorytellerUtility.DefaultParmsNow))]
    static class MapContextIncidentParms
    {
        static void Prefix(IIncidentTarget target, ref Map __state)
        {
            // This may be running inside a context already
            if (AsyncTimeComp.tickingMap != null)
                return;

            if (MultiplayerWorldComp.tickingWorld && target is Map map)
            {
                AsyncTimeComp.tickingMap = map;
                map.AsyncTime().PreContext();
                __state = map;
            }
        }

        static void Postfix(Map __state)
        {
            if (__state != null)
            {
                __state.AsyncTime().PostContext();
                AsyncTimeComp.tickingMap = null;
            }
        }
    }

    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    static class MapContextIncidentExecute
    {
        static void Prefix(IncidentParms parms, ref Map __state)
        {
            if (MultiplayerWorldComp.tickingWorld && parms.target is Map map)
            {
                AsyncTimeComp.tickingMap = map;
                map.AsyncTime().PreContext();
                __state = map;
            }
        }

        static void Postfix(Map __state)
        {
            if (__state != null)
            {
                __state.AsyncTime().PostContext();
                AsyncTimeComp.tickingMap = null;
            }
        }
    }
}
