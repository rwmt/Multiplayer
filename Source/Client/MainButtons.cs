using Harmony;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(MainButtonsRoot), nameof(MainButtonsRoot.MainButtonsOnGUI))]
    [HotSwappable]
    public static class MainButtonsPatch
    {
        static bool Prefix()
        {
            Text.Font = GameFont.Small;

            DoDebugInfo();

            if (Multiplayer.IsReplay || TickPatch.skipTo >= 0)
            {
                DrawTimeline();
                DrawSkippingWindow();
            }

            DoButtons();

            return Find.Maps.Count > 0;
        }

        static void DoDebugInfo()
        {
            if (MpVersion.IsDebug && Multiplayer.Client != null)
            {
                int timerLag = (TickPatch.tickUntil - TickPatch.Timer);
                string text = $"{Find.TickManager.TicksGame} {TickPatch.Timer} {TickPatch.tickUntil} {timerLag} {Time.deltaTime * 60f}";
                Rect rect = new Rect(80f, 60f, 330f, Text.CalcHeight(text, 330f));
                Widgets.Label(rect, text);
            }

            if (MpVersion.IsDebug && Multiplayer.Client != null && Find.CurrentMap != null)
            {
                var async = Find.CurrentMap.AsyncTime();
                StringBuilder text = new StringBuilder();
                text.Append(async.mapTicks);

                text.Append($" d: {Find.CurrentMap.designationManager.allDesignations.Count}");

                if (Find.CurrentMap.ParentFaction != null)
                {
                    int faction = Find.CurrentMap.ParentFaction.loadID;
                    MultiplayerMapComp comp = Find.CurrentMap.MpComp();
                    FactionMapData data = comp.factionMapData.GetValueSafe(faction);

                    if (data != null)
                    {
                        text.Append($" h: {data.listerHaulables.ThingsPotentiallyNeedingHauling().Count}");
                        text.Append($" sg: {data.haulDestinationManager.AllGroupsListForReading.Count}");
                    }
                }

                text.Append($" {Multiplayer.GlobalIdBlock.current}");

                text.Append($"\n{Sync.bufferedChanges.Sum(kv => kv.Value.Count)}");
                text.Append($"\n{((uint)async.randState)} {(uint)(async.randState >> 32)}");
                text.Append($"\n{(uint)Multiplayer.WorldComp.randState} {(uint)(Multiplayer.WorldComp.randState >> 32)}");
                text.Append($"\n{async.cmds.Count} {Multiplayer.WorldComp.cmds.Count} {async.slower.ForcedNormalSpeed}");

                Rect rect1 = new Rect(80f, 110f, 330f, Text.CalcHeight(text.ToString(), 330f));
                Widgets.Label(rect1, text.ToString());
            }
        }

        static void DoButtons()
        {
            float y = 10f;
            const float btnWidth = 60f;

            if (Multiplayer.session != null && !Multiplayer.IsReplay)
            {
                var btnRect = new Rect(UI.screenWidth - btnWidth - 10f, y, btnWidth, 25f);

                if (Widgets.ButtonText(btnRect, $"{"MpChatButton".Translate()}{(Multiplayer.session.hasUnread ? "*" : "")}"))
                    Find.WindowStack.Add(new ChatWindow());

                if (TickPatch.skipTo < 0)
                {
                    IndicatorInfo(out Color color, out string text);

                    var indRect = new Rect(btnRect.x - 25f - 5f + 6f / 2f, btnRect.y + 6f / 2f, 19f, 19f);
                    Widgets.DrawRectFast(new Rect(btnRect.x - 25f - 5f + 2f / 2f, btnRect.y + 2f / 2f, 23f, 23f), new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f));
                    Widgets.DrawRectFast(indRect, color);
                    TooltipHandler.TipRegion(indRect, new TipSignal(text, 31641624));
                }

                y += 25f;
            }

            if (MpVersion.IsDebug && Multiplayer.PacketLog != null)
            {
                if (Widgets.ButtonText(new Rect(UI.screenWidth - btnWidth - 10f, y, btnWidth, 25f), "Packets"))
                    Find.WindowStack.Add(Multiplayer.PacketLog);
                y += 25f;
            }

            if (Multiplayer.Client != null && Multiplayer.WorldComp.trading.Any())
            {
                if (Widgets.ButtonText(new Rect(UI.screenWidth - btnWidth - 10f, y, btnWidth, 25f), "MpTradingButton".Translate()))
                    Find.WindowStack.Add(new TradingWindow());
                y += 25f;
            }
        }

        static void IndicatorInfo(out Color color, out string text)
        {
            int behind = TickPatch.tickUntil - TickPatch.Timer;
            text = "MpTicksBehind".Translate(behind);

            if (behind > 30)
            {
                color = new Color(0.9f, 0, 0);
                text += $"\n\n{"MpLowerGameSpeed".Translate()}";
            }
            else if (behind > 15)
            {
                color = Color.yellow;
            }
            else
            {
                color = new Color(0.0f, 0.8f, 0.0f);
            }
        }

        const float TimelineMargin = 50f;
        const float TimelineHeight = 35f;

        static void DrawTimeline()
        {
            Rect rect = new Rect(TimelineMargin, UI.screenHeight - 35f - TimelineHeight - 10f - 30f, UI.screenWidth - TimelineMargin * 2, TimelineHeight + 30f);
            Find.WindowStack.ImmediateWindow(TimelineWindowId, rect, WindowLayer.SubSuper, DrawTimelineWindow, doBackground: false, shadowAlpha: 0);
        }

        static void DrawTimelineWindow()
        {
            if (Multiplayer.Client == null) return;

            Rect rect = new Rect(0, 30f, UI.screenWidth - TimelineMargin * 2, TimelineHeight);

            Widgets.DrawBoxSolid(rect, new Color(0.6f, 0.6f, 0.6f, 0.8f));

            int timerStart = Multiplayer.session.replayTimerStart >= 0 ? Multiplayer.session.replayTimerStart : OnMainThread.cachedAtTime;
            int timerEnd = Multiplayer.session.replayTimerEnd >= 0 ? Multiplayer.session.replayTimerEnd : TickPatch.tickUntil;
            int timeLen = timerEnd - timerStart;

            Widgets.DrawLine(new Vector2(rect.xMin + 2f, rect.yMin), new Vector2(rect.xMin + 2f, rect.yMax), Color.white, 4f);
            Widgets.DrawLine(new Vector2(rect.xMax - 2f, rect.yMin), new Vector2(rect.xMax - 2f, rect.yMax), Color.white, 4f);

            float progress = (TickPatch.Timer - timerStart) / (float)timeLen;
            float progressX = rect.xMin + progress * rect.width;
            Widgets.DrawLine(new Vector2(progressX, rect.yMin), new Vector2(progressX, rect.yMax), Color.green, 7f);

            float mouseX = Event.current.mousePosition.x;
            ReplayEvent mouseEvent = null;

            foreach (var ev in Multiplayer.session.events)
            {
                if (ev.time < timerStart || ev.time > timerEnd)
                    continue;

                var pointX = rect.xMin + (ev.time - timerStart) / (float)timeLen * rect.width;

                //GUI.DrawTexture(new Rect(pointX - 12f, rect.yMin - 24f, 24f, 24f), texture);
                Widgets.DrawLine(new Vector2(pointX, rect.yMin), new Vector2(pointX, rect.yMax), ev.color, 5f);

                if (Mouse.IsOver(rect) && Math.Abs(mouseX - pointX) < 10)
                {
                    mouseX = pointX;
                    mouseEvent = ev;
                }
            }

            if (Mouse.IsOver(rect))
            {
                float mouseProgress = (mouseX - rect.xMin) / rect.width;
                int mouseTimer = timerStart + (int)(timeLen * mouseProgress);

                Widgets.DrawLine(new Vector2(mouseX, rect.yMin), new Vector2(mouseX, rect.yMax), Color.blue, 3f);

                if (Event.current.type == EventType.MouseUp)
                {
                    TickPatch.skipTo = mouseTimer;

                    if (mouseTimer < TickPatch.Timer)
                    {
                        ClientJoiningState.ReloadGame(OnMainThread.cachedMapData.Keys.ToList(), false);
                    }
                }

                if (Event.current.isMouse)
                    Event.current.Use();

                string tooltip = $"Tick {mouseTimer}";
                if (mouseEvent != null)
                    tooltip = $"{mouseEvent.name}\n{tooltip}";

                TooltipHandler.TipRegion(rect, new TipSignal(tooltip, 215462143));
                // No delay between the mouseover and showing
                if (TooltipHandler.activeTips.TryGetValue(215462143, out ActiveTip tip))
                    tip.firstTriggerTime = 0;
            }

            if (TickPatch.skipTo >= 0)
            {
                float pct = (TickPatch.skipTo - timerStart) / (float)timeLen;
                float skipToX = rect.xMin + rect.width * pct;
                Widgets.DrawLine(new Vector2(skipToX, rect.yMin), new Vector2(skipToX, rect.yMax), Color.yellow, 4f);
            }
        }

        public const int SkippingWindowId = 26461263;
        public const int TimelineWindowId = 5723681;

        static void DrawSkippingWindow()
        {
            if (Multiplayer.Client == null || TickPatch.skipTo < 0) return;

            string text = $"{"MpSimulating".Translate()}{MpUtil.FixedEllipsis()}";
            float textWidth = Text.CalcSize(text).x;
            float windowWidth = Math.Max(240f, textWidth + 40f);
            Rect rect = new Rect(0, 0, windowWidth, 75f).CenterOn(new Rect(0, 0, UI.screenWidth, UI.screenHeight));

            if (Multiplayer.IsReplay && !TickPatch.disableSkipCancel && Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Escape)
            {
                TickPatch.ClearSkipping();
                Event.current.Use();
            }

            Find.WindowStack.ImmediateWindow(SkippingWindowId, rect, WindowLayer.Super, () =>
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Small;
                Widgets.Label(rect.AtZero(), text);
                Text.Anchor = TextAnchor.UpperLeft;
            }, absorbInputAroundWindow: true);
        }
    }

    [MpPatch(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI))]
    [MpPatch(typeof(GlobalControls), nameof(GlobalControls.GlobalControlsOnGUI))]
    static class MakeSpaceForReplayTimeline
    {
        static void Prefix()
        {
            if (Multiplayer.IsReplay)
                UI.screenHeight -= 45;
        }

        static void Postfix()
        {
            if (Multiplayer.IsReplay)
                UI.screenHeight += 45;
        }
    }
}
