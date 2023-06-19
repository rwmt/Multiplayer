using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client.Saving;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public static class ReplayTimeline
{
    const float TimelineMargin = 50f;
    const float TimelineHeight = 35f;
    const int TimelineWindowId = 5723681;

    internal static void DrawTimeline()
    {
        Rect rect = new Rect(TimelineMargin, UI.screenHeight - 35f - TimelineHeight - 10f - 30f,
            UI.screenWidth - TimelineMargin * 2, TimelineHeight + 30f);

        Find.WindowStack.ImmediateWindow(TimelineWindowId, rect, WindowLayer.SubSuper, DrawTimelineWindow,
            doBackground: false, shadowAlpha: 0);
    }

    private static void DrawTimelineWindow()
    {
        Rect rect = new Rect(0, 30f, UI.screenWidth - TimelineMargin * 2, TimelineHeight);

        Widgets.DrawBoxSolid(rect, new Color(0.6f, 0.6f, 0.6f, 0.8f));

        int timerStart = Multiplayer.session.replayTimerStart >= 0
            ? Multiplayer.session.replayTimerStart
            : Multiplayer.session.dataSnapshot.CachedAtTime;

        int timerEnd = Multiplayer.session.replayTimerEnd >= 0
            ? Multiplayer.session.replayTimerEnd
            : TickPatch.tickUntil;

        int timeLen = timerEnd - timerStart;

        MpUI.DrawRotatedLine(new Vector2(rect.xMin + 2f, rect.center.y), TimelineHeight, 20f, 90f, Color.white);
        MpUI.DrawRotatedLine(new Vector2(rect.xMax - 2f, rect.center.y), TimelineHeight, 20f, 90f, Color.white);

        float progress = (TickPatch.Timer - timerStart) / (float)timeLen;
        float progressX = rect.xMin + progress * rect.width;
        MpUI.DrawRotatedLine(new Vector2((int)progressX, rect.center.y), TimelineHeight, 20f, 90f, Color.green);

        float mouseX = Event.current.mousePosition.x;
        ReplayEvent mouseEvent = null;

        foreach (var ev in Multiplayer.session.events)
        {
            if (ev.time < timerStart || ev.time > timerEnd)
                continue;

            var pointX = rect.xMin + (ev.time - timerStart) / (float)timeLen * rect.width;

            //GUI.DrawTexture(new Rect(pointX - 12f, rect.yMin - 24f, 24f, 24f), texture);
            MpUI.DrawRotatedLine(new Vector2(pointX, rect.center.y), TimelineHeight, 20f, 90f, /*ev.color*/ Color.red);

            if (Mouse.IsOver(rect) && Math.Abs(mouseX - pointX) < 10)
            {
                mouseX = pointX;
                mouseEvent = ev;
            }
        }

        // Draw mouse pointer and tooltip
        if (Mouse.IsOver(rect))
        {
            float mouseProgress = (mouseX - rect.xMin) / rect.width;
            int mouseTimer = timerStart + (int)(timeLen * mouseProgress);

            MpUI.DrawRotatedLine(new Vector2(mouseX, rect.center.y), TimelineHeight, 15f, 90f, Color.blue);

            if (Event.current.type == EventType.MouseUp)
                SimulateToTick(mouseTimer);

            if (Event.current.isMouse)
                Event.current.Use();

            string tooltip = $"Tick {mouseTimer}";
            if (mouseEvent != null)
                tooltip = $"{mouseEvent.name}\n{tooltip}";

            const int tickTipId = 215462143;

            TooltipHandler.TipRegion(rect, new TipSignal(tooltip, tickTipId));

            // Remove delay between the mouseover and showing
            if (TooltipHandler.activeTips.TryGetValue(tickTipId, out ActiveTip tip))
                tip.firstTriggerTime = 0;
        }

        // Draw simulation target when simulating
        if (TickPatch.Simulating)
        {
            float pct = (TickPatch.simulating.target.Value - timerStart) / (float)timeLen;
            float simulateToX = rect.xMin + rect.width * pct;
            MpUI.DrawRotatedLine(new Vector2(simulateToX, rect.center.y), TimelineHeight, 15f, 90f, Color.yellow);
        }
    }

    private static void SimulateToTick(int targetTick)
    {
        TickPatch.SetSimulation(targetTick, canESC: true);

        if (targetTick < TickPatch.Timer)
        {
            Loader.ReloadGame(
                Multiplayer.session.dataSnapshot.MapData.Keys.ToList(),
                false,
                Multiplayer.GameComp.asyncTime
            );
        }
    }
}

[HarmonyPatch]
static class MakeSpaceForReplayTimeline
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI));
        yield return AccessTools.Method(typeof(GlobalControls), nameof(GlobalControls.GlobalControlsOnGUI));
        yield return AccessTools.Method(typeof(WorldGlobalControls), nameof(WorldGlobalControls.WorldGlobalControlsOnGUI));
    }

    static void Prefix()
    {
        if (Multiplayer.IsReplay)
            UI.screenHeight -= 60;
    }

    static void Postfix()
    {
        if (Multiplayer.IsReplay)
            UI.screenHeight += 60;
    }
}
