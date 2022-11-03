using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.AsyncTime;

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

    static void Prefix(ref ITickable __state)
    {
        if (Multiplayer.Client == null) return;
        if (!WorldRendererUtility.WorldRenderedNow && Find.CurrentMap == null) return;

        ITickable tickable = Multiplayer.WorldComp;
        if (!WorldRendererUtility.WorldRenderedNow && Multiplayer.GameComp.asyncTime)
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
        else if (normalSpeed == fastSpeed) // Slowed down
            Widgets.DrawLineHorizontal(btn.x + btn.width * 2f, btn.y + btn.height / 2f, btn.width * 2f);

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

            TickPatch.Tick(out _);

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
            if (curGroup == entry.group) continue;

            ITickable entryTickable = entry.map?.AsyncTime();
            if (entryTickable == null) entryTickable = Multiplayer.WorldComp;

            Rect groupBar = bar.drawer.GroupFrameRect(entry.group);
            float drawXPos = groupBar.x;
            Color bgColor = (entryTickable.ActualRateMultiplier(TimeSpeed.Normal) == 0f) ? pauseBgColor : normalBgColor;

            if (Multiplayer.GameComp.asyncTime)
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
                DrawWindowShortcuts(new Rect(drawXPos, groupBar.yMax, 70, btnHeight), bgColor, options);

            curGroup = entry.group;
        }
    }

    static void DrawWindowShortcuts(Rect button, Color bgColor, List<FloatMenuOption> options)
    {
        Widgets.DrawRectFast(button, bgColor);

        using (MpStyle.Set(GameFont.Tiny))
            if (Widgets.ButtonText(button, "MpDialogsButton".Translate()))
                Find.WindowStack.Add(new FloatMenu(options));
    }

    static List<FloatMenuOption> GetBlockingWindowOptions(ColonistBar.Entry entry, ITickable tickable)
    {
        List<FloatMenuOption> options = new List<FloatMenuOption>();
        var split = Multiplayer.WorldComp.splitSession;

        if (split != null && split.Caravan.pawns.Contains(entry.pawn))
        {
            options.Add(new FloatMenuOption("MpCaravanSplittingSession".Translate(), () =>
            {
                SwitchMap(entry.map);
                CameraJumper.TryJumpAndSelect(entry.pawn);
                Multiplayer.WorldComp.splitSession.OpenWindow();
            }));
        }

        if (Multiplayer.WorldComp.trading.FirstOrDefault(t => t.playerNegotiator?.Map == entry.map) is MpTradeSession
            trade)
        {
            options.Add(new FloatMenuOption("MpTradingSession".Translate(), () =>
            {
                SwitchMap(entry.map);
                CameraJumper.TryJumpAndSelect(trade.playerNegotiator);
                Find.WindowStack.Add(new TradingWindow()
                    { selectedTab = Multiplayer.WorldComp.trading.IndexOf(trade) });
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

        if (entry.map?.MpComp().ritualSession != null)
        {
            options.Add(new FloatMenuOption("MpRitualSession".Translate(), () =>
            {
                SwitchMap(entry.map);
                entry.map.MpComp().ritualSession.OpenWindow();
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
            Log.Message($"{Current.Game.CurrentMap} {map}");
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
        if (!Multiplayer.GameComp.asyncTime) return;

        Rect button = new Rect(rect.xMax - TimeControls.TimeButSize.x - 5f,
            rect.y + (rect.height - TimeControls.TimeButSize.y) / 2f, TimeControls.TimeButSize.x,
            TimeControls.TimeButSize.y);
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
